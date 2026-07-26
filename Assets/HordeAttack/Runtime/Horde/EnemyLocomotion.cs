using System.Collections.Generic;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Walks an enemy toward the nearest player and gets it to take hold, on foot or through the
    /// air.
    /// </summary>
    /// <remarks>
    /// The decisions live in <see cref="HordeSteering"/> and <see cref="LatchAnchorSelector"/>; this
    /// is the component that runs them against a real Rigidbody and a real clock.
    /// <para>
    /// It drives the body by writing velocity rather than by applying forces. Force-driven walkers
    /// need drag, mass and friction tuned against each other before they stop sliding about, and
    /// none of that is worth doing for creatures whose whole job is to close a few meters in a
    /// straight line. The cost is that the enemy has to be handed back to physics whenever
    /// something else should be moving it — which is exactly what the knockback window is.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HordeEnemy), typeof(Rigidbody))]
    public class EnemyLocomotion : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Cómo se te echa encima este enemigo. Los saltadores brincan a las anclas altas; los aferradores llegan a pie a las bajas.")]
        LatchStyle m_Style = LatchStyle.Clinger;

        [SerializeField]
        [Tooltip("Velocidades, distancias y tiempos del avance y del salto.")]
        EnemyLocomotionSettings m_Settings = new EnemyLocomotionSettings();

        readonly List<Vector3> m_Neighbours = new List<Vector3>();

        HordeEnemy m_Enemy;
        Rigidbody m_Body;
        PlayerLatchTarget m_Target;
        LatchAnchor m_LeapAnchor;

        float m_RecoveredAt;
        float m_LeapStartedAt;
        Vector3 m_LeapFrom;

        /// <summary>How this enemy goes about taking hold.</summary>
        public LatchStyle style
        {
            get => m_Style;
            set => m_Style = value;
        }

        /// <summary>The movement model this enemy runs on.</summary>
        public EnemyLocomotionSettings settings => m_Settings;

        /// <summary>Whether this enemy is mid-air on a committed jump.</summary>
        public bool isLeaping => m_LeapAnchor != null;

        /// <summary>Whether physics currently owns the body because the enemy was just punched.</summary>
        public bool isRecovering => Time.time < m_RecoveredAt;

        /// <inheritdoc/>
        void Awake()
        {
            m_Enemy = GetComponent<HordeEnemy>();
            m_Body = GetComponent<Rigidbody>();
            m_Settings?.Clamp();
        }

        /// <inheritdoc/>
        void OnValidate() => m_Settings?.Clamp();

        /// <inheritdoc/>
        void OnDisable()
        {
            // An enemy switched off mid-jump would otherwise leave its destination reserved for
            // good, and the player would slowly run out of places to be grabbed.
            AbandonLeap();
        }

        /// <inheritdoc/>
        void FixedUpdate()
        {
            if (m_Enemy == null || !m_Enemy.isAlive)
            {
                AbandonLeap();
                return;
            }

            // Already holding on: nothing to drive. The enemy rides the anchor's transform.
            if (m_Enemy.isLatched)
            {
                AbandonLeap();
                return;
            }

            if (isLeaping)
            {
                AdvanceLeap();
                return;
            }

            // Physics owns the body while the knockback plays out. Writing a walking velocity here
            // would erase the punch on the very next step, and the enemy would appear to shrug it
            // off without moving.
            if (isRecovering)
                return;

            Advance();
        }

        void Advance()
        {
            m_Target = PlayerLatchTarget.FindNearest(transform.position);
            if (m_Target == null)
                return;

            var body = m_Target.bodyPosition;
            float distance = HorizontalDistance(transform.position, body);

            if (distance <= m_Settings.latchRange && m_Target.TryLatch(m_Enemy, m_Style))
                return;

            if (m_Style == LatchStyle.Leaper
                && HordeSteering.IsInLeapRange(distance, m_Settings.minLeapRange, m_Settings.maxLeapRange)
                && TryBeginLeap())
                return;

            // Falling through to walking is deliberate. An enemy that is in range but found every
            // anchor taken keeps pressing in rather than standing still, which is what makes a
            // crowded player feel surrounded instead of politely queued for.
            Walk(body);
        }

        void Walk(Vector3 targetPosition)
        {
            CollectNeighbours();

            var heading = HordeSteering.Steer(
                transform.position, targetPosition, m_Neighbours,
                m_Settings.separationRadius, m_Settings.separationWeight);

            if (heading == Vector3.zero)
                return;

            // Freezing rotation while walking is what keeps a capsule on its feet. It is lifted the
            // moment a punch lands, so being knocked over still tumbles the enemy properly.
            m_Body.constraints = RigidbodyConstraints.FreezeRotation;

            var velocity = m_Body.linearVelocity;
            m_Body.linearVelocity = new Vector3(
                heading.x * m_Settings.moveSpeed, velocity.y, heading.z * m_Settings.moveSpeed);

            m_Body.MoveRotation(Quaternion.RotateTowards(
                m_Body.rotation,
                Quaternion.LookRotation(heading, Vector3.up),
                m_Settings.turnSpeed * Time.fixedDeltaTime));
        }

        /// <summary>
        /// Collects where the other enemies are, so this one can push away from them.
        /// </summary>
        /// <remarks>
        /// Enemies already holding on to a player are left out. They are standing on the exact spot
        /// everyone else is trying to reach, and counting them would push arriving enemies away
        /// from the player instead of around each other.
        /// </remarks>
        void CollectNeighbours()
        {
            m_Neighbours.Clear();

            var all = HordeEnemy.all;
            for (int i = 0; i < all.Count; i++)
            {
                var other = all[i];
                if (other == null || other == m_Enemy || !other.isAlive || other.isLatched)
                    continue;

                m_Neighbours.Add(other.transform.position);
            }
        }

        bool TryBeginLeap()
        {
            if (!m_Target.TryReserve(m_Enemy, m_Style, out m_LeapAnchor))
                return false;

            m_LeapStartedAt = Time.time;
            m_LeapFrom = transform.position;

            // Kinematic for the arc: the jump is scripted, and gravity or a collision partway
            // through would pull it off the line it needs to end on.
            m_Body.constraints = RigidbodyConstraints.None;
            m_Body.linearVelocity = Vector3.zero;
            m_Body.angularVelocity = Vector3.zero;
            m_Body.isKinematic = true;

            return true;
        }

        void AdvanceLeap()
        {
            // The reservation is checked every step rather than trusted for the whole jump: the
            // player can walk out of range, and the anchor's branch can be switched off underneath
            // it when the headset flips between controllers and hand tracking.
            if (m_Target == null || m_LeapAnchor == null || m_LeapAnchor.occupant != m_Enemy)
            {
                AbandonLeap();
                return;
            }

            float progress = (Time.time - m_LeapStartedAt) / m_Settings.leapDuration;

            // Sampled every step so the arc follows a player who is walking or turning, instead of
            // landing where they were standing when the jump started.
            var destination = m_LeapAnchor.transform.position + Vector3.down * m_LeapAnchor.hangDrop;
            var point = HordeSteering.LeapPoint(m_LeapFrom, destination, progress, m_Settings.leapArcHeight);

            m_Body.MovePosition(point);
            FaceHorizontally(destination - transform.position);

            if (progress < 1f)
                return;

            var anchor = m_LeapAnchor;
            m_LeapAnchor = null;

            if (m_Target.CompleteLatch(m_Enemy, anchor))
                return;

            // Landed on nothing. Hand the body back to physics so it drops rather than hanging in
            // the air where the anchor used to be.
            m_Target.CancelReservation(m_Enemy, anchor);
            ReturnToPhysics();
        }

        /// <summary>
        /// Drops a jump in progress and gives its destination back.
        /// </summary>
        void AbandonLeap()
        {
            if (m_LeapAnchor == null)
                return;

            var anchor = m_LeapAnchor;
            m_LeapAnchor = null;

            if (m_Target != null)
                m_Target.CancelReservation(m_Enemy, anchor);
            else
                anchor.Release(m_Enemy);

            ReturnToPhysics();
        }

        /// <summary>
        /// Hands the body back to physics and stops driving it for a moment, so a punch can be seen.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="HordeEnemy.ReceivePunch"/> immediately before the knockback impulse,
        /// and it has to be: an enemy caught mid-leap is kinematic, and <c>AddForce</c> on a
        /// kinematic body is discarded without a word. The punch would take health off a creature
        /// that then calmly completed its jump onto the player who threw it.
        /// </remarks>
        public void ReleaseForKnockback()
        {
            AbandonLeap();
            ReturnToPhysics();

            m_RecoveredAt = Time.time + m_Settings.knockbackRecovery;
        }

        /// <summary>
        /// Puts the enemy back under its own control, for an enemy that has just respawned.
        /// </summary>
        public void ResetForRespawn()
        {
            AbandonLeap();
            ReturnToPhysics();

            m_RecoveredAt = 0f;
        }

        /// <summary>
        /// Returns the Rigidbody to a state where forces and gravity apply to it again.
        /// </summary>
        /// <remarks>
        /// Constraints are cleared as well as the kinematic flag. A body still frozen upright would
        /// slide away from a punch without ever tipping over, and a punch that does not visibly
        /// knock a creature about does not feel like it connected.
        /// </remarks>
        void ReturnToPhysics()
        {
            if (m_Body == null)
                return;

            m_Body.isKinematic = false;
            m_Body.constraints = RigidbodyConstraints.None;
        }

        void FaceHorizontally(Vector3 direction)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude <= 1e-6f)
                return;

            m_Body.MoveRotation(Quaternion.LookRotation(direction.normalized, Vector3.up));
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }
    }
}
