using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Tests for the maths that decides where an enemy walks and how it jumps.
    /// </summary>
    public class HordeSteeringTests
    {
        const float k_Tolerance = 1e-4f;

        static List<Vector3> Neighbours(params Vector3[] positions) => new List<Vector3>(positions);

        [Test]
        public void Seek_PointsStraightAtTheTarget()
        {
            var heading = HordeSteering.Seek(new Vector3(2f, 0f, 0f), new Vector3(5f, 0f, 0f));

            Assert.That(heading.x, Is.EqualTo(1f).Within(k_Tolerance));
            Assert.That(heading.magnitude, Is.EqualTo(1f).Within(k_Tolerance),
                "The heading has to be a unit vector; its length is not a speed.");
        }

        /// <summary>
        /// A player standing on a platform, or an enemy that has just been punched into the air,
        /// must not tilt the heading. Walking is a horizontal business and height belongs to physics.
        /// </summary>
        [Test]
        public void Seek_IgnoresAnyDifferenceInHeight()
        {
            var heading = HordeSteering.Seek(new Vector3(0f, 3f, 0f), new Vector3(0f, 0f, 4f));

            Assert.That(heading.y, Is.EqualTo(0f).Within(k_Tolerance));
            Assert.That(heading.z, Is.EqualTo(1f).Within(k_Tolerance));
        }

        [Test]
        public void Seek_ReturnsNothingWhenAlreadyOnTheTarget()
        {
            var heading = HordeSteering.Seek(Vector3.one, Vector3.one);

            Assert.That(heading, Is.EqualTo(Vector3.zero),
                "Normalising a zero vector yields NaN, which would poison the Rigidbody.");
        }

        [Test]
        public void Separation_PushesDirectlyAwayFromACloseNeighbour()
        {
            var push = HordeSteering.Separation(Vector3.zero, Neighbours(new Vector3(0.3f, 0f, 0f)), 1f);

            Assert.That(push.x, Is.LessThan(0f), "The push is toward the neighbour, not away from it.");
            Assert.That(push.z, Is.EqualTo(0f).Within(k_Tolerance));
        }

        [Test]
        public void Separation_IgnoresNeighboursOutsideTheRadius()
        {
            var push = HordeSteering.Separation(Vector3.zero, Neighbours(new Vector3(2f, 0f, 0f)), 1f);

            Assert.That(push, Is.EqualTo(Vector3.zero),
                "An enemy two meters away is not crowding anyone.");
        }

        /// <summary>
        /// The whole point of the falloff: whoever is most on top of you shoves hardest. A flat push
        /// would make a distant neighbour as disruptive as one you are standing inside.
        /// </summary>
        [Test]
        public void Separation_PushesHarderTheCloserTheNeighbourIs()
        {
            float near = HordeSteering.Separation(Vector3.zero, Neighbours(new Vector3(0.2f, 0f, 0f)), 1f).magnitude;
            float far = HordeSteering.Separation(Vector3.zero, Neighbours(new Vector3(0.8f, 0f, 0f)), 1f).magnitude;

            Assert.That(near, Is.GreaterThan(far),
                $"A neighbour at 0.2 m pushed with {near:F3} and one at 0.8 m with {far:F3}.");
        }

        [Test]
        public void Separation_FadesToNothingAtTheEdgeOfTheRadius()
        {
            var push = HordeSteering.Separation(Vector3.zero, Neighbours(new Vector3(0.999f, 0f, 0f)), 1f);

            Assert.That(push.magnitude, Is.LessThan(0.01f),
                "The push has to reach zero at the radius, or enemies jolt as neighbours cross it.");
        }

        [Test]
        public void Separation_SkipsANeighbourItCannotPushAwayFrom()
        {
            var push = HordeSteering.Separation(Vector3.zero, Neighbours(Vector3.zero), 1f);

            Assert.That(push, Is.EqualTo(Vector3.zero),
                "A neighbour at zero distance has no direction to be pushed away from, and " +
                "normalising it would produce NaN.");
        }

        [Test]
        public void Separation_StaysHorizontal()
        {
            var push = HordeSteering.Separation(
                Vector3.zero, Neighbours(new Vector3(0.2f, 0.4f, 0.2f)), 1f);

            Assert.That(push.y, Is.EqualTo(0f).Within(k_Tolerance),
                "Separation must never push an enemy into the floor or into the air.");
        }

        [Test]
        public void Separation_AccumulatesAcrossSeveralNeighbours()
        {
            var one = HordeSteering.Separation(Vector3.zero, Neighbours(new Vector3(0.3f, 0f, 0f)), 1f);
            var two = HordeSteering.Separation(
                Vector3.zero, Neighbours(new Vector3(0.3f, 0f, 0f), new Vector3(0.3f, 0f, 0.001f)), 1f);

            Assert.That(two.magnitude, Is.GreaterThan(one.magnitude),
                "Being hemmed in by two enemies has to push harder than being hemmed in by one.");
        }

        [Test]
        public void Steer_HeadsStraightForThePlayerWithNobodyInTheWay()
        {
            var heading = HordeSteering.Steer(
                Vector3.zero, new Vector3(0f, 0f, 5f), Neighbours(), 0.9f, 1.3f);

            Assert.That(heading.z, Is.EqualTo(1f).Within(k_Tolerance));
        }

        /// <summary>
        /// The balance that matters: an enemy with someone right in front of it still has to make
        /// progress. Separation that overpowers the heading leaves the horde milling about out of
        /// reach, which reads as the enemies losing interest in the player.
        /// </summary>
        [Test]
        public void Steer_StillClosesInWhenANeighbourIsInTheWay()
        {
            var settings = new EnemyLocomotionSettings();
            var target = new Vector3(0f, 0f, 5f);
            var blocker = Neighbours(new Vector3(0f, 0f, 0.5f));

            var heading = HordeSteering.Steer(
                Vector3.zero, target, blocker, settings.separationRadius, settings.separationWeight);

            Assert.That(heading.z, Is.GreaterThan(0f),
                $"The enemy walked away from the player (heading {heading}) because the creature in " +
                "front of it pushed harder than the player pulled.");
        }

        [Test]
        public void Steer_SidestepsRatherThanWalkingThroughANeighbour()
        {
            var settings = new EnemyLocomotionSettings();
            var target = new Vector3(0f, 0f, 5f);
            var blocker = Neighbours(new Vector3(0.2f, 0f, 0.4f));

            var heading = HordeSteering.Steer(
                Vector3.zero, target, blocker, settings.separationRadius, settings.separationWeight);

            Assert.That(heading.x, Is.LessThan(0f),
                "A neighbour off to the right should push the enemy to the left, not be walked into.");
        }

        [Test]
        public void Steer_AlwaysReturnsAUnitHeadingOrNothing()
        {
            var heading = HordeSteering.Steer(
                Vector3.zero, new Vector3(0f, 0f, 5f),
                Neighbours(new Vector3(0.1f, 0f, 0.1f)), 0.9f, 1.3f);

            Assert.That(heading.magnitude, Is.EqualTo(1f).Within(k_Tolerance),
                "The caller multiplies this by a speed, so anything but unit length changes how " +
                "fast a crowded enemy walks.");
        }

        [Test]
        public void IsInLeapRange_AcceptsDistancesInsideTheBand()
        {
            Assert.That(HordeSteering.IsInLeapRange(1.5f, 1f, 2.2f), Is.True);
            Assert.That(HordeSteering.IsInLeapRange(1f, 1f, 2.2f), Is.True, "The near edge is inclusive.");
            Assert.That(HordeSteering.IsInLeapRange(2.2f, 1f, 2.2f), Is.True, "The far edge is inclusive.");
        }

        [Test]
        public void IsInLeapRange_RefusesToJumpFromTooFarAway()
        {
            Assert.That(HordeSteering.IsInLeapRange(4f, 1f, 2.2f), Is.False,
                "A jump that cannot reach would drop the enemy in mid-air short of the player.");
        }

        /// <summary>
        /// An enemy already at arm's length has nothing to jump over, and a hop in place reads as a
        /// glitch rather than as an attack.
        /// </summary>
        [Test]
        public void IsInLeapRange_RefusesToJumpFromOnTopOfThePlayer()
        {
            Assert.That(HordeSteering.IsInLeapRange(0.4f, 1f, 2.2f), Is.False);
        }

        [Test]
        public void LeapPoint_StartsWhereTheEnemyLeftTheGround()
        {
            var from = new Vector3(1f, 0.5f, 2f);
            var point = HordeSteering.LeapPoint(from, new Vector3(0f, 1.2f, 0f), 0f, 0.7f);

            Assert.That(Vector3.Distance(point, from), Is.LessThan(k_Tolerance));
        }

        [Test]
        public void LeapPoint_EndsExactlyOnTheAnchor()
        {
            var to = new Vector3(0f, 1.2f, 0f);
            var point = HordeSteering.LeapPoint(new Vector3(1f, 0.5f, 2f), to, 1f, 0.7f);

            Assert.That(Vector3.Distance(point, to), Is.LessThan(k_Tolerance),
                "A leap that does not finish on the anchor leaves the enemy latched in the wrong place.");
        }

        [Test]
        public void LeapPoint_PeaksAtTheRequestedHeightHalfwayThrough()
        {
            const float arc = 0.7f;
            var from = Vector3.zero;
            var to = new Vector3(0f, 0f, 2f);

            var point = HordeSteering.LeapPoint(from, to, 0.5f, arc);
            var flat = Vector3.Lerp(from, to, 0.5f);

            Assert.That(point.y - flat.y, Is.EqualTo(arc).Within(k_Tolerance));
        }

        [Test]
        public void LeapPoint_ArcsAboveTheStraightLineThroughout()
        {
            var from = Vector3.zero;
            var to = new Vector3(0f, 1.2f, 2f);

            // Stepped with an integer count rather than by adding 0.1f nine times, which drifts
            // past 1 and would sample the landing frame where the arc is legitimately flat.
            for (int step = 1; step < 10; step++)
            {
                float t = step / 10f;
                var point = HordeSteering.LeapPoint(from, to, t, 0.7f);
                var flat = Vector3.Lerp(from, to, t);

                Assert.That(point.y, Is.GreaterThan(flat.y),
                    $"At t={t:F1} the leap is at or below the straight line, so it reads as a slide.");
            }
        }

        [Test]
        public void LeapPoint_ClampsProgressPastTheEnd()
        {
            var to = new Vector3(0f, 1.2f, 0f);
            var point = HordeSteering.LeapPoint(Vector3.zero, to, 1.8f, 0.7f);

            Assert.That(Vector3.Distance(point, to), Is.LessThan(k_Tolerance),
                "Overshooting progress must not send the enemy flying past the player.");
        }
    }
}
