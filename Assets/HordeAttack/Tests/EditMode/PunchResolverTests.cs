using System;
using NUnit.Framework;
using UnityEngine;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Tests for the punch model: what a swing at a given speed is worth in damage, knockback and
    /// vibration.
    /// </summary>
    /// <remarks>
    /// Everything here runs against the tuned defaults rather than against values invented for the
    /// test, so a change to the tuning that breaks the feel the design asks for — enemies taking
    /// two to three punches, a slow hand not counting — shows up as a failure here rather than in
    /// the headset.
    /// </remarks>
    public class PunchResolverTests
    {
        const float k_Tolerance = 1e-3f;

        /// <summary>
        /// A solid but ordinary punch. Slow enough to sit in the lower half of the power range, so
        /// it deals the base damage; this is the swing the "three punches to kill" design is
        /// written against.
        /// </summary>
        const float k_StandardPunchSpeed = 3f;

        PunchSettings m_Settings;

        [SetUp]
        public void SetUp() => m_Settings = new PunchSettings();

        static Vector3 Swing(float speed) => Vector3.forward * speed;

        [Test]
        public void Resolve_IgnoresAHandThatIsBarelyMoving()
        {
            var outcome = PunchResolver.Resolve(Swing(m_Settings.minSpeed - 0.1f), 3, m_Settings);

            Assert.That(outcome.landed, Is.False, "A slow hand brushing an enemy is not a punch.");
            Assert.That(outcome.damage, Is.Zero);
            Assert.That(outcome.impulse, Is.EqualTo(Vector3.zero));
            Assert.That(outcome.remainingHealth, Is.EqualTo(3), "A miss must not cost the enemy health.");
            Assert.That(outcome.isLethal, Is.False);
        }

        [Test]
        public void Resolve_CountsAPunchThrownExactlyAtTheThreshold()
        {
            var outcome = PunchResolver.Resolve(Swing(m_Settings.minSpeed), 3, m_Settings);

            Assert.That(outcome.landed, Is.True);
            Assert.That(outcome.damage, Is.EqualTo(1),
                "The weakest punch that counts must still hurt, or landing it feels broken.");
        }

        /// <summary>
        /// The headline behaviour of Fase 1, asserted as a sequence rather than a single call:
        /// health has to step down one punch at a time and the kill has to happen on the third,
        /// not the second or the fourth.
        /// </summary>
        [Test]
        public void Resolve_KillsAnEnemyOnTheThirdStandardPunch()
        {
            int health = 3;
            var swing = Swing(k_StandardPunchSpeed);

            var first = PunchResolver.Resolve(swing, health, m_Settings);
            Assert.That(first.damage, Is.EqualTo(1),
                $"A {k_StandardPunchSpeed} m/s punch is meant to be an ordinary one worth 1 damage.");
            Assert.That(first.remainingHealth, Is.EqualTo(2));
            Assert.That(first.isLethal, Is.False);
            health = first.remainingHealth;

            var second = PunchResolver.Resolve(swing, health, m_Settings);
            Assert.That(second.remainingHealth, Is.EqualTo(1));
            Assert.That(second.isLethal, Is.False, "The enemy died a punch early.");
            health = second.remainingHealth;

            var third = PunchResolver.Resolve(swing, health, m_Settings);
            Assert.That(third.remainingHealth, Is.Zero);
            Assert.That(third.isLethal, Is.True, "The third standard punch has to be the one that kills.");
        }

        [Test]
        public void Resolve_MakesAHardPunchHurtMoreThanAStandardOne()
        {
            var standard = PunchResolver.Resolve(Swing(k_StandardPunchSpeed), 5, m_Settings);
            var hard = PunchResolver.Resolve(Swing(m_Settings.maxSpeed), 5, m_Settings);

            Assert.That(hard.damage, Is.GreaterThan(standard.damage),
                "Swinging twice as fast makes no difference to how much it hurts.");
            Assert.That(hard.damage, Is.EqualTo(m_Settings.maxDamage));
        }

        [Test]
        public void Resolve_NeverExceedsTheMaximumDamageNoMatterHowWildTheSwing()
        {
            var outcome = PunchResolver.Resolve(Swing(500f), 99, m_Settings);

            Assert.That(outcome.damage, Is.EqualTo(m_Settings.maxDamage),
                "A flailing arm one-shots anything.");
            Assert.That(outcome.power, Is.EqualTo(1f).Within(k_Tolerance));
        }

        /// <summary>
        /// Pins the design constraint from <c>PLAN.md</c>: enemies take two to three punches. One
        /// is not a fight, four is a chore.
        /// </summary>
        [Test]
        public void Resolve_LeavesEnemiesTakingBetweenTwoAndThreePunches()
        {
            const int health = 3;

            var hard = PunchResolver.Resolve(Swing(m_Settings.maxSpeed), health, m_Settings);
            Assert.That(hard.isLethal, Is.False, "The hardest punch kills a full-health enemy outright.");
            Assert.That(health / (float)hard.damage, Is.LessThanOrEqualTo(2f),
                "Even at full power an enemy takes more than two punches.");

            var weakest = PunchResolver.Resolve(Swing(m_Settings.minSpeed), health, m_Settings);
            Assert.That(health / (float)weakest.damage, Is.LessThanOrEqualTo(3f),
                "The weakest punch that lands takes more than three swings to kill.");
        }

        [Test]
        public void Resolve_ScalesKnockbackWithHowFastTheHandWasMoving()
        {
            var soft = PunchResolver.Resolve(Swing(2f), 9, m_Settings);
            var hard = PunchResolver.Resolve(Swing(6f), 9, m_Settings);

            Assert.That(hard.impulse.magnitude, Is.GreaterThan(soft.impulse.magnitude),
                "A hard punch throws the enemy no further than a soft one.");
            Assert.That(hard.impulse.magnitude / soft.impulse.magnitude, Is.EqualTo(3f).Within(1e-2f),
                "Knockback is meant to be proportional to hand speed.");
        }

        [Test]
        public void Resolve_CapsKnockbackAtFullPowerSoAFlailCannotLaunchAnyone()
        {
            var atCap = PunchResolver.Resolve(Swing(m_Settings.maxSpeed), 9, m_Settings);
            var wellPast = PunchResolver.Resolve(Swing(m_Settings.maxSpeed * 20f), 9, m_Settings);

            Assert.That(wellPast.impulse.magnitude, Is.EqualTo(atCap.impulse.magnitude).Within(k_Tolerance));
        }

        [Test]
        public void Resolve_ThrowsTheEnemyTheWayTheHandWasTravelling()
        {
            var swing = new Vector3(1f, 0f, 1f).normalized * 5f;
            var outcome = PunchResolver.Resolve(swing, 9, m_Settings);

            var knockbackHeading = new Vector3(outcome.impulse.x, 0f, outcome.impulse.z).normalized;
            var swingHeading = new Vector3(swing.x, 0f, swing.z).normalized;

            Assert.That(Vector3.Angle(knockbackHeading, swingHeading), Is.LessThan(1f),
                "The enemy is not being thrown in the direction of the punch.");
        }

        /// <summary>
        /// Without an upward component the enemy just slides along the floor, which reads as a
        /// shove rather than a hit.
        /// </summary>
        [Test]
        public void Resolve_LiftsTheEnemyOffTheFloor()
        {
            var outcome = PunchResolver.Resolve(Swing(5f), 9, m_Settings);

            Assert.That(outcome.impulse.y, Is.GreaterThan(0f), "The enemy is knocked back but never up.");
            Assert.That(outcome.impulse.y, Is.LessThan(outcome.impulse.z),
                "More of the punch goes upward than backward; the enemy pops up instead of away.");
        }

        /// <summary>
        /// A punch thrown downward should still send the enemy away rather than straight into the
        /// ground, where the floor collider simply eats the impulse.
        /// </summary>
        [Test]
        public void Resolve_DoesNotDriveTheEnemyIntoTheGroundOnADownwardSwing()
        {
            var outcome = PunchResolver.Resolve(new Vector3(0f, -3f, 4f), 9, m_Settings);

            Assert.That(outcome.impulse.y, Is.GreaterThan(0f));
            Assert.That(outcome.impulse.z, Is.GreaterThan(0f));
        }

        [Test]
        public void Resolve_StillPicksADirectionForAPurelyVerticalSwing()
        {
            var outcome = PunchResolver.Resolve(Vector3.up * 5f, 9, m_Settings);

            Assert.That(outcome.landed, Is.True, "An uppercut is a punch.");
            Assert.That(outcome.impulse.magnitude, Is.GreaterThan(0f),
                "An uppercut resolved to no knockback at all.");
            Assert.That(outcome.impulse.y, Is.GreaterThan(0f));
        }

        [Test]
        public void Resolve_VibratesHarderForAHarderPunch()
        {
            var soft = PunchResolver.Resolve(Swing(m_Settings.minSpeed), 9, m_Settings);
            var hard = PunchResolver.Resolve(Swing(m_Settings.maxSpeed), 9, m_Settings);

            Assert.That(soft.hapticAmplitude, Is.EqualTo(m_Settings.minHapticAmplitude).Within(k_Tolerance));
            Assert.That(hard.hapticAmplitude, Is.EqualTo(1f).Within(k_Tolerance));
            Assert.That(soft.hapticAmplitude, Is.GreaterThan(0f),
                "The weakest punch that lands does not vibrate, so the player cannot feel it connect.");
        }

        [Test]
        public void Resolve_GivesEveryLandedPunchAVibrationLongEnoughToFeel()
        {
            var outcome = PunchResolver.Resolve(Swing(4f), 9, m_Settings);

            Assert.That(outcome.hapticDuration, Is.EqualTo(m_Settings.hapticDuration).Within(k_Tolerance));
            Assert.That(outcome.hapticDuration, Is.GreaterThan(0f));
        }

        /// <summary>
        /// Without this an enemy lying dead on the floor keeps absorbing punches and reporting each
        /// one as a kill, and by Fase 3 the wave counter runs away.
        /// </summary>
        [Test]
        public void Resolve_CannotPunchSomethingThatIsAlreadyDead()
        {
            var outcome = PunchResolver.Resolve(Swing(m_Settings.maxSpeed), 0, m_Settings);

            Assert.That(outcome.landed, Is.False);
            Assert.That(outcome.isLethal, Is.False, "A corpse was killed a second time.");
            Assert.That(outcome.damage, Is.Zero);
        }

        [Test]
        public void Resolve_ClampsOverkillAtZeroRatherThanGoingNegative()
        {
            var outcome = PunchResolver.Resolve(Swing(m_Settings.maxSpeed), 1, m_Settings);

            Assert.That(outcome.damage, Is.EqualTo(2));
            Assert.That(outcome.remainingHealth, Is.Zero, "Health went negative.");
            Assert.That(outcome.isLethal, Is.True);
        }

        [Test]
        public void Resolve_RefusesToWorkWithoutATuning()
        {
            Assert.Throws<ArgumentNullException>(() => PunchResolver.Resolve(Swing(5f), 3, null));
        }

        [Test]
        public void NormalizePower_RunsFromZeroAtTheThresholdToOneAtFullPower()
        {
            Assert.That(PunchResolver.NormalizePower(m_Settings.minSpeed, m_Settings),
                Is.EqualTo(0f).Within(k_Tolerance));
            Assert.That(PunchResolver.NormalizePower(m_Settings.maxSpeed, m_Settings),
                Is.EqualTo(1f).Within(k_Tolerance));
            Assert.That(PunchResolver.NormalizePower((m_Settings.minSpeed + m_Settings.maxSpeed) * 0.5f, m_Settings),
                Is.EqualTo(0.5f).Within(k_Tolerance));
        }

        [Test]
        public void NormalizePower_StaysInsideZeroToOne()
        {
            Assert.That(PunchResolver.NormalizePower(0f, m_Settings), Is.EqualTo(0f).Within(k_Tolerance));
            Assert.That(PunchResolver.NormalizePower(1000f, m_Settings), Is.EqualTo(1f).Within(k_Tolerance));
        }

        [Test]
        public void KnockbackDirection_IsAlwaysAUnitVector()
        {
            var directions = new[]
            {
                new Vector3(3f, 0f, 0f),
                new Vector3(0f, 0f, -7f),
                new Vector3(1f, -2f, 3f),
                Vector3.up * 4f,
            };

            foreach (var swing in directions)
            {
                var direction = PunchResolver.KnockbackDirection(swing, m_Settings.upwardBias);

                Assert.That(direction.magnitude, Is.EqualTo(1f).Within(k_Tolerance),
                    $"Direction for swing {swing} is not normalized, so it scales the impulse twice.");
            }
        }

        [Test]
        public void KnockbackDirection_FallsBackToUpForAHandThatIsNotMovingAtAll()
        {
            var direction = PunchResolver.KnockbackDirection(Vector3.zero, m_Settings.upwardBias);

            Assert.That(direction, Is.EqualTo(Vector3.up));
        }

        /// <summary>
        /// A designer dragging the upper bound below the lower one leaves a range in which no punch
        /// can ever reach full power. The clamp is what stops that from silently flattening combat.
        /// </summary>
        [Test]
        public void Clamp_RepairsAnInvertedSpeedRange()
        {
            var settings = new PunchSettings(minSpeed: 5f, maxSpeed: 1f);

            settings.Clamp();

            Assert.That(settings.maxSpeed, Is.GreaterThan(settings.minSpeed));
            Assert.That(PunchResolver.NormalizePower(settings.maxSpeed, settings),
                Is.EqualTo(1f).Within(k_Tolerance));
        }

        [Test]
        public void Clamp_KeepsEveryPunchWorthAtLeastOneDamage()
        {
            var settings = new PunchSettings(maxDamage: 0);

            settings.Clamp();

            Assert.That(settings.maxDamage, Is.GreaterThanOrEqualTo(1));
            Assert.That(PunchResolver.Resolve(Swing(4f), 3, settings).damage, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Clamp_LeavesTheTunedDefaultsAlone()
        {
            var settings = new PunchSettings();
            settings.Clamp();

            var untouched = new PunchSettings();

            Assert.That(settings.minSpeed, Is.EqualTo(untouched.minSpeed));
            Assert.That(settings.maxSpeed, Is.EqualTo(untouched.maxSpeed));
            Assert.That(settings.maxDamage, Is.EqualTo(untouched.maxDamage));
            Assert.That(settings.impulsePerSpeed, Is.EqualTo(untouched.impulsePerSpeed));
        }
    }
}
