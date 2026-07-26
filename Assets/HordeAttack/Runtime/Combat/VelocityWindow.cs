using System.Collections.Generic;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Sliding-window estimate of how fast a tracked point is moving.
    /// </summary>
    /// <remarks>
    /// A single frame's position delta is far too noisy to drive punch detection: one dropped
    /// tracking frame or a controller prediction correction reads as tens of meters per second and
    /// would register as a maximum power punch the player never threw. Measuring displacement
    /// across a window instead averages those spikes out, because a glitch that jumps away and
    /// comes back nets out to nothing over the window.
    /// <para>
    /// The window is defined in seconds rather than in samples so the reading means the same thing
    /// at 72 Hz on a standalone headset and at whatever rate the editor runs over Link.
    /// </para>
    /// This is plain C# with no Unity lifecycle so it can be exercised directly in EditMode tests;
    /// <see cref="PointVelocityTracker"/> is the thin component that feeds it.
    /// </remarks>
    public class VelocityWindow
    {
        /// <summary>
        /// Default window length in seconds, a little under a tenth of a second.
        /// </summary>
        /// <remarks>
        /// Long enough to span several frames at headset frame rates, short enough that the
        /// reading still reflects the current swing rather than the previous one.
        /// </remarks>
        public const float k_DefaultWindowSeconds = 0.09f;

        /// <summary>Fewest samples that can produce a reading at all.</summary>
        const int k_MinimumSamples = 2;

        readonly float m_WindowSeconds;
        readonly List<Sample> m_Samples = new List<Sample>();

        /// <param name="windowSeconds">
        /// How far back the window reaches. Must be positive.
        /// </param>
        public VelocityWindow(float windowSeconds = k_DefaultWindowSeconds)
        {
            if (windowSeconds <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(windowSeconds), windowSeconds, "The velocity window must span a positive amount of time.");
            }

            m_WindowSeconds = windowSeconds;
        }

        /// <summary>Length of the window in seconds.</summary>
        public float windowSeconds => m_WindowSeconds;

        /// <summary>Number of samples currently inside the window.</summary>
        public int sampleCount => m_Samples.Count;

        /// <summary>
        /// Velocity across the window, in meters per second, or zero while there is not yet enough
        /// history to measure one.
        /// </summary>
        /// <remarks>
        /// A least-squares fit of position against time rather than the displacement between the
        /// oldest and newest samples. Both smooth a glitch that lands in the middle of the window,
        /// but only the fit smooths one that lands on the newest sample: differencing the endpoints
        /// gives that sample the entire weight of the reading, whereas the fit gives it roughly one
        /// sample's worth. XRI's own <c>AttachPointVelocityTracker</c> fits for the same reason.
        /// </remarks>
        public Vector3 velocity
        {
            get
            {
                int count = m_Samples.Count;
                if (count < k_MinimumSamples)
                    return Vector3.zero;

                float meanTime = 0f;
                var meanPosition = Vector3.zero;

                foreach (var sample in m_Samples)
                {
                    meanTime += sample.time;
                    meanPosition += sample.position;
                }

                meanTime /= count;
                meanPosition /= count;

                float timeVariance = 0f;
                var covariance = Vector3.zero;

                foreach (var sample in m_Samples)
                {
                    float offset = sample.time - meanTime;
                    timeVariance += offset * offset;
                    covariance += offset * (sample.position - meanPosition);
                }

                // Every sample sharing one timestamp: no time passed, so no speed can be inferred.
                if (timeVariance <= 0f)
                    return Vector3.zero;

                return covariance / timeVariance;
            }
        }

        /// <summary>Speed across the window, in meters per second.</summary>
        public float speed => velocity.magnitude;

        /// <summary>
        /// Records where the tracked point was at <paramref name="time"/>.
        /// </summary>
        /// <remarks>
        /// Samples that are not newer than the last one recorded are dropped. Two calls within the
        /// same frame carry the same timestamp, and treating that as elapsed time would divide by
        /// zero.
        /// </remarks>
        /// <param name="position">World position of the tracked point.</param>
        /// <param name="time">Timestamp in seconds, on any clock, as long as it is the same clock every call.</param>
        public void AddSample(Vector3 position, float time)
        {
            if (m_Samples.Count > 0 && time <= m_Samples[m_Samples.Count - 1].time)
                return;

            m_Samples.Add(new Sample(position, time));

            // Keep the oldest two samples regardless of age: a hand that stops moving stops
            // generating new samples in a hurry, and an empty window has no reading to give.
            while (m_Samples.Count > k_MinimumSamples && time - m_Samples[0].time > m_WindowSeconds)
                m_Samples.RemoveAt(0);
        }

        /// <summary>
        /// Forgets all history, so the next reading is built only from samples taken after this
        /// call. Used when the hand is teleported rather than moved, which is not motion the
        /// player made.
        /// </summary>
        public void Reset() => m_Samples.Clear();

        readonly struct Sample
        {
            public readonly Vector3 position;
            public readonly float time;

            public Sample(Vector3 position, float time)
            {
                this.position = position;
                this.time = time;
            }
        }
    }
}
