using System;
using NUnit.Framework;
using UnityEngine;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Tests for the spawn-ring math that positions enemies around the player.
    /// </summary>
    public class HordePocLayoutTests
    {
        const float k_Tolerance = 1e-4f;

        [Test]
        public void RingPosition_PlacesEveryPointAtTheRequestedRadius()
        {
            const float radius = 4.5f;
            const int count = 7;

            for (int i = 0; i < count; i++)
            {
                var position = HordePocLayout.RingPosition(i, count, radius);
                Assert.That(position.magnitude, Is.EqualTo(radius).Within(k_Tolerance),
                    $"Position {i} is not on the ring.");
            }
        }

        [Test]
        public void RingPosition_KeepsPointsHorizontal()
        {
            for (int i = 0; i < 5; i++)
            {
                var position = HordePocLayout.RingPosition(i, 5, 3f);
                Assert.That(position.y, Is.EqualTo(0f).Within(k_Tolerance),
                    "Ring positions must be level; height is applied by the caller.");
            }
        }

        [Test]
        public void RingPosition_FirstPointIsOnPositiveZ()
        {
            var position = HordePocLayout.RingPosition(0, 8, 2f);

            Assert.That(position.x, Is.EqualTo(0f).Within(k_Tolerance));
            Assert.That(position.z, Is.EqualTo(2f).Within(k_Tolerance));
        }

        [Test]
        public void RingPosition_SpacesPointsEvenly()
        {
            const int count = 6;
            const float expectedSpacingDegrees = 360f / count;

            for (int i = 0; i < count; i++)
            {
                var current = HordePocLayout.RingPosition(i, count, 5f);
                var next = HordePocLayout.RingPosition(i + 1, count, 5f);

                Assert.That(Vector3.Angle(current, next), Is.EqualTo(expectedSpacingDegrees).Within(1e-2f),
                    $"Spacing between {i} and {i + 1} is uneven.");
            }
        }

        [Test]
        public void RingPosition_AdvancesClockwiseSeenFromAbove()
        {
            // Clockwise from above means the second point moves toward +X.
            var second = HordePocLayout.RingPosition(1, 4, 1f);

            Assert.That(second.x, Is.EqualTo(1f).Within(k_Tolerance));
            Assert.That(second.z, Is.EqualTo(0f).Within(k_Tolerance));
        }

        [Test]
        public void RingPosition_WrapsIndexesBeyondTheCount()
        {
            var first = HordePocLayout.RingPosition(1, 4, 3f);
            var wrapped = HordePocLayout.RingPosition(9, 4, 3f); // 9 mod 4 == 1

            Assert.That(wrapped, Is.EqualTo(first).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void RingPosition_WrapsNegativeIndexes()
        {
            var third = HordePocLayout.RingPosition(3, 4, 3f);
            var negative = HordePocLayout.RingPosition(-1, 4, 3f); // -1 wraps to 3

            Assert.That(negative, Is.EqualTo(third).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void RingPosition_CollapsesToTheCenterWhenRadiusIsZero()
        {
            var position = HordePocLayout.RingPosition(2, 5, 0f);

            Assert.That(position.magnitude, Is.EqualTo(0f).Within(k_Tolerance));
        }

        [Test]
        public void RingPosition_RejectsNonPositiveCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HordePocLayout.RingPosition(0, 0, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => HordePocLayout.RingPosition(0, -3, 1f));
        }

        [Test]
        public void RingPosition_RejectsNegativeRadius()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HordePocLayout.RingPosition(0, 4, -1f));
        }

        /// <summary>Compares vectors with a tolerance, since ring math goes through trig.</summary>
        class Vector3EqualityComparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            public static readonly Vector3EqualityComparer Instance = new Vector3EqualityComparer();

            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < k_Tolerance;
            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }
    }
}
