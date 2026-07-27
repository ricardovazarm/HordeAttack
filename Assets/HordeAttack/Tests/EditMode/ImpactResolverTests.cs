using System;
using NUnit.Framework;
using UnityEngine;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Tests for the impact model: what running into something at speed is worth.
    /// </summary>
    /// <remarks>
    /// Everything here runs against the tuned defaults rather than against numbers invented for the
    /// test, and several of the cases are deliberately written against the <em>punch</em> model as
    /// well. That coupling is the point: the projectile in this game is almost always an enemy that
    /// was just punched, so the two models cannot be tuned independently without one of them quietly
    /// breaking the other.
    /// </remarks>
    public class ImpactResolverTests
    {
        const float k_Tolerance = 1e-3f;

        /// <summary>Health a fresh enemy has, and therefore what "kills outright" has to beat.</summary>
        /// <remarks>
        /// Written as a literal rather than read from <see cref="HordeEnemy.maxHealth"/>. An
        /// assertion that reads the constant it is validating cannot fail — dropping the enemy to one
        /// hit point would drop the expectation with it, and the test would stay green while the
        /// design promise it exists to protect quietly went away.
        /// </remarks>
        const int k_HealthyEnemy = 3;

        /// <summary>
        /// Speed a committed downward slam puts on a held creature, in m/s.
        /// </summary>
        /// <remarks>
        /// A hand driven down at about 6 m/s, times the toolkit's 1.5 throw scale. This is the swing
        /// behind "aventarlo fuerte sí lo mata": hard, but nothing a player has to be told about.
        /// </remarks>
        const float k_HardSlamSpeed = 9f;

        ImpactSettings m_Settings;
        PunchSettings m_Punch;

        [SetUp]
        public void SetUp()
        {
            m_Settings = new ImpactSettings();
            m_Punch = new PunchSettings();
        }

        /// <summary>Launch angle the punch model throws enemies at, in radians.</summary>
        float PunchElevation => Mathf.Atan(m_Punch.upwardBias);

        /// <summary>
        /// How fast an enemy punched <paramref name="range"/> meters is travelling as it leaves the
        /// fist, in m/s.
        /// </summary>
        float LaunchSpeed(float range) => PunchResolver.LaunchSpeedForRange(range, m_Punch.upwardBias);

        [Test]
        public void Resolve_IgnoresAGentleBump()
        {
            var outcome = ImpactResolver.Resolve(
                m_Settings.creatureMinSpeed - 0.1f, ImpactKind.Creature, m_Settings);

            Assert.That(outcome.landed, Is.False, "Two enemies jostling each other is not an impact.");
            Assert.That(outcome.damage, Is.Zero);
            Assert.That(outcome.power, Is.EqualTo(0f).Within(k_Tolerance));
        }

        [Test]
        public void Resolve_CountsAnImpactExactlyAtTheThreshold()
        {
            var outcome = ImpactResolver.Resolve(
                m_Settings.creatureMinSpeed, ImpactKind.Creature, m_Settings);

            Assert.That(outcome.landed, Is.True);
            Assert.That(outcome.damage, Is.EqualTo(1),
                "An impact hard enough to register has to cost something, or it reads as ignored.");
        }

        [Test]
        public void Resolve_ScalesDamageWithHowHardTheImpactWas()
        {
            var soft = ImpactResolver.Resolve(
                m_Settings.creatureMinSpeed + 0.5f, ImpactKind.Creature, m_Settings);
            var hard = ImpactResolver.Resolve(
                m_Settings.creatureMaxSpeed, ImpactKind.Creature, m_Settings);

            Assert.That(hard.damage, Is.GreaterThan(soft.damage),
                "A gnome thrown at full speed has to hurt more than one that was nudged.");
            Assert.That(hard.power, Is.GreaterThan(soft.power));
        }

        [Test]
        public void Resolve_CapsDamageSoAnAbsurdSpeedIsNoBetterThanFullPower()
        {
            var full = ImpactResolver.Resolve(m_Settings.creatureMaxSpeed, ImpactKind.Creature, m_Settings);
            var absurd = ImpactResolver.Resolve(500f, ImpactKind.Creature, m_Settings);

            Assert.That(absurd.power, Is.EqualTo(1f).Within(k_Tolerance));
            Assert.That(absurd.damage, Is.EqualTo(full.damage));
            Assert.That(absurd.damage, Is.EqualTo(m_Settings.maxDamage));
        }

        /// <summary>
        /// The headline promise of the phase: a creature thrown hard enough into another one is a
        /// kill, which is what turns the thing hanging off you into ammunition.
        /// </summary>
        [Test]
        public void Defaults_MakeAFullPowerImpactKillAHealthyEnemyOutright()
        {
            var outcome = ImpactResolver.Resolve(
                m_Settings.creatureMaxSpeed, ImpactKind.Creature, m_Settings);

            Assert.That(outcome.damage, Is.GreaterThanOrEqualTo(k_HealthyEnemy),
                $"A full power impact deals {outcome.damage}, so a healthy enemy with " +
                $"{k_HealthyEnemy} health walks away from being thrown at top speed.");
        }

        /// <summary>
        /// The floor must not finish off a creature the player merely punched.
        /// </summary>
        /// <remarks>
        /// This is the case that decides whether Fase 1 survives Fase 2b. A punched enemy is launched
        /// on a ballistic arc and comes back down onto the ground a second later, every single time;
        /// if that landing counted, every punch would be lethal and the "two to three punches" the
        /// whole fight is built on would be gone without anyone touching the punch model.
        /// <para>
        /// Deliberately written against the hardest punch in the game, and against the punch model's
        /// own numbers rather than a copied constant, so raising the knockback distance or lowering
        /// the ground threshold both show up here.
        /// </para>
        /// </remarks>
        [Test]
        public void Ground_ShrugsOffTheLandingOfEvenTheHardestPunch()
        {
            // A projectile launched and landing at the same height comes down with the vertical
            // speed it left with.
            float landingSpeed = LaunchSpeed(m_Punch.maxKnockbackDistance) * Mathf.Sin(PunchElevation);

            var outcome = ImpactResolver.Resolve(landingSpeed, ImpactKind.Ground, m_Settings);

            Assert.That(outcome.landed, Is.False,
                $"An enemy punched the full {m_Punch.maxKnockbackDistance} m lands at " +
                $"{landingSpeed:0.00} m/s, which the ground band counts as an impact worth " +
                $"{outcome.damage} damage. Every punch would kill.");
        }

        [Test]
        public void Ground_ShrugsOffBeingDroppedFromHandHeight()
        {
            const float handHeight = 1.2f;
            float dropSpeed = Mathf.Sqrt(2f * Physics.gravity.magnitude * handHeight);

            var outcome = ImpactResolver.Resolve(dropSpeed, ImpactKind.Ground, m_Settings);

            Assert.That(outcome.landed, Is.False,
                $"Letting a creature go at head height drops it at {dropSpeed:0.00} m/s and killed " +
                "it. Dropping something is not throwing it.");
        }

        [Test]
        public void Defaults_LetAHardSlamAtTheFloorKillAHealthyEnemy()
        {
            var outcome = ImpactResolver.Resolve(k_HardSlamSpeed, ImpactKind.Ground, m_Settings);

            Assert.That(outcome.landed, Is.True);
            Assert.That(outcome.damage, Is.GreaterThanOrEqualTo(k_HealthyEnemy),
                $"Spiking a gnome into the floor at {k_HardSlamSpeed} m/s only did {outcome.damage} " +
                $"damage, so it survives with {k_HealthyEnemy} health and the throw reads as useless.");
        }

        /// <summary>
        /// A knocked-back enemy is a projectile whether the player meant it or not, and it has to
        /// hurt what it lands on — that is most of what kills the horde once there are thirty of them.
        /// </summary>
        [Test]
        public void Creature_TakesDamageFromAnEnemyThatWasMerelyPunchedIntoIt()
        {
            float horizontalSpeed = LaunchSpeed(m_Punch.minKnockbackDistance) * Mathf.Cos(PunchElevation);

            var outcome = ImpactResolver.Resolve(horizontalSpeed, ImpactKind.Creature, m_Settings);

            Assert.That(outcome.landed, Is.True,
                $"The weakest punch that counts sends a gnome off at {horizontalSpeed:0.00} m/s and " +
                "flying into a neighbour did nothing at all.");
        }

        /// <summary>
        /// The other side of the same coin: the weakest punch in the game must not clear the board.
        /// </summary>
        /// <remarks>
        /// Without a ceiling well above knockback speed, every punch thrown into a crowd would kill
        /// the bystander outright, and the grip — the entire point of this phase — would be a slower
        /// way of doing what a jab already did.
        /// </remarks>
        [Test]
        public void Creature_IsNotKilledOutrightByTheWeakestPunchInTheGame()
        {
            float horizontalSpeed = LaunchSpeed(m_Punch.minKnockbackDistance) * Mathf.Cos(PunchElevation);

            var outcome = ImpactResolver.Resolve(horizontalSpeed, ImpactKind.Creature, m_Settings);

            Assert.That(outcome.damage, Is.LessThan(k_HealthyEnemy),
                $"A {m_Punch.minSpeed} m/s jab sends a gnome into its neighbour at " +
                $"{horizontalSpeed:0.00} m/s and kills it outright.");
        }

        [Test]
        public void ApproachSpeed_MeasuresOnlyTheMotionIntoTheSurface()
        {
            float speed = ImpactResolver.ApproachSpeed(new Vector3(0f, -8f, 0f), Vector3.up);

            Assert.That(speed, Is.EqualTo(8f).Within(k_Tolerance));
        }

        /// <summary>
        /// A punched enemy skids across the floor for a second or two, in contact the whole way.
        /// Scored on raw closing speed that is a full-power impact on every physics step, and the
        /// creature would die of sliding rather than of anything the player did.
        /// </summary>
        [Test]
        public void ApproachSpeed_IgnoresSlidingAlongASurface()
        {
            float speed = ImpactResolver.ApproachSpeed(new Vector3(12f, 0f, 0f), Vector3.up);

            Assert.That(speed, Is.EqualTo(0f).Within(k_Tolerance),
                "Skidding along the ground was counted as driving into it.");

            var outcome = ImpactResolver.Resolve(speed, ImpactKind.Ground, m_Settings);
            Assert.That(outcome.landed, Is.False);
        }

        [Test]
        public void ApproachSpeed_TakesTheSameNumberWhicheverWayTheNormalPoints()
        {
            float up = ImpactResolver.ApproachSpeed(new Vector3(0f, -8f, 0f), Vector3.up);
            float down = ImpactResolver.ApproachSpeed(new Vector3(0f, -8f, 0f), Vector3.down);

            Assert.That(up, Is.EqualTo(down).Within(k_Tolerance),
                "Which of the two bodies Unity reports the normal from is not the game's business.");
        }

        [Test]
        public void ApproachSpeed_FallsBackToTheFullClosingSpeedWithoutAContactNormal()
        {
            var velocity = new Vector3(0f, -8f, 0f);

            float speed = ImpactResolver.ApproachSpeed(velocity, Vector3.zero);

            Assert.That(speed, Is.EqualTo(velocity.magnitude).Within(k_Tolerance),
                "A collision with no contact points must err toward counting, not toward silence.");
        }

        [Test]
        public void ApproachSpeed_DoesNotDependOnHowLongTheNormalIs()
        {
            float unit = ImpactResolver.ApproachSpeed(new Vector3(0f, -8f, 0f), Vector3.up);
            float stretched = ImpactResolver.ApproachSpeed(new Vector3(0f, -8f, 0f), Vector3.up * 7f);

            Assert.That(stretched, Is.EqualTo(unit).Within(k_Tolerance));
        }

        [Test]
        public void Resolve_RefusesToGuessWithoutSettings()
        {
            Assert.Throws<ArgumentNullException>(
                () => ImpactResolver.Resolve(10f, ImpactKind.Creature, null));
            Assert.Throws<ArgumentNullException>(
                () => ImpactResolver.NormalizePower(10f, ImpactKind.Creature, null));
        }

        [Test]
        public void Clamp_OpensUpABandThatWasSetInverted()
        {
            var settings = new ImpactSettings(
                creatureMinSpeed: 9f, creatureMaxSpeed: 2f, groundMinSpeed: 9f, groundMaxSpeed: 1f);

            settings.Clamp();

            Assert.That(settings.creatureMaxSpeed, Is.GreaterThan(settings.creatureMinSpeed),
                "An inverted band makes every impact a full power one, silently.");
            Assert.That(settings.groundMaxSpeed, Is.GreaterThan(settings.groundMinSpeed));
        }

        [Test]
        public void Clamp_RefusesAnImpactThatCouldNotHurtAnybody()
        {
            var settings = new ImpactSettings(maxDamage: 0);

            settings.Clamp();

            Assert.That(settings.maxDamage, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Clamp_RefusesNegativeSpeeds()
        {
            var settings = new ImpactSettings(creatureMinSpeed: -4f, groundMinSpeed: -4f);

            settings.Clamp();

            Assert.That(settings.creatureMinSpeed, Is.GreaterThanOrEqualTo(0f));
            Assert.That(settings.groundMinSpeed, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void SpeedBands_AreReadPerKind()
        {
            var settings = new ImpactSettings(
                creatureMinSpeed: 3f, creatureMaxSpeed: 11f, groundMinSpeed: 5f, groundMaxSpeed: 7f);

            Assert.That(settings.MinSpeedFor(ImpactKind.Creature), Is.EqualTo(3f).Within(k_Tolerance));
            Assert.That(settings.MaxSpeedFor(ImpactKind.Creature), Is.EqualTo(11f).Within(k_Tolerance));
            Assert.That(settings.MinSpeedFor(ImpactKind.Ground), Is.EqualTo(5f).Within(k_Tolerance));
            Assert.That(settings.MaxSpeedFor(ImpactKind.Ground), Is.EqualTo(7f).Within(k_Tolerance));
        }

        /// <summary>
        /// The same speed must be able to mean different things against the floor and against a
        /// creature, or the two could never have been tuned against the constraints they each face.
        /// </summary>
        [Test]
        public void Defaults_ScoreTheGroundAndACreatureOnDifferentCurves()
        {
            const float speed = 9f;

            var onGround = ImpactResolver.Resolve(speed, ImpactKind.Ground, m_Settings);
            var onCreature = ImpactResolver.Resolve(speed, ImpactKind.Creature, m_Settings);

            Assert.That(onGround.power, Is.Not.EqualTo(onCreature.power).Within(k_Tolerance),
                "Both kinds of impact are being scored on the same ramp, so the ground threshold " +
                "cannot be kept clear of punch landings without dulling enemy-on-enemy throws.");
        }
    }
}
