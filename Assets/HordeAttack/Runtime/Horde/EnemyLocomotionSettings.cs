using System;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Tunables for how an enemy closes on the player and takes hold.
    /// </summary>
    /// <remarks>
    /// Serializable data kept apart from <see cref="EnemyLocomotion"/> for the same reason
    /// <see cref="PunchSettings"/> is kept apart from the fist: it lets the maths in
    /// <see cref="HordeSteering"/> stay pure, and it means the numbers can be tuned in the
    /// inspector without touching behaviour.
    /// </remarks>
    [Serializable]
    public class EnemyLocomotionSettings
    {
        [Header("Avance")]
        [SerializeField]
        [Tooltip("Velocidad de avance, en m/s. Tope de diseño: 3 m/s. Más rápido y no da tiempo de reaccionar.")]
        float m_MoveSpeed = k_DefaultMoveSpeed;

        [SerializeField]
        [Tooltip("Qué tan rápido se reorienta hacia donde camina, en grados por segundo.")]
        float m_TurnSpeed = 540f;

        [Header("Separación")]
        [SerializeField]
        [Tooltip("Distancia a la que un enemigo empieza a estorbarle a otro, en metros.")]
        float m_SeparationRadius = 0.9f;

        [SerializeField]
        [Tooltip("Cuánto pesa apartarse de los vecinos frente a ir por el jugador. Muy alto = se dispersan y no llegan.")]
        float m_SeparationWeight = 1.3f;

        [Header("Agarre")]
        [SerializeField]
        [Tooltip("Distancia horizontal a la que un enemigo a pie ya puede aferrarse, en metros.")]
        float m_LatchRange = 0.75f;

        [SerializeField]
        [Tooltip("Distancia mínima para saltar. Más cerca que esto, el salto sería un brinco en el lugar.")]
        float m_MinLeapRange = 1f;

        [SerializeField]
        [Tooltip("Distancia máxima que alcanza un salto, en metros.")]
        float m_MaxLeapRange = 2.2f;

        [SerializeField]
        [Tooltip("Cuánto dura el salto, en segundos. Corto se siente a teletransporte; largo, a que flota.")]
        float m_LeapDuration = 0.45f;

        [SerializeField]
        [Tooltip("Altura del arco del salto por encima de la línea recta, en metros.")]
        float m_LeapArcHeight = 0.7f;

        [Header("Recuperación")]
        [SerializeField]
        [Tooltip("Segundos sin control tras recibir un golpe. Es lo que deja que el knockback se vea antes de que el enemigo se reincorpore.")]
        float m_KnockbackRecovery = 0.9f;

        /// <summary>Creates settings with the tuned defaults. Required by Unity's serializer.</summary>
        public EnemyLocomotionSettings()
        {
        }

        /// <summary>
        /// Creates settings explicitly, for callers that build a locomotion model in code rather
        /// than in the inspector.
        /// </summary>
        public EnemyLocomotionSettings(
            float moveSpeed = k_DefaultMoveSpeed,
            float turnSpeed = 540f,
            float separationRadius = 0.9f,
            float separationWeight = 1.3f,
            float latchRange = 0.75f,
            float minLeapRange = 1f,
            float maxLeapRange = 2.2f,
            float leapDuration = 0.45f,
            float leapArcHeight = 0.7f,
            float knockbackRecovery = 0.9f)
        {
            m_MoveSpeed = moveSpeed;
            m_TurnSpeed = turnSpeed;
            m_SeparationRadius = separationRadius;
            m_SeparationWeight = separationWeight;
            m_LatchRange = latchRange;
            m_MinLeapRange = minLeapRange;
            m_MaxLeapRange = maxLeapRange;
            m_LeapDuration = leapDuration;
            m_LeapArcHeight = leapArcHeight;
            m_KnockbackRecovery = knockbackRecovery;
        }

        /// <summary>Advance speed, in m/s.</summary>
        public float moveSpeed => m_MoveSpeed;

        /// <summary>How fast the enemy turns to face where it is going, in degrees per second.</summary>
        public float turnSpeed => m_TurnSpeed;

        /// <summary>Distance at which neighbours start pushing each other apart, in meters.</summary>
        public float separationRadius => m_SeparationRadius;

        /// <summary>Weight of separation against seeking the player.</summary>
        public float separationWeight => m_SeparationWeight;

        /// <summary>Horizontal distance at which an enemy on foot can take hold, in meters.</summary>
        public float latchRange => m_LatchRange;

        /// <summary>Closest distance from which leaping still makes sense, in meters.</summary>
        public float minLeapRange => m_MinLeapRange;

        /// <summary>Furthest distance a leap can cover, in meters.</summary>
        public float maxLeapRange => m_MaxLeapRange;

        /// <summary>How long a leap takes, in seconds.</summary>
        public float leapDuration => m_LeapDuration;

        /// <summary>Peak height of the leap arc above the straight line, in meters.</summary>
        public float leapArcHeight => m_LeapArcHeight;

        /// <summary>Seconds an enemy is left to physics after being punched.</summary>
        public float knockbackRecovery => m_KnockbackRecovery;

        /// <summary>
        /// Forces the values into a range the locomotion can work with.
        /// </summary>
        /// <remarks>
        /// Called from <c>OnValidate</c>. The one that matters is the leap band: a designer dragging
        /// the maximum below the minimum would leave a band no distance can satisfy, so leapers
        /// would silently never jump and would just walk into the player instead — a behaviour
        /// change that looks exactly like a bug in the state machine.
        /// </remarks>
        public void Clamp()
        {
            // Clamped at both ends. The ceiling is the one that matters: it is a design limit on
            // how fast a creature may close on the player, and a limit that only exists in a
            // tooltip is one somebody raises while tuning something else.
            m_MoveSpeed = Mathf.Clamp(m_MoveSpeed, 0f, k_MaxMoveSpeed);
            m_TurnSpeed = Mathf.Max(0f, m_TurnSpeed);
            m_SeparationRadius = Mathf.Max(0f, m_SeparationRadius);
            m_SeparationWeight = Mathf.Max(0f, m_SeparationWeight);
            m_LatchRange = Mathf.Max(0f, m_LatchRange);
            m_MinLeapRange = Mathf.Max(m_LatchRange, m_MinLeapRange);
            m_MaxLeapRange = Mathf.Max(m_MinLeapRange + k_MinimumLeapBand, m_MaxLeapRange);
            m_LeapDuration = Mathf.Max(k_MinimumLeapDuration, m_LeapDuration);
            m_LeapArcHeight = Mathf.Max(0f, m_LeapArcHeight);
            m_KnockbackRecovery = Mathf.Max(0f, m_KnockbackRecovery);
        }

        /// <summary>
        /// Speed an enemy actually advances at, in m/s.
        /// </summary>
        /// <remarks>
        /// A deliberate walk rather than a charge: the horde is threatening because it keeps coming
        /// and there is more of it than you have hands, not because any one creature is fast. Well
        /// under <see cref="k_MaxMoveSpeed"/>, which is the never-exceed rather than the target.
        /// </remarks>
        public const float k_DefaultMoveSpeed = 1.6f;

        /// <summary>
        /// Fastest an enemy may ever advance, in m/s.
        /// </summary>
        /// <remarks>
        /// A design decision, not a physical limit, and a ceiling rather than the speed anything
        /// currently uses. A 1 m creature covering ground faster than this crosses the gap between
        /// "I can see it coming" and "it is already on me" inside a single reaction, and the player
        /// has no answer to it. See <c>PLAN.md</c>, Fase 2a.
        /// </remarks>
        public const float k_MaxMoveSpeed = 3f;

        /// <summary>Smallest leap band allowed, so it never collapses to a single distance.</summary>
        public const float k_MinimumLeapBand = 0.1f;

        /// <summary>Shortest leap allowed, so the arc always spans more than one frame.</summary>
        public const float k_MinimumLeapDuration = 0.05f;
    }
}
