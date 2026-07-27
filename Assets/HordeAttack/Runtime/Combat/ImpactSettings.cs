using System;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Tunables that turn the speed of a collision into damage, for an enemy that was thrown.
    /// </summary>
    /// <remarks>
    /// Kept as serializable data separate from the component that uses it, for the same reason
    /// <see cref="PunchSettings"/> is: the maths in <see cref="ImpactResolver"/> stays pure and
    /// testable, and Fase 4 can move the damage behind an owner RPC without dragging the tuning
    /// along with it.
    /// <para>
    /// The two speed bands are the heart of the tuning and they are deliberately different. Both are
    /// measured as approach speed along the contact normal, but the numbers they have to survive are
    /// not the same:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Ground.</b> A punched enemy is launched at up to 12.5 m/s, comes down along a ballistic
    /// arc and touches the floor with a vertical component of about 4.1 m/s. That must be worth
    /// nothing, or every punch would kill and the "two to three punches" promise of Fase 1 would be
    /// silently gone. <see cref="groundMinSpeed"/> sits well above it, and
    /// <see cref="groundMaxSpeed"/> is low enough that a committed downward slam still kills.
    /// </description></item>
    /// <item><description>
    /// <b>Creature.</b> Here the projectile <em>is</em> a punched enemy, arriving at 8-12 m/s, and
    /// the whole point is that it hurts whatever it lands on. A band that topped out where the
    /// ground one does would make every knocked-back gnome an instant kill on its neighbour, so
    /// <see cref="creatureMaxSpeed"/> is stretched to leave room between an ordinary punch and a
    /// full-power one.
    /// </description></item>
    /// </list>
    /// </remarks>
    [Serializable]
    public class ImpactSettings
    {
        [Header("Contra otro enemigo")]
        [SerializeField]
        [Tooltip("Velocidad de acercamiento por debajo de la cual el choque entre dos enemigos es un empujón y no un impacto (m/s).")]
        float m_CreatureMinSpeed = 6f;

        [SerializeField]
        [Tooltip("Velocidad de acercamiento a la que el impacto entre dos enemigos ya hace el daño máximo (m/s).")]
        float m_CreatureMaxSpeed = 14f;

        [Header("Contra el suelo")]
        [SerializeField]
        [Tooltip("Velocidad contra el suelo por debajo de la cual el golpe no cuenta (m/s). Tiene que quedar POR ENCIMA de la caída de un enemigo golpeado, o cada puñetazo mataría.")]
        float m_GroundMinSpeed = 6f;

        [SerializeField]
        [Tooltip("Velocidad contra el suelo a la que el impacto ya hace el daño máximo (m/s).")]
        float m_GroundMaxSpeed = 10f;

        [Header("Daño")]
        [SerializeField]
        [Tooltip("Daño de un impacto a máxima potencia. En 3 mata de una a un enemigo sano, que es lo que hace que aventar valga la pena.")]
        int m_MaxDamage = 3;

        /// <summary>Creates settings with the tuned defaults. Required by Unity's serializer.</summary>
        public ImpactSettings()
        {
        }

        /// <summary>
        /// Creates settings explicitly, for callers that build an impact model in code rather than
        /// in the inspector.
        /// </summary>
        public ImpactSettings(
            float creatureMinSpeed = 6f,
            float creatureMaxSpeed = 14f,
            float groundMinSpeed = 6f,
            float groundMaxSpeed = 10f,
            int maxDamage = 3)
        {
            m_CreatureMinSpeed = creatureMinSpeed;
            m_CreatureMaxSpeed = creatureMaxSpeed;
            m_GroundMinSpeed = groundMinSpeed;
            m_GroundMaxSpeed = groundMaxSpeed;
            m_MaxDamage = maxDamage;
        }

        /// <summary>Approach speed at or above which two enemies colliding counts, in m/s.</summary>
        public float creatureMinSpeed => m_CreatureMinSpeed;

        /// <summary>Approach speed at which an enemy-on-enemy impact reaches full power, in m/s.</summary>
        public float creatureMaxSpeed => m_CreatureMaxSpeed;

        /// <summary>Approach speed at or above which hitting the ground counts, in m/s.</summary>
        public float groundMinSpeed => m_GroundMinSpeed;

        /// <summary>Approach speed at which hitting the ground reaches full power, in m/s.</summary>
        public float groundMaxSpeed => m_GroundMaxSpeed;

        /// <summary>Damage dealt by a full-power impact. Every impact that lands deals at least 1.</summary>
        public int maxDamage => m_MaxDamage;

        /// <summary>Lowest speed at which <paramref name="kind"/> of impact counts at all, in m/s.</summary>
        public float MinSpeedFor(ImpactKind kind) =>
            kind == ImpactKind.Ground ? m_GroundMinSpeed : m_CreatureMinSpeed;

        /// <summary>Speed at which <paramref name="kind"/> of impact reaches full power, in m/s.</summary>
        public float MaxSpeedFor(ImpactKind kind) =>
            kind == ImpactKind.Ground ? m_GroundMaxSpeed : m_CreatureMaxSpeed;

        /// <summary>
        /// Forces the values into a range the resolver can work with.
        /// </summary>
        /// <remarks>
        /// Called from <c>OnValidate</c> on the owning component, for the same reason
        /// <see cref="PunchSettings.Clamp"/> is: an inverted band makes every impact that lands a
        /// full-power one, and that is invisible until someone dies to a nudge in the headset.
        /// </remarks>
        public void Clamp()
        {
            m_CreatureMinSpeed = Mathf.Max(0f, m_CreatureMinSpeed);
            m_CreatureMaxSpeed = Mathf.Max(m_CreatureMinSpeed + k_MinimumSpeedSpan, m_CreatureMaxSpeed);
            m_GroundMinSpeed = Mathf.Max(0f, m_GroundMinSpeed);
            m_GroundMaxSpeed = Mathf.Max(m_GroundMinSpeed + k_MinimumSpeedSpan, m_GroundMaxSpeed);
            m_MaxDamage = Mathf.Max(1, m_MaxDamage);
        }

        /// <summary>
        /// Smallest gap allowed between the bottom and the top of a speed band, so the power ramp
        /// never collapses onto a single speed.
        /// </summary>
        public const float k_MinimumSpeedSpan = 0.1f;
    }
}
