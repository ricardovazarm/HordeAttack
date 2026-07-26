using System;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Tunables that turn a hand's speed into damage, knockback and haptics.
    /// </summary>
    /// <remarks>
    /// Kept as serializable data separate from the components that use it so the punch maths in
    /// <see cref="PunchResolver"/> stays pure and testable, and so the fist and any future weapon
    /// can be tuned from the inspector against the same model.
    /// </remarks>
    [Serializable]
    public class PunchSettings
    {
        [Header("Detección")]
        [SerializeField]
        [Tooltip("Velocidad de mano por debajo de la cual el contacto es un roce y no un golpe (m/s).")]
        float m_MinSpeed = 1.5f;

        [SerializeField]
        [Tooltip("Velocidad de mano a la que el golpe ya es de potencia máxima (m/s). Por encima no pega más fuerte.")]
        float m_MaxSpeed = 7.5f;

        [Header("Daño")]
        [SerializeField]
        [Tooltip("Daño de un golpe a potencia máxima. Un golpe a media potencia o menos hace 1.")]
        int m_MaxDamage = 2;

        [Header("Knockback")]
        [SerializeField]
        [Tooltip("Impulso de knockback por cada m/s de mano (N·s por m/s). Es, en la práctica, la masa efectiva del puño.")]
        float m_ImpulsePerSpeed = 15f;

        [SerializeField]
        [Tooltip("Cuánto del knockback se redirige hacia arriba, para que el enemigo despegue del suelo en vez de resbalar.")]
        float m_UpwardBias = 0.35f;

        [Header("Haptics")]
        [SerializeField]
        [Tooltip("Amplitud de vibración del golpe más flojo que aún cuenta (0-1).")]
        float m_MinHapticAmplitude = 0.3f;

        [SerializeField]
        [Tooltip("Duración del pulso de vibración, en segundos.")]
        float m_HapticDuration = 0.08f;

        /// <summary>Creates settings with the tuned defaults. Required by Unity's serializer.</summary>
        public PunchSettings()
        {
        }

        /// <summary>
        /// Creates settings explicitly, for callers that build a punch model in code rather than
        /// in the inspector.
        /// </summary>
        /// <remarks>
        /// Every parameter defaults to the tuned value, so a caller that only cares about, say, the
        /// threshold can name that one alone and leave the rest of the model alone with it.
        /// </remarks>
        public PunchSettings(
            float minSpeed = 1.5f,
            float maxSpeed = 7.5f,
            int maxDamage = 2,
            float impulsePerSpeed = 15f,
            float upwardBias = 0.35f,
            float minHapticAmplitude = 0.3f,
            float hapticDuration = 0.08f)
        {
            m_MinSpeed = minSpeed;
            m_MaxSpeed = maxSpeed;
            m_MaxDamage = maxDamage;
            m_ImpulsePerSpeed = impulsePerSpeed;
            m_UpwardBias = upwardBias;
            m_MinHapticAmplitude = minHapticAmplitude;
            m_HapticDuration = hapticDuration;
        }

        /// <summary>Hand speed at or above which contact counts as a punch, in m/s.</summary>
        public float minSpeed => m_MinSpeed;

        /// <summary>Hand speed at which a punch reaches full power, in m/s.</summary>
        public float maxSpeed => m_MaxSpeed;

        /// <summary>Damage dealt by a full power punch. Every landed punch deals at least 1.</summary>
        public int maxDamage => m_MaxDamage;

        /// <summary>Knockback impulse per m/s of hand speed, in N·s per m/s.</summary>
        public float impulsePerSpeed => m_ImpulsePerSpeed;

        /// <summary>Share of the knockback redirected upward.</summary>
        public float upwardBias => m_UpwardBias;

        /// <summary>Haptic amplitude of the weakest punch that still lands, in 0..1.</summary>
        public float minHapticAmplitude => m_MinHapticAmplitude;

        /// <summary>Haptic pulse length, in seconds.</summary>
        public float hapticDuration => m_HapticDuration;

        /// <summary>
        /// Forces the values into a range the resolver can work with.
        /// </summary>
        /// <remarks>
        /// Called from <c>OnValidate</c> on the owning components. A designer dragging
        /// <see cref="maxSpeed"/> below <see cref="minSpeed"/> would otherwise leave an inverted
        /// range in which no punch ever reaches full power, which is hard to spot in the headset.
        /// </remarks>
        public void Clamp()
        {
            m_MinSpeed = Mathf.Max(0f, m_MinSpeed);
            m_MaxSpeed = Mathf.Max(m_MinSpeed + k_MinimumSpeedSpan, m_MaxSpeed);
            m_MaxDamage = Mathf.Max(1, m_MaxDamage);
            m_ImpulsePerSpeed = Mathf.Max(0f, m_ImpulsePerSpeed);
            m_UpwardBias = Mathf.Max(0f, m_UpwardBias);
            m_MinHapticAmplitude = Mathf.Clamp01(m_MinHapticAmplitude);
            m_HapticDuration = Mathf.Max(0f, m_HapticDuration);
        }

        /// <summary>
        /// Smallest gap allowed between <see cref="minSpeed"/> and <see cref="maxSpeed"/>, so the
        /// power ramp never collapses to a single speed.
        /// </summary>
        public const float k_MinimumSpeedSpan = 0.1f;
    }
}
