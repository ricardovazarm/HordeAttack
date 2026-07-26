using UnityEngine;

namespace HordeAttack
{
    /// <summary>
    /// Everything a single punch produces: whether it counted, what it did to the target, and the
    /// feedback the puncher should get.
    /// </summary>
    public readonly struct PunchOutcome
    {
        /// <summary>Whether the contact was fast enough to count as a punch.</summary>
        public readonly bool landed;

        /// <summary>How hard the punch was, from 0 at the detection threshold to 1 at full power.</summary>
        public readonly float power;

        /// <summary>Health removed from the target. Zero when the punch did not land.</summary>
        public readonly int damage;

        /// <summary>World-space knockback impulse, in N·s, to feed to <c>ForceMode.Impulse</c>.</summary>
        public readonly Vector3 impulse;

        /// <summary>Controller vibration amplitude, in 0..1.</summary>
        public readonly float hapticAmplitude;

        /// <summary>Controller vibration length, in seconds.</summary>
        public readonly float hapticDuration;

        /// <summary>Target health after the punch, never below zero.</summary>
        public readonly int remainingHealth;

        /// <summary>Whether this punch is the one that killed the target.</summary>
        public bool isLethal => landed && remainingHealth <= 0;

        public PunchOutcome(
            bool landed, float power, int damage, Vector3 impulse,
            float hapticAmplitude, float hapticDuration, int remainingHealth)
        {
            this.landed = landed;
            this.power = power;
            this.damage = damage;
            this.impulse = impulse;
            this.hapticAmplitude = hapticAmplitude;
            this.hapticDuration = hapticDuration;
            this.remainingHealth = remainingHealth;
        }

        /// <summary>A punch that did not happen, leaving <paramref name="health"/> untouched.</summary>
        public static PunchOutcome Miss(int health) =>
            new PunchOutcome(false, 0f, 0, Vector3.zero, 0f, 0f, Mathf.Max(0, health));
    }

    /// <summary>
    /// Turns a hand's velocity into the effect of a punch. Pure maths, no Unity lifecycle.
    /// </summary>
    /// <remarks>
    /// Detection is driven by hand speed rather than by physical impulse. In VR the hands meet no
    /// resistance and pass straight through anything they touch, so a real collision response
    /// reads as mushy and unpredictable; a speed threshold is what makes a swing feel like it
    /// connected. See <c>PLAN.md</c>, Fase 1.
    /// <para>
    /// Isolating this from <see cref="MonoBehaviour"/> is deliberate: it is the part of combat
    /// worth testing, and it can only be tested properly if it does not need a running scene.
    /// </para>
    /// </remarks>
    public static class PunchResolver
    {
        /// <summary>Below this, a direction vector is treated as having no direction at all.</summary>
        const float k_DirectionEpsilon = 1e-6f;

        /// <summary>
        /// Resolves a punch thrown at <paramref name="handVelocity"/> against a target that
        /// currently has <paramref name="currentHealth"/> health.
        /// </summary>
        /// <param name="handVelocity">
        /// Smoothed world-space velocity of the hand, in m/s. See <see cref="VelocityWindow"/> —
        /// a raw per-frame delta is too noisy to threshold on.
        /// </param>
        /// <param name="currentHealth">Target health before the punch.</param>
        /// <param name="settings">Tuning for threshold, damage curve, knockback and haptics.</param>
        public static PunchOutcome Resolve(Vector3 handVelocity, int currentHealth, PunchSettings settings)
        {
            if (settings == null)
                throw new System.ArgumentNullException(nameof(settings));

            // Nothing to hit. Without this an enemy already on the floor would keep absorbing
            // punches and reporting lethal hits, and the kill counter would run away.
            if (currentHealth <= 0)
                return PunchOutcome.Miss(currentHealth);

            float speed = handVelocity.magnitude;
            if (speed < settings.minSpeed)
                return PunchOutcome.Miss(currentHealth);

            float power = NormalizePower(speed, settings);

            // Ceil, so anything above half power already hurts more, and clamp to at least 1: a
            // punch that landed but did nothing feels broken even when the swing was slow.
            int damage = Mathf.Clamp(Mathf.CeilToInt(power * settings.maxDamage), 1, settings.maxDamage);

            // Clamp the speed the impulse is built from, not the impulse itself, so the same
            // ceiling governs damage and knockback: a wild flail should not launch anyone.
            float impulseMagnitude = Mathf.Min(speed, settings.maxSpeed) * settings.impulsePerSpeed;
            Vector3 impulse = KnockbackDirection(handVelocity, settings.upwardBias) * impulseMagnitude;

            float amplitude = Mathf.Clamp01(Mathf.Lerp(settings.minHapticAmplitude, 1f, power));

            return new PunchOutcome(
                true, power, damage, impulse,
                amplitude, settings.hapticDuration,
                Mathf.Max(0, currentHealth - damage));
        }

        /// <summary>
        /// Maps hand speed onto 0..1, with 0 at the detection threshold and 1 at full power.
        /// </summary>
        public static float NormalizePower(float speed, PunchSettings settings)
        {
            if (settings == null)
                throw new System.ArgumentNullException(nameof(settings));

            float span = settings.maxSpeed - settings.minSpeed;

            // A collapsed or inverted range would make every landed punch full power silently.
            // Saying so is better than guessing, because it is invisible in the headset.
            if (span <= 0f)
                return 1f;

            return Mathf.Clamp01((speed - settings.minSpeed) / span);
        }

        /// <summary>
        /// Direction the target is thrown in: where the hand was travelling, tilted upward.
        /// </summary>
        /// <remarks>
        /// The horizontal component drives the direction rather than the raw velocity, because a
        /// punch thrown slightly downward should still send the enemy away and up, not into the
        /// floor where the ground collider just absorbs it. The upward tilt is what turns a hit
        /// into something the player can watch fly.
        /// </remarks>
        public static Vector3 KnockbackDirection(Vector3 handVelocity, float upwardBias)
        {
            var horizontal = new Vector3(handVelocity.x, 0f, handVelocity.z);

            // A purely vertical swing — an uppercut or a hammer blow — has no horizontal component
            // to aim with, so it falls back to the raw direction of the swing.
            Vector3 direction = horizontal.sqrMagnitude > k_DirectionEpsilon
                ? horizontal.normalized
                : handVelocity.normalized;

            if (direction.sqrMagnitude <= k_DirectionEpsilon)
                return Vector3.up;

            return (direction + Vector3.up * Mathf.Max(0f, upwardBias)).normalized;
        }
    }
}
