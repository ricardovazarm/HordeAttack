using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Turns a collision this enemy is involved in into damage, for both sides of it.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="PunchDetector"/> for the other way of dealing with a creature:
    /// pull it off with the grip and throw it at the ones still coming. The judgement lives in
    /// <see cref="ImpactResolver"/>; this component is what feeds it a real collision.
    /// <para>
    /// Sits on every enemy rather than only on thrown ones, because "thrown" is not a state anything
    /// tracks — an enemy sent flying by a punch is just as much a projectile as one the player let
    /// go of, and that is the shot most of the horde will actually be killed by.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class ImpactDetector : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Modelo de impacto: a qué velocidad un choque cuenta y cuánto daño hace.")]
        ImpactSettings m_Settings = new ImpactSettings();

        HordeEnemy m_Enemy;

        /// <summary>The impact model this enemy collides under.</summary>
        public ImpactSettings settings => m_Settings;

        /// <inheritdoc/>
        void Awake()
        {
            TryGetComponent(out m_Enemy);
            m_Settings?.Clamp();
        }

        /// <inheritdoc/>
        void OnValidate() => m_Settings?.Clamp();

        /// <inheritdoc/>
        void OnCollisionEnter(Collision collision)
        {
            if (!CanBeInvolved(m_Enemy))
                return;

            float speed = ImpactResolver.ApproachSpeed(collision.relativeVelocity, ContactNormal(collision));

            // Anything without a HordeEnemy above it is the world: the arena floor today, scenery
            // later. Throwing a creature into it has to count, or spiking one head first into the
            // ground would read as the game not having noticed.
            var other = collision.collider != null
                ? collision.collider.GetComponentInParent<HordeEnemy>()
                : null;

            if (other == null)
            {
                Resolve(speed, ImpactKind.Ground, m_Enemy);
                return;
            }

            if (!CanBeInvolved(other))
                return;

            // Unity delivers the same collision to both bodies, so without this the pair would be
            // charged twice over. Picking by instance id rather than by "who was moving faster"
            // keeps it decidable: ids are unique, so exactly one of the two resolves and neither
            // has to agree with the other about who threw whom.
            if (m_Enemy.GetInstanceID() > other.GetInstanceID())
                return;

            Resolve(speed, ImpactKind.Creature, m_Enemy, other);
        }

        /// <summary>
        /// Applies an impact of <paramref name="speed"/> to everyone it touches.
        /// </summary>
        /// <remarks>
        /// No knockback is applied by hand. Both bodies are dynamic and the physics engine has
        /// already resolved the collision by the time this runs, so adding a scripted impulse on top
        /// would double it. The one case that does not tumble is a creature that was clinging to the
        /// player: it is kinematic at the moment of contact, so it takes no impulse and simply drops
        /// once <see cref="HordeEnemy.ReceiveImpact"/> lets go of the anchor. Being knocked off is
        /// the part that matters there.
        /// </remarks>
        void Resolve(float speed, ImpactKind kind, HordeEnemy thrown, HordeEnemy struck = null)
        {
            var outcome = ImpactResolver.Resolve(speed, kind, m_Settings);
            if (!outcome.landed)
                return;

            thrown.ReceiveImpact(outcome.damage);
            struck?.ReceiveImpact(outcome.damage);
        }

        /// <summary>
        /// Whether <paramref name="enemy"/> is in a state where a collision means anything.
        /// </summary>
        /// <remarks>
        /// An enemy held in someone's hand is excluded on both sides. Velocity tracking drives a held
        /// body straight through whatever is in the way, so counting those contacts would let a
        /// player mow the horde down by walking into it with a gnome in their fist — and it would
        /// also chip away at the creature they are holding for free. It has to be let go of to be a
        /// weapon.
        /// </remarks>
        static bool CanBeInvolved(HordeEnemy enemy) => enemy != null && enemy.isAlive && !enemy.isHeld;

        /// <summary>
        /// Normal at the point of contact, or the direction of travel when there is no contact to
        /// read.
        /// </summary>
        /// <remarks>
        /// Speculative contacts can report a collision with no contact points. Falling back to the
        /// relative velocity makes <see cref="ImpactResolver.ApproachSpeed"/> return the full closing
        /// speed, which errs toward counting the impact rather than dropping it silently.
        /// </remarks>
        static Vector3 ContactNormal(Collision collision) =>
            collision.contactCount > 0 ? collision.GetContact(0).normal : collision.relativeVelocity;
    }
}
