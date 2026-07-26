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

        [Test]
        public void HandSideOf_ClassifiesEveryAnchorTheBuilderUses()
        {
            foreach (var anchorName in HordePocLayout.k_HandAnchorNames)
            {
                var expected = anchorName.StartsWith("Left") ? HandSide.Left : HandSide.Right;

                Assert.That(HordePocLayout.HandSideOf(anchorName), Is.EqualTo(expected),
                    $"'{anchorName}' was classified as the wrong hand.");
            }
        }

        /// <summary>
        /// Both hand-tracking and controller anchors exist for each side, and the fist hangs off all
        /// four because which pair is live is only known at runtime. Pinning that both spellings of
        /// a side agree is what stops one branch from buzzing the wrong controller.
        /// </summary>
        [Test]
        public void HandSideOf_AgreesAcrossTheControllerAndHandTrackingAnchors()
        {
            Assert.That(HordePocLayout.HandSideOf("Left Controller"),
                Is.EqualTo(HordePocLayout.HandSideOf("Left Hand")));
            Assert.That(HordePocLayout.HandSideOf("Right Controller"),
                Is.EqualTo(HordePocLayout.HandSideOf("Right Hand")));
            Assert.That(HordePocLayout.HandSideOf("Left Controller"),
                Is.Not.EqualTo(HordePocLayout.HandSideOf("Right Controller")));
        }

        [Test]
        public void HandSideOf_RejectsAnythingThatIsNotAHand()
        {
            Assert.Throws<ArgumentException>(() => HordePocLayout.HandSideOf("Camera Offset"));
            Assert.Throws<ArgumentException>(() => HordePocLayout.HandSideOf(""));
            Assert.Throws<ArgumentException>(() => HordePocLayout.HandSideOf(null));
        }

        /// <summary>
        /// The punch trigger is tuned in world meters but stored in the fist's local space, which is
        /// scaled to hand size. Getting the conversion backwards yields a trigger either far too
        /// small to ever connect or wide enough to punch across the room.
        /// </summary>
        [Test]
        public void PunchTriggerLocalRadius_ScalesBackToTheAuthoredWorldRadius()
        {
            float worldRadius = HordePocLayout.k_PunchTriggerLocalRadius * HordePocLayout.k_FistDiameter;

            Assert.That(worldRadius, Is.EqualTo(HordePocLayout.k_PunchTriggerRadius).Within(k_Tolerance));
        }

        [Test]
        public void PunchTriggerRadius_ReachesBeyondTheVisibleFist()
        {
            float fistRadius = HordePocLayout.k_FistDiameter * 0.5f;

            Assert.That(HordePocLayout.k_PunchTriggerRadius, Is.GreaterThan(fistRadius),
                "The trigger is inside the fist mesh, so a punch only registers after it visibly " +
                "clipped through the enemy.");
        }

        /// <summary>
        /// The whole reason the spawn moved out from 3 m: the player has to get a look at the horde
        /// before it arrives. At 3 m the first creature was already holding on before they had
        /// finished putting the headset on.
        /// </summary>
        [Test]
        public void ApproachPosition_StartsEveryEnemyWithinTheRequestedBand()
        {
            for (int i = 0; i < 8; i++)
            {
                float distance = Fan(i, 8).magnitude;

                Assert.That(distance, Is.InRange(
                        HordePocLayout.k_SpawnNearDistance - k_Tolerance,
                        HordePocLayout.k_SpawnFarDistance + k_Tolerance),
                    $"Enemy {i} starts {distance:F2} m away, outside the " +
                    $"{HordePocLayout.k_SpawnNearDistance}-{HordePocLayout.k_SpawnFarDistance} m band.");
            }
        }

        /// <summary>
        /// A row of creatures all the same distance out arrives in step and reads as a formation.
        /// Staggering them is what makes the group look like a horde.
        /// </summary>
        [Test]
        public void ApproachPosition_GivesEveryEnemyItsOwnDistance()
        {
            const int count = 6;
            var distances = new System.Collections.Generic.List<float>();

            for (int i = 0; i < count; i++)
            {
                float distance = Fan(i, count).magnitude;

                foreach (float other in distances)
                {
                    Assert.That(Mathf.Abs(distance - other), Is.GreaterThan(0.1f),
                        $"Two enemies start {distance:F2} m and {other:F2} m out — close enough to " +
                        "arrive together.");
                }

                distances.Add(distance);
            }
        }

        [Test]
        public void ApproachPosition_PutsEveryEnemyInFrontOfThePlayer()
        {
            for (int i = 0; i < 8; i++)
            {
                var position = Fan(i, 8);

                Assert.That(position.z, Is.GreaterThan(0f),
                    $"Enemy {i} starts at {position}, behind or beside the player rather than in " +
                    "front of them.");
            }
        }

        [Test]
        public void ApproachPosition_SpreadsTheFanAcrossBothSides()
        {
            var positions = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 4; i++)
                positions.Add(Fan(i, 4));

            Assert.That(positions.Exists(p => p.x < 0f), Is.True, "Nothing starts on the player's left.");
            Assert.That(positions.Exists(p => p.x > 0f), Is.True, "Nothing starts on the player's right.");
        }

        [Test]
        public void ApproachPosition_KeepsTheFanInsideTheArc()
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = Vector3.Angle(Vector3.forward, Fan(i, 8));

                Assert.That(angle, Is.LessThanOrEqualTo(HordePocLayout.k_SpawnArcDegrees * 0.5f + 1e-2f),
                    $"Enemy {i} starts {angle:F1}° off centre, outside the " +
                    $"{HordePocLayout.k_SpawnArcDegrees}° fan.");
            }
        }

        [Test]
        public void ApproachPosition_PutsASingleEnemyStraightAhead()
        {
            var position = Fan(0, 1);

            Assert.That(position.x, Is.EqualTo(0f).Within(k_Tolerance));
            Assert.That(position.z, Is.GreaterThan(0f));
        }

        [Test]
        public void ApproachPosition_KeepsEveryEnemyLevel()
        {
            for (int i = 0; i < 5; i++)
            {
                Assert.That(Fan(i, 5).y, Is.EqualTo(0f).Within(k_Tolerance),
                    "Spawn positions must be level; height is applied by the caller.");
            }
        }

        /// <summary>
        /// The scene is generated, not authored, so it has to come out the same every time. A
        /// randomised layout means a bug that reproduces on some runs and not others.
        /// </summary>
        [Test]
        public void ApproachPosition_IsTheSameEveryTimeItIsAsked()
        {
            Assert.That(Fan(2, 5), Is.EqualTo(Fan(2, 5)).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void ApproachPosition_WrapsIndexesBeyondTheCount()
        {
            Assert.That(Fan(7, 4), Is.EqualTo(Fan(3, 4)).Using(Vector3EqualityComparer.Instance));
        }

        [Test]
        public void ApproachPosition_RejectsAFanNothingCouldFitIn()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HordePocLayout.ApproachPosition(0, 0, 10f, 12f, 70f));
            Assert.Throws<ArgumentOutOfRangeException>(() => HordePocLayout.ApproachPosition(0, 3, -1f, 12f, 70f));
            Assert.Throws<ArgumentOutOfRangeException>(() => HordePocLayout.ApproachPosition(0, 3, 12f, 10f, 70f));
        }

        /// <summary>
        /// Enemies have to be standing on something. The arena is sized from the spawn band, so if
        /// the band ever grows past the plate the horde starts the game in mid-air.
        /// </summary>
        [Test]
        public void SpawnBand_FitsInsideTheArena()
        {
            Assert.That(HordePocLayout.k_SpawnFarDistance, Is.LessThan(HordePocLayout.k_ArenaRadius),
                "Enemies would spawn past the edge of the ground and fall.");
        }

        /// <summary>
        /// The invariant behind the whole spawn layout, stated as the thing the player actually
        /// experiences rather than as a distance.
        /// </summary>
        /// <remarks>
        /// Distance and speed are only meaningful against each other: doubling the speed undoes
        /// moving the spawn out, and either change looks harmless on its own. What broke the first
        /// build was that the nearest creature arrived in about a second, before the player had
        /// finished settling into the headset — so the number that has to hold is the time.
        /// </remarks>
        [Test]
        public void SpawnBand_GivesThePlayerSecondsToSeeTheHordeComing()
        {
            // What the player actually gets today. Reading the speed enemies really advance at,
            // rather than the ceiling, means this also catches somebody speeding them up.
            const float minimumWarning = 3f;
            float warning = HordePocLayout.k_SpawnNearDistance / EnemyLocomotionSettings.k_DefaultMoveSpeed;

            Assert.That(warning, Is.GreaterThanOrEqualTo(minimumWarning),
                $"The nearest enemy reaches the player in {warning:F1} s. Under {minimumWarning:F0} s " +
                "the player is grabbed before they have looked around, which reads as starting the " +
                "game already caught.");

            // And the floor that has to hold however the speed is later tuned: the ceiling is the
            // fastest anything is ever allowed to close, and under a couple of seconds there is no
            // reacting to it at all.
            const float minimumWarningAtFullSpeed = 2f;
            float worstCase = HordePocLayout.k_SpawnNearDistance / EnemyLocomotionSettings.k_MaxMoveSpeed;

            Assert.That(worstCase, Is.GreaterThanOrEqualTo(minimumWarningAtFullSpeed),
                $"An enemy tuned all the way up to the {EnemyLocomotionSettings.k_MaxMoveSpeed:F1} m/s " +
                $"ceiling would arrive in {worstCase:F1} s. Either the ceiling is too high for where " +
                "enemies start, or they start too close for the ceiling.");
        }

        static Vector3 Fan(int index, int count) => HordePocLayout.ApproachPosition(
            index, count,
            HordePocLayout.k_SpawnNearDistance,
            HordePocLayout.k_SpawnFarDistance,
            HordePocLayout.k_SpawnArcDegrees);

        /// <summary>
        /// The comfort guard, checked against the anchors the builder actually places rather than
        /// against invented ones. Nothing else in the project can catch a chest anchor being nudged
        /// up until it sits under the player's chin — it looks fine in the scene view and only
        /// becomes obvious with the headset on.
        /// </summary>
        [Test]
        public void BodyAnchors_StayClearOfTheHeadAtEveryOrdinaryEyeHeight()
        {
            foreach (float eyeHeight in new[] { HordePocLayout.k_ReferenceEyeHeight, 1.7f, 1.9f })
            {
                var head = new Vector3(0f, eyeHeight, 0f);

                foreach (var layout in HordePocLayout.k_BodyAnchors)
                {
                    var position = AnchorPosition(layout, eyeHeight);
                    float distance = Vector3.Distance(position, head);

                    Assert.That(
                        LatchAnchorSelector.IsClearOfHead(
                            position, head, LatchAnchorSelector.k_DefaultHeadClearance),
                        Is.True,
                        $"'{layout.name}' sits {distance:F2} m from the eyes of a {eyeHeight:F2} m " +
                        "player, inside the clearance, so it would be refused and that band would " +
                        "quietly stop being reachable.");
                }
            }
        }

        /// <summary>
        /// A creature hanging from an anchor must not end up buried in the floor. The drop is
        /// measured downward from the anchor, so a low anchor with a generous drop puts a 1 m
        /// creature's feet underground, where it reads as sunk rather than as holding on.
        /// </summary>
        [Test]
        public void BodyAnchors_KeepLatchedEnemiesOutOfTheFloor()
        {
            foreach (float eyeHeight in new[] { HordePocLayout.k_ReferenceEyeHeight, 1.7f, 1.9f })
            {
                foreach (var layout in HordePocLayout.k_BodyAnchors)
                {
                    float center = AnchorPosition(layout, eyeHeight).y - layout.hangDrop;
                    float bottom = center - HordePocLayout.k_DummyHeight * 0.5f;

                    Assert.That(bottom, Is.GreaterThanOrEqualTo(0f),
                        $"A creature on '{layout.name}' reaches {bottom:F2} m — below the floor — " +
                        $"on a {eyeHeight:F2} m player.");
                }
            }
        }

        [Test]
        public void BodyAnchors_OfferBothBandsOnBothSides()
        {
            var high = System.Array.FindAll(HordePocLayout.k_BodyAnchors, a => a.height == LatchHeight.High);
            var low = System.Array.FindAll(HordePocLayout.k_BodyAnchors, a => a.height == LatchHeight.Low);

            Assert.That(high.Length, Is.GreaterThanOrEqualTo(2),
                "With fewer than two high anchors the second leaper of a wave has nowhere to land.");
            Assert.That(low.Length, Is.GreaterThanOrEqualTo(2));

            Assert.That(System.Array.Exists(HordePocLayout.k_BodyAnchors, a => a.bodyOffset.x < 0f), Is.True,
                "Nothing can take hold of the player's left side.");
            Assert.That(System.Array.Exists(HordePocLayout.k_BodyAnchors, a => a.bodyOffset.x > 0f), Is.True,
                "Nothing can take hold of the player's right side.");
        }

        [Test]
        public void BodyAnchors_AreAllNamedDifferently()
        {
            var names = new System.Collections.Generic.HashSet<string>();

            foreach (var layout in HordePocLayout.k_BodyAnchors)
            {
                Assert.That(names.Add(layout.name), Is.True,
                    $"Two anchors are both called '{layout.name}', so the scene hierarchy cannot be read.");
            }
        }

        static Vector3 AnchorPosition(HordePocLayout.LatchAnchorLayout layout, float eyeHeight) =>
            new Vector3(layout.bodyOffset.x, layout.heightFraction * eyeHeight, layout.bodyOffset.y);

        /// <summary>Compares vectors with a tolerance, since ring math goes through trig.</summary>
        class Vector3EqualityComparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            public static readonly Vector3EqualityComparer Instance = new Vector3EqualityComparer();

            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < k_Tolerance;
            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }
    }
}
