using NUnit.Framework;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Tests for the guards that stop a locomotion model from being tuned into nonsense.
    /// </summary>
    /// <remarks>
    /// These matter more than they look. Every value here is dragged in an inspector, and the ways
    /// they can be set wrong are all silent: nothing throws, nothing logs, the enemies simply stop
    /// behaving and the cause is a number three panels away from where the symptom shows up.
    /// </remarks>
    public class EnemyLocomotionSettingsTests
    {
        const float k_Tolerance = 1e-4f;

        [Test]
        public void Defaults_AdvanceAtAWalkWellInsideTheCeiling()
        {
            var settings = new EnemyLocomotionSettings();

            Assert.That(settings.moveSpeed,
                Is.EqualTo(EnemyLocomotionSettings.k_DefaultMoveSpeed).Within(k_Tolerance));
            Assert.That(settings.moveSpeed, Is.LessThan(EnemyLocomotionSettings.k_MaxMoveSpeed),
                "The ceiling is meant to be a never-exceed, not the speed everything runs at; with " +
                "them equal there is no headroom left to tune into.");
        }

        /// <summary>
        /// The ceiling has to be enforced, not merely documented. How fast a creature may close on
        /// the player is a design decision — past it the gap between seeing one coming and having it
        /// on you is shorter than a reaction — and a limit that lives only in a tooltip is one
        /// somebody raises while tuning something else.
        /// </summary>
        [Test]
        public void Clamp_RefusesToLetAnEnemyCloseFasterThanThePlayerCanReact()
        {
            var settings = new EnemyLocomotionSettings(moveSpeed: 12f);
            settings.Clamp();

            Assert.That(settings.moveSpeed, Is.EqualTo(EnemyLocomotionSettings.k_MaxMoveSpeed).Within(k_Tolerance),
                $"An enemy tuned to 12 m/s was left at {settings.moveSpeed:F1} m/s.");
        }

        [Test]
        public void Clamp_RefusesNegativeSpeeds()
        {
            var settings = new EnemyLocomotionSettings(moveSpeed: -4f, turnSpeed: -90f);
            settings.Clamp();

            Assert.That(settings.moveSpeed, Is.EqualTo(0f).Within(k_Tolerance),
                "A negative speed would send the enemy away from the player.");
            Assert.That(settings.turnSpeed, Is.EqualTo(0f).Within(k_Tolerance));
        }

        /// <summary>
        /// An inverted band is satisfied by no distance at all, so leapers would silently never jump
        /// and would walk into the player instead — a behaviour change that looks exactly like a bug
        /// in the state machine.
        /// </summary>
        [Test]
        public void Clamp_RepairsALeapBandThatWasDraggedInsideOut()
        {
            var settings = new EnemyLocomotionSettings(minLeapRange: 3f, maxLeapRange: 1f);
            settings.Clamp();

            Assert.That(settings.maxLeapRange, Is.GreaterThan(settings.minLeapRange),
                $"The leap band is still {settings.minLeapRange:F1}-{settings.maxLeapRange:F1} m, " +
                "which no distance can satisfy.");
            Assert.That(HordeSteering.IsInLeapRange(
                    (settings.minLeapRange + settings.maxLeapRange) * 0.5f,
                    settings.minLeapRange, settings.maxLeapRange),
                Is.True);
        }

        /// <summary>
        /// Leaping from closer than an enemy can already grab from would be a hop in place, and it
        /// would take a high anchor the enemy could have reached on foot.
        /// </summary>
        [Test]
        public void Clamp_KeepsTheLeapBandOutsideGrabbingRange()
        {
            var settings = new EnemyLocomotionSettings(latchRange: 2f, minLeapRange: 0.5f);
            settings.Clamp();

            Assert.That(settings.minLeapRange, Is.GreaterThanOrEqualTo(settings.latchRange));
        }

        /// <summary>
        /// A leap of zero seconds finishes inside the frame it started, so the creature teleports
        /// onto the player with no jump ever drawn.
        /// </summary>
        [Test]
        public void Clamp_KeepsALeapLongEnoughToBeSeen()
        {
            var settings = new EnemyLocomotionSettings(leapDuration: 0f);
            settings.Clamp();

            Assert.That(settings.leapDuration,
                Is.GreaterThanOrEqualTo(EnemyLocomotionSettings.k_MinimumLeapDuration));
        }

        [Test]
        public void Clamp_LeavesAReasonableModelAlone()
        {
            var settings = new EnemyLocomotionSettings(
                moveSpeed: 2f, latchRange: 0.75f, minLeapRange: 1f, maxLeapRange: 2.2f, leapDuration: 0.45f);
            settings.Clamp();

            Assert.That(settings.moveSpeed, Is.EqualTo(2f).Within(k_Tolerance));
            Assert.That(settings.latchRange, Is.EqualTo(0.75f).Within(k_Tolerance));
            Assert.That(settings.minLeapRange, Is.EqualTo(1f).Within(k_Tolerance));
            Assert.That(settings.maxLeapRange, Is.EqualTo(2.2f).Within(k_Tolerance));
            Assert.That(settings.leapDuration, Is.EqualTo(0.45f).Within(k_Tolerance));
        }
    }
}
