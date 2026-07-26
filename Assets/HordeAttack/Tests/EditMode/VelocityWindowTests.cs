using System;
using NUnit.Framework;
using UnityEngine;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Tests for the sliding-window velocity estimate that punch detection thresholds on.
    /// </summary>
    public class VelocityWindowTests
    {
        const float k_Tolerance = 1e-3f;

        /// <summary>A plausible headset frame time, 72 Hz.</summary>
        const float k_FrameTime = 1f / 72f;

        [Test]
        public void Velocity_IsZeroBeforeThereIsAnythingToMeasure()
        {
            var window = new VelocityWindow();

            Assert.That(window.velocity, Is.EqualTo(Vector3.zero));
            Assert.That(window.sampleCount, Is.Zero);
        }

        [Test]
        public void Velocity_IsZeroFromASingleSample()
        {
            var window = new VelocityWindow();
            window.AddSample(new Vector3(1f, 2f, 3f), 0f);

            Assert.That(window.velocity, Is.EqualTo(Vector3.zero),
                "One position cannot imply a direction of travel.");
        }

        [Test]
        public void Velocity_MeasuresSteadyMotion()
        {
            var window = new VelocityWindow();
            var expected = new Vector3(0f, 0f, 4f);

            Move(window, expected, frames: 6);

            Assert.That(window.speed, Is.EqualTo(expected.magnitude).Within(k_Tolerance));
            Assert.That(window.velocity.normalized, Is.EqualTo(expected.normalized).Using(Vector3Comparer.Instance));
        }

        [Test]
        public void Velocity_IsZeroForAHandThatIsNotMoving()
        {
            var window = new VelocityWindow();

            Move(window, Vector3.zero, frames: 8);

            Assert.That(window.speed, Is.EqualTo(0f).Within(k_Tolerance));
        }

        /// <summary>
        /// The reason this class exists. A dropped or mispredicted tracking frame moves the hand a
        /// long way in a single frame and back again; read as a frame delta that is tens of meters
        /// per second, which would land a maximum-power punch the player never threw.
        /// </summary>
        /// <summary>
        /// The reason this class exists. A dropped or mispredicted tracking frame moves the hand a
        /// long way in one frame and back again; read as a frame delta that is tens of meters per
        /// second, and it would land a maximum-power punch the player never threw.
        /// </summary>
        [Test]
        public void Velocity_SwallowsATrackingGlitchInTheMiddleOfTheWindow()
        {
            var window = new VelocityWindow();
            var drift = new Vector3(0f, 0f, 1f);

            Move(window, drift, frames: 3);
            float naiveSpeed = Glitch(window, new Vector3(0.2f, 0f, 0f));
            Move(window, drift, frames: 3);

            Assert.That(naiveSpeed, Is.GreaterThan(10f),
                "The fixture is wrong: this glitch is not violent enough to be worth smoothing.");
            Assert.That(window.speed, Is.EqualTo(drift.magnitude).Within(0.5f),
                $"A one-frame glitch dragged the reading to {window.speed:F1} m/s; the hand was " +
                $"really moving at {drift.magnitude} m/s.");
        }

        /// <summary>
        /// The awkward case: the glitch is the newest sample, so there is nothing after it to
        /// average against. It cannot be erased, but it must not be taken at face value either —
        /// a one-frame delta would read this as a punch several times faster than a human throws.
        /// </summary>
        [Test]
        public void Velocity_DampsATrackingGlitchOnTheNewestSample()
        {
            var window = new VelocityWindow();

            Move(window, Vector3.zero, frames: 6);
            float naiveSpeed = Glitch(window, new Vector3(0.5f, 0f, 0f));

            Assert.That(naiveSpeed, Is.GreaterThan(20f), "The fixture's glitch is too gentle.");
            Assert.That(window.speed, Is.LessThan(naiveSpeed / 3f),
                $"The window reported {window.speed:F1} m/s against a raw frame delta of " +
                $"{naiveSpeed:F1} m/s, so it is barely smoothing at all.");
        }

        [Test]
        public void Velocity_ForgetsMotionOlderThanTheWindow()
        {
            var window = new VelocityWindow(windowSeconds: 0.1f);

            // A fast swing, then a full window's worth of standing still.
            Move(window, new Vector3(0f, 0f, 8f), frames: 4);
            Move(window, Vector3.zero, frames: 12);

            Assert.That(window.speed, Is.LessThan(0.5f),
                "The window is still reporting a swing that finished more than a window ago.");
        }

        [Test]
        public void Velocity_KeepsTheWindowBoundedRegardlessOfHowLongItRuns()
        {
            var window = new VelocityWindow(windowSeconds: 0.1f);

            Move(window, new Vector3(1f, 0f, 0f), frames: 500);

            // 0.1 s at 72 Hz is about 8 frames; the exact figure depends on when pruning trims,
            // so assert the bound rather than a count.
            Assert.That(window.sampleCount, Is.LessThanOrEqualTo(12),
                "Samples are accumulating without bound; a long session would leak memory.");
        }

        [Test]
        public void Velocity_StaysReadableWhenTheHandStopsGeneratingNewSamples()
        {
            var window = new VelocityWindow(windowSeconds: 0.05f);
            window.AddSample(Vector3.zero, 0f);
            window.AddSample(Vector3.forward, 1f);

            Assert.That(window.sampleCount, Is.EqualTo(2),
                "Pruning must never leave fewer than the two samples a reading needs.");
            Assert.That(window.speed, Is.EqualTo(1f).Within(k_Tolerance));
        }

        [Test]
        public void AddSample_IgnoresSamplesThatDoNotAdvanceTheClock()
        {
            var window = new VelocityWindow();
            window.AddSample(Vector3.zero, 5f);
            window.AddSample(new Vector3(0f, 0f, 100f), 5f);

            Assert.That(window.sampleCount, Is.EqualTo(1),
                "Two samples sharing a timestamp would divide a displacement by zero elapsed time.");
            Assert.That(window.velocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void AddSample_IgnoresSamplesFromThePast()
        {
            var window = new VelocityWindow();
            window.AddSample(Vector3.zero, 0f);
            window.AddSample(Vector3.forward, 1f);
            window.AddSample(new Vector3(0f, 0f, -50f), 0.5f);

            Assert.That(window.speed, Is.EqualTo(1f).Within(k_Tolerance),
                "An out-of-order sample was allowed to rewrite the reading.");
        }

        [Test]
        public void Reset_ForgetsEverythingMeasuredSoFar()
        {
            var window = new VelocityWindow();
            Move(window, new Vector3(0f, 0f, 5f), frames: 6);
            Assert.That(window.speed, Is.GreaterThan(1f), "The fixture never built up a reading.");

            window.Reset();

            Assert.That(window.sampleCount, Is.Zero);
            Assert.That(window.velocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Constructor_RejectsAWindowThatSpansNoTime()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VelocityWindow(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VelocityWindow(-0.1f));
        }

        /// <summary>
        /// Feeds <paramref name="frames"/> frames of motion at <paramref name="velocity"/>,
        /// continuing from wherever the fixture's hand already is in space and time.
        /// </summary>
        void Move(VelocityWindow window, Vector3 velocity, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                window.AddSample(m_Position, m_Time);
                m_Position += velocity * k_FrameTime;
                m_Time += k_FrameTime;
            }
        }

        /// <summary>
        /// Feeds one frame in which tracking reports the hand <paramref name="offset"/> away from
        /// where it really is, and returns what a naive one-frame delta would have made of it.
        /// </summary>
        float Glitch(VelocityWindow window, Vector3 offset)
        {
            window.AddSample(m_Position + offset, m_Time);
            m_Time += k_FrameTime;

            return offset.magnitude / k_FrameTime;
        }

        Vector3 m_Position;
        float m_Time;

        [SetUp]
        public void SetUp()
        {
            m_Position = Vector3.zero;
            m_Time = 0f;
        }

        class Vector3Comparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            public static readonly Vector3Comparer Instance = new Vector3Comparer();

            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < k_Tolerance;
            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }
    }
}
