using System.Collections.Generic;
using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Works out which way an enemy should walk. Pure maths, no Unity lifecycle.
    /// </summary>
    /// <remarks>
    /// No NavMesh. The POC arena is a flat open square with nothing to path around, so a direct
    /// heading plus a push away from crowding neighbours produces the same movement for a tiny
    /// fraction of the cost — and cost is the whole question at the 20-30 enemies Fase 3 targets.
    /// <para>
    /// Everything here is horizontal. Height is the physics engine's business: enemies fall, get
    /// knocked into the air and land, and none of that should feed back into where they are headed.
    /// </para>
    /// </remarks>
    public static class HordeSteering
    {
        /// <summary>Below this a direction vector is treated as having no direction at all.</summary>
        const float k_DirectionEpsilon = 1e-6f;

        /// <summary>
        /// Unit heading from <paramref name="self"/> toward <paramref name="target"/>, flattened.
        /// </summary>
        /// <remarks>
        /// Returns zero when the two points are on top of each other, which the caller must treat
        /// as "stay put" rather than normalising into a NaN.
        /// </remarks>
        public static Vector3 Seek(Vector3 self, Vector3 target)
        {
            var toTarget = Horizontal(target - self);

            return toTarget.sqrMagnitude > k_DirectionEpsilon ? toTarget.normalized : Vector3.zero;
        }

        /// <summary>
        /// Push away from neighbours that are closer than <paramref name="radius"/>.
        /// </summary>
        /// <remarks>
        /// The push from each neighbour fades linearly to nothing at the edge of the radius, so an
        /// enemy is shoved hardest by whoever is most on top of it. The result is deliberately not
        /// normalised: its length is how crowded this spot is, which is what lets
        /// <see cref="Steer"/> weigh it against the heading toward the player.
        /// <para>
        /// A neighbour at exactly zero distance is skipped. There is no direction to push in, and
        /// two enemies cannot actually reach that state with solid colliders between them.
        /// </para>
        /// </remarks>
        /// <param name="self">Position of the enemy being steered.</param>
        /// <param name="neighbours">Positions of the other enemies. May include <paramref name="self"/>, which is ignored.</param>
        /// <param name="radius">Distance at which a neighbour stops mattering, in meters.</param>
        public static Vector3 Separation(Vector3 self, IReadOnlyList<Vector3> neighbours, float radius)
        {
            if (neighbours == null || radius <= 0f)
                return Vector3.zero;

            var push = Vector3.zero;

            for (int i = 0; i < neighbours.Count; i++)
            {
                var away = Horizontal(self - neighbours[i]);
                float distance = away.magnitude;

                if (distance <= k_DirectionEpsilon || distance >= radius)
                    continue;

                push += away / distance * (1f - distance / radius);
            }

            return push;
        }

        /// <summary>
        /// Combines the heading toward the player with the push away from neighbours into a single
        /// unit heading.
        /// </summary>
        /// <remarks>
        /// Separation is weighted rather than absolute so a crowded enemy still makes progress
        /// toward the player: pure separation would leave the horde milling about just out of
        /// reach, which reads as the enemies losing interest.
        /// </remarks>
        /// <returns>A flattened unit vector, or zero when there is nowhere to go.</returns>
        public static Vector3 Steer(
            Vector3 self,
            Vector3 target,
            IReadOnlyList<Vector3> neighbours,
            float separationRadius,
            float separationWeight)
        {
            var heading = Seek(self, target)
                + Separation(self, neighbours, separationRadius) * Mathf.Max(0f, separationWeight);

            return heading.sqrMagnitude > k_DirectionEpsilon ? heading.normalized : Vector3.zero;
        }

        /// <summary>
        /// Whether an enemy at <paramref name="distance"/> from the player should launch itself.
        /// </summary>
        /// <remarks>
        /// A band, not a threshold. The far edge is the range the jump can actually cover; the near
        /// edge exists because an enemy that is already at arm's length has nothing to jump over,
        /// and a hop in place reads as a glitch. Inside the near edge the enemy takes hold on foot
        /// instead — the anchor chooser falls back to a low anchor on its own.
        /// </remarks>
        /// <param name="distance">Horizontal distance to the player, in meters.</param>
        /// <param name="minRange">Below this the enemy is already too close to bother jumping.</param>
        /// <param name="maxRange">Above this the jump would fall short.</param>
        public static bool IsInLeapRange(float distance, float minRange, float maxRange) =>
            distance >= minRange && distance <= maxRange;

        /// <summary>
        /// Position along a leap from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        /// <remarks>
        /// A scripted arc rather than an impulse and a prayer. A physically launched enemy has to
        /// hit a target that is walking, turning and ducking, so most jumps would miss, and a horde
        /// whose attacks mostly miss is not a horde. The arc is the shape of the jump; whether the
        /// jump was allowed to happen at all is <see cref="IsInLeapRange"/>'s business.
        /// </remarks>
        /// <param name="from">Where the enemy left the ground.</param>
        /// <param name="to">The anchor it is aiming at, sampled as the leap progresses so it tracks a moving player.</param>
        /// <param name="t">Progress through the leap, 0 to 1.</param>
        /// <param name="arcHeight">How high above the straight line the peak of the jump sits, in meters.</param>
        public static Vector3 LeapPoint(Vector3 from, Vector3 to, float t, float arcHeight)
        {
            t = Mathf.Clamp01(t);

            // 4t(1-t) peaks at exactly 1 when t is 0.5 and is zero at both ends, so the arc starts
            // and lands on the straight line no matter how high the peak is set.
            float lift = 4f * t * (1f - t) * arcHeight;

            return Vector3.Lerp(from, to, t) + Vector3.up * lift;
        }

        static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
