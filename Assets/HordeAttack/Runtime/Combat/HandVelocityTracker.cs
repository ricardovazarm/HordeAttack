using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Keeps a smoothed reading of how fast the hand this sits on is moving through the world.
    /// </summary>
    /// <remarks>
    /// All the maths is in <see cref="VelocityWindow"/>; this is only the Unity plumbing that feeds
    /// it a sample per frame. Sampling happens in <c>LateUpdate</c> because XR pose providers write
    /// the tracked transforms during <c>Update</c>, so reading earlier would measure the previous
    /// frame's pose.
    /// </remarks>
    public class HandVelocityTracker : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Cuánto tiempo hacia atrás promedia la velocidad, en segundos. Muy corto = ruidoso; muy largo = el golpe se siente tarde.")]
        float m_WindowSeconds = VelocityWindow.k_DefaultWindowSeconds;

        [SerializeField]
        [Tooltip("Transform que se sigue. Vacío = este mismo objeto.")]
        Transform m_Tracked;

        VelocityWindow m_Window;

        /// <summary>Smoothed world-space velocity of the tracked point, in m/s.</summary>
        public Vector3 velocity => m_Window?.velocity ?? Vector3.zero;

        /// <summary>Smoothed speed of the tracked point, in m/s.</summary>
        public float speed => velocity.magnitude;

        /// <summary>The transform being followed.</summary>
        public Transform tracked => m_Tracked != null ? m_Tracked : transform;

        /// <inheritdoc/>
        void Awake()
        {
            m_Window = new VelocityWindow(Mathf.Max(k_MinWindowSeconds, m_WindowSeconds));
        }

        /// <inheritdoc/>
        void OnEnable()
        {
            // A hand that was disabled — the headset switched from controllers to hand tracking,
            // say — reappears somewhere else entirely. Carrying the old samples across would read
            // that jump as a punch thrown at tens of meters per second.
            m_Window?.Reset();
        }

        /// <inheritdoc/>
        void LateUpdate()
        {
            // Unscaled: the hand keeps moving through the real world no matter what the game does
            // to its own clock, and scaled time would misreport the speed of the swing.
            m_Window?.AddSample(tracked.position, Time.unscaledTime);
        }

        /// <inheritdoc/>
        void OnValidate()
        {
            m_WindowSeconds = Mathf.Max(k_MinWindowSeconds, m_WindowSeconds);
        }

        /// <summary>Shortest window that still spans more than one frame at headset frame rates.</summary>
        const float k_MinWindowSeconds = 0.01f;
    }
}
