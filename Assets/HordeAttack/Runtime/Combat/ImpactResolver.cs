using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// What a collision between a thrown enemy and something else was worth.
    /// </summary>
    public readonly struct ImpactOutcome
    {
        /// <summary>Whether the collision was fast enough to count as an impact.</summary>
        public readonly bool landed;

        /// <summary>How hard the impact was, from 0 at the detection threshold to 1 at full power.</summary>
        public readonly float power;

        /// <summary>
        /// Health removed. Zero when the impact did not land.
        /// </summary>
        /// <remarks>
        /// One number, not two. A collision between two creatures costs both of them the same, which
        /// is what makes throwing one enemy into another a trade the player chooses to make rather
        /// than a free kill — and it is the plainest reading of "ambos reciben daño" in
        /// <c>PLAN.md</c>. Against the ground there is only one creature to charge it to.
        /// </remarks>
        public readonly int damage;

        public ImpactOutcome(bool landed, float power, int damage)
        {
            this.landed = landed;
            this.power = power;
            this.damage = damage;
        }

        /// <summary>A collision too gentle to be worth anything.</summary>
        public static ImpactOutcome Miss => new ImpactOutcome(false, 0f, 0);
    }

    /// <summary>
    /// Turns the speed of a collision into damage. Pure maths, no Unity lifecycle.
    /// </summary>
    /// <remarks>
    /// The companion to <see cref="PunchResolver"/> and deliberately built the same way: the part of
    /// combat worth testing is isolated from <see cref="MonoBehaviour"/>, so it can be tested without
    /// a running scene, and Fase 4 can put the mutation it feeds behind an owner RPC without moving
    /// any of the arithmetic.
    /// </remarks>
    public static class ImpactResolver
    {
        /// <summary>Below this, a vector is treated as having no direction at all.</summary>
        const float k_DirectionEpsilon = 1e-6f;

        /// <summary>
        /// Resolves a collision that closed at <paramref name="approachSpeed"/> against
        /// <paramref name="kind"/>.
        /// </summary>
        /// <param name="approachSpeed">
        /// How fast the two bodies were closing along the contact normal, in m/s. See
        /// <see cref="ApproachSpeed"/> — the raw relative speed is the wrong number.
        /// </param>
        /// <param name="kind">What was hit, which picks the speed band.</param>
        /// <param name="settings">Tuning for the speed bands and the damage ceiling.</param>
        public static ImpactOutcome Resolve(float approachSpeed, ImpactKind kind, ImpactSettings settings)
        {
            if (settings == null)
                throw new System.ArgumentNullException(nameof(settings));

            if (approachSpeed < settings.MinSpeedFor(kind))
                return ImpactOutcome.Miss;

            float power = NormalizePower(approachSpeed, kind, settings);

            // Ceil and a floor of 1, the same shape the punch curve uses: an impact hard enough to
            // register has to cost the creature something, or a hit that clearly connected reads as
            // having been ignored.
            int damage = Mathf.Clamp(
                Mathf.CeilToInt(power * settings.maxDamage), 1, settings.maxDamage);

            return new ImpactOutcome(true, power, damage);
        }

        /// <summary>
        /// Maps an approach speed onto 0..1 within the band <paramref name="kind"/> is scored on.
        /// </summary>
        public static float NormalizePower(float approachSpeed, ImpactKind kind, ImpactSettings settings)
        {
            if (settings == null)
                throw new System.ArgumentNullException(nameof(settings));

            float min = settings.MinSpeedFor(kind);
            float span = settings.MaxSpeedFor(kind) - min;

            // A collapsed or inverted band would silently make every impact a full-power one, which
            // is the sort of thing nobody notices until enemies start dying to nudges.
            if (span <= 0f)
                return 1f;

            return Mathf.Clamp01((approachSpeed - min) / span);
        }

        /// <summary>
        /// How fast two colliding bodies were actually closing on each other, in m/s.
        /// </summary>
        /// <remarks>
        /// The component of the relative velocity along the contact normal, not its magnitude, and
        /// the difference is not academic. An enemy that has been punched skids and rolls across the
        /// floor for a second or two, touching the ground at high speed the whole way; scored on raw
        /// relative speed it would take impact damage every physics step and die of sliding. Along
        /// the normal, that same skid is nearly zero — only the part of the motion that drives the
        /// body <em>into</em> the surface counts, which is what "impact" means.
        /// </remarks>
        /// <param name="relativeVelocity">
        /// Relative linear velocity of the two bodies, as reported by <c>Collision.relativeVelocity</c>.
        /// </param>
        /// <param name="contactNormal">Normal at the contact point.</param>
        public static float ApproachSpeed(Vector3 relativeVelocity, Vector3 contactNormal)
        {
            // A degenerate normal means there is nothing to project onto, so fall back to the full
            // closing speed rather than silently reporting a harmless collision.
            if (contactNormal.sqrMagnitude <= k_DirectionEpsilon)
                return relativeVelocity.magnitude;

            return Mathf.Abs(Vector3.Dot(relativeVelocity, contactNormal.normalized));
        }
    }
}
