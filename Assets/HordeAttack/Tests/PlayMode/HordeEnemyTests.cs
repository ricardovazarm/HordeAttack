using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Exercises <see cref="HordeEnemy"/> as a live component, against the real physics engine.
    /// </summary>
    /// <remarks>
    /// These have to be PlayMode tests. <c>Awake</c> never runs on a component added in edit mode,
    /// so an EditMode fixture would be testing an enemy that never initialised its health, and
    /// <c>Rigidbody.AddForce</c> only shows up in the velocity after a physics step, which edit
    /// mode does not take.
    /// </remarks>
    public class HordeEnemyTests
    {
        /// <summary>
        /// An ordinary swing: fast enough to count, slow enough to be worth the base damage. Matches
        /// the speed the EditMode punch model tests call standard.
        /// </summary>
        const float k_StandardPunchSpeed = 3f;

        readonly List<GameObject> m_Created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in m_Created)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            m_Created.Clear();
        }

        /// <summary>
        /// Builds a punchable enemy at <paramref name="position"/>.
        /// </summary>
        /// <remarks>
        /// Gravity is switched off so that knockback assertions measure the punch and nothing else;
        /// an enemy in free fall picks up velocity of its own between the punch and the assertion.
        /// </remarks>
        HordeEnemy CreateEnemy(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Test Enemy";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.5f;
            m_Created.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = HordePocLayout.k_DummyMass;
            body.useGravity = false;

            // Added last: HordeEnemy captures its spawn pose and caches the body in Awake, which
            // runs the moment the component is attached in play mode.
            return go.AddComponent<HordeEnemy>();
        }

        /// <summary>
        /// Builds an enemy that falls, at the size and mass the real ones have.
        /// </summary>
        /// <remarks>
        /// The rest of this fixture switches gravity off so that knockback assertions measure the
        /// punch and nothing else. The distance tests need the opposite: knockback distance is a
        /// ballistic figure, so it only means anything with gravity pulling the creature back down.
        /// </remarks>
        HordeEnemy CreateFallingEnemy(float x = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.SetActive(false);
            go.name = "Test Falling Enemy";
            go.transform.position = new Vector3(x, HordePocLayout.k_DummyCenterHeight, 0f);
            go.transform.localScale = Vector3.one * HordePocLayout.k_DummyScale;
            m_Created.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = HordePocLayout.k_DummyMass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var enemy = go.AddComponent<HordeEnemy>();
            go.SetActive(true);

            return enemy;
        }

        /// <summary>A floor wide enough that a hard punch cannot throw anything off the edge.</summary>
        void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Test Ground";
            ground.transform.localScale = new Vector3(80f, 0.2f, 80f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            m_Created.Add(ground);
        }

        static float HorizontalTravel(Vector3 from, Vector3 to) =>
            Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));

        static Vector3 Swing(float speed) => Vector3.forward * speed;

        /// <summary>
        /// Waits until an impulse handed to the physics engine has actually been simulated.
        /// </summary>
        /// <remarks>
        /// One <c>WaitForFixedUpdate</c> is not enough: it resumes after the <c>FixedUpdate</c>
        /// calls but before the physics step that turns accumulated forces into velocity, so the
        /// Rigidbody would still report the velocity it had before the punch.
        /// </remarks>
        static IEnumerator SimulatePhysics()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        /// <summary>
        /// Punches <paramref name="enemy"/> at full power until it dies, failing rather than
        /// spinning forever if it turns out to be unkillable.
        /// </summary>
        static void PunchToDeath(HordeEnemy enemy, PunchSettings settings)
        {
            const int maxPunches = 20;

            for (int i = 0; i < maxPunches && enemy.isAlive; i++)
                enemy.ReceivePunch(Swing(settings.maxSpeed), settings);

            Assert.That(enemy.isAlive, Is.False,
                $"The enemy survived {maxPunches} full power punches.");
        }

        [UnityTest]
        public IEnumerator Enemy_StartsAtFullHealth()
        {
            var enemy = CreateEnemy(Vector3.zero);
            yield return null;

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth));
            Assert.That(enemy.isAlive, Is.True);
            Assert.That(enemy.maxHealth, Is.GreaterThan(1), "An enemy that dies to one punch is not a fight.");
        }

        [UnityTest]
        public IEnumerator ReceivePunch_TakesHealthOffTheEnemy()
        {
            var enemy = CreateEnemy(Vector3.zero);
            int before = enemy.health;

            var outcome = enemy.ReceivePunch(Swing(k_StandardPunchSpeed), new PunchSettings());
            yield return null;

            Assert.That(outcome.landed, Is.True);
            Assert.That(enemy.health, Is.EqualTo(before - outcome.damage),
                "The enemy reported damage it never applied to its own health.");
        }

        [UnityTest]
        public IEnumerator ReceivePunch_IgnoresASwingBelowTheThreshold()
        {
            var enemy = CreateEnemy(Vector3.zero);
            var settings = new PunchSettings();

            var outcome = enemy.ReceivePunch(Swing(settings.minSpeed - 0.5f), settings);
            yield return SimulatePhysics();

            Assert.That(outcome.landed, Is.False);
            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth), "A graze cost the enemy health.");
            Assert.That(enemy.GetComponent<Rigidbody>().linearVelocity.magnitude, Is.LessThan(0.01f),
                "A graze knocked the enemy back.");
        }

        /// <summary>
        /// The punch model decides how hard the knockback should be; this is the check that the
        /// enemy actually hands that impulse to the physics engine rather than computing it and
        /// throwing it away.
        /// </summary>
        [UnityTest]
        public IEnumerator ReceivePunch_ActuallyThrowsTheEnemy()
        {
            var enemy = CreateEnemy(Vector3.zero);
            var body = enemy.GetComponent<Rigidbody>();

            var outcome = enemy.ReceivePunch(Swing(5f), new PunchSettings());
            yield return SimulatePhysics();

            var velocity = body.linearVelocity;

            Assert.That(velocity.magnitude, Is.GreaterThan(1f),
                $"The enemy is barely moving at {velocity.magnitude:F2} m/s after a 5 m/s punch.");
            Assert.That(velocity.z, Is.GreaterThan(0f), "The enemy was not thrown away from the punch.");
            Assert.That(velocity.y, Is.GreaterThan(0f), "The enemy slid along the floor instead of flying.");

            // Knockback is applied as a velocity, so what the enemy ends up doing is exactly what
            // the punch model asked for, whatever the creature weighs.
            Assert.That(velocity.magnitude, Is.EqualTo(outcome.launchVelocity.magnitude).Within(0.1f),
                "The knockback the enemy received does not match the launch the punch resolved.");
        }

        /// <summary>
        /// The knockback model measured the way the player experiences it: with gravity, a floor,
        /// and a creature that lands and slides. Everything else in this fixture checks the launch;
        /// this checks where the creature actually ends up.
        /// </summary>
        /// <remarks>
        /// The first version of the model scaled an impulse with hand speed. It looked reasonable
        /// written down and moved an ordinary punch about 30 cm — the creature landed beside the
        /// player and was climbing back on before the fist had come down. Nothing caught it, because
        /// every test asserted on the impulse rather than on where anyone ended up.
        /// </remarks>
        [UnityTest]
        public IEnumerator ReceivePunch_ThrowsTheEnemyMetersAwayNotCentimetres()
        {
            CreateGround();
            var enemy = CreateFallingEnemy();
            var settings = new PunchSettings();
            yield return null;

            var start = enemy.transform.position;

            // The weakest swing that still counts as a punch, so this measures the floor of the
            // model rather than a best case.
            var outcome = enemy.ReceivePunch(Swing(settings.minSpeed), settings);
            Assume.That(outcome.landed, Is.True);

            yield return new WaitForSeconds(2.5f);

            float travelled = HorizontalTravel(start, enemy.transform.position);

            Assert.That(travelled, Is.GreaterThanOrEqualTo(settings.minKnockbackDistance),
                $"The weakest punch that counts moved the enemy {travelled:F2} m, short of the " +
                $"{settings.minKnockbackDistance:F1} m the model promises. It lands next to the " +
                "player and walks straight back in.");
        }

        [UnityTest]
        public IEnumerator ReceivePunch_ThrowsAHardPunchFurtherAcrossTheFloor()
        {
            CreateGround();
            var enemy = CreateFallingEnemy();
            var settings = new PunchSettings();
            yield return null;

            var start = enemy.transform.position;
            enemy.ReceivePunch(Swing(settings.maxSpeed), settings);

            yield return new WaitForSeconds(2.5f);

            float travelled = HorizontalTravel(start, enemy.transform.position);

            Assert.That(travelled, Is.GreaterThanOrEqualTo(settings.maxKnockbackDistance),
                $"A full power punch moved the enemy {travelled:F2} m, short of the " +
                $"{settings.maxKnockbackDistance:F1} m the model promises.");
        }

        /// <summary>
        /// Compared in meters travelled rather than in launch speed. Under a distance-based model
        /// the speed only grows with the square root of the range, so two punches that land a
        /// creature twice as far apart differ by about 40% in speed — a comparison on speed reads
        /// as "barely harder" while the thing the player watches has doubled.
        /// </summary>
        [UnityTest]
        public IEnumerator ReceivePunch_ThrowsAHardPunchFurtherThanASoftOne()
        {
            CreateGround();

            // Far apart, so neither creature lands on the other and skews the measurement.
            var softly = CreateFallingEnemy();
            var hard = CreateFallingEnemy(30f);
            var settings = new PunchSettings();
            yield return null;

            var softStart = softly.transform.position;
            var hardStart = hard.transform.position;

            softly.ReceivePunch(Swing(2f), settings);
            hard.ReceivePunch(Swing(7f), settings);

            yield return new WaitForSeconds(2.5f);

            float softTravel = HorizontalTravel(softStart, softly.transform.position);
            float hardTravel = HorizontalTravel(hardStart, hard.transform.position);

            Assert.That(hardTravel - softTravel, Is.GreaterThanOrEqualTo(3f),
                $"A 7 m/s punch sent the enemy {hardTravel:F2} m and a 2 m/s one {softTravel:F2} m. " +
                "Hitting harder has to be worth meters, or there is no reason to swing hard.");
        }

        [UnityTest]
        public IEnumerator ReceivePunch_KillsTheEnemyOnTheThirdStandardPunch()
        {
            var enemy = CreateEnemy(Vector3.zero);
            var settings = new PunchSettings();
            int deaths = 0;
            enemy.OnDied += _ => deaths++;

            Assume.That(enemy.maxHealth, Is.EqualTo(3), "This test is written against a three-health enemy.");

            enemy.ReceivePunch(Swing(k_StandardPunchSpeed), settings);
            Assert.That(enemy.isAlive, Is.True, "The enemy died on the first punch.");

            enemy.ReceivePunch(Swing(k_StandardPunchSpeed), settings);
            Assert.That(enemy.isAlive, Is.True, "The enemy died on the second punch.");
            Assert.That(deaths, Is.Zero);

            enemy.ReceivePunch(Swing(k_StandardPunchSpeed), settings);
            yield return null;

            Assert.That(enemy.isAlive, Is.False, "The enemy survived the third standard punch.");
            Assert.That(enemy.health, Is.Zero);
            Assert.That(deaths, Is.EqualTo(1), "The death event did not fire exactly once.");
        }

        [UnityTest]
        public IEnumerator ReceivePunch_RaisesOnPunchedForEveryPunchThatLands()
        {
            var enemy = CreateEnemy(Vector3.zero);
            var settings = new PunchSettings();
            var landed = new List<PunchOutcome>();
            enemy.OnPunched += landed.Add;

            enemy.ReceivePunch(Swing(settings.minSpeed - 0.5f), settings);
            Assert.That(landed, Is.Empty, "A graze was reported as a punch.");

            enemy.ReceivePunch(Swing(k_StandardPunchSpeed), settings);
            yield return null;

            Assert.That(landed.Count, Is.EqualTo(1));
            Assert.That(landed[0].damage, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator ReceivePunch_DoesNothingToAnEnemyThatIsAlreadyDead()
        {
            var enemy = CreateEnemy(Vector3.zero);
            var settings = new PunchSettings();
            int deaths = 0;
            enemy.OnDied += _ => deaths++;

            PunchToDeath(enemy, settings);

            yield return null;

            var outcome = enemy.ReceivePunch(Swing(settings.maxSpeed), settings);

            Assert.That(outcome.landed, Is.False, "A corpse absorbed another punch.");
            Assert.That(deaths, Is.EqualTo(1), "The enemy died more than once, so the kill count would run away.");
        }

        [UnityTest]
        public IEnumerator Respawn_PutsTheEnemyBackWhereItStartedAtFullHealth()
        {
            var spawn = new Vector3(1f, 0.5f, 2f);
            var enemy = CreateEnemy(spawn);
            var settings = new PunchSettings();

            PunchToDeath(enemy, settings);

            // Let the corpse fly somewhere else before putting it back.
            yield return SimulatePhysics();
            Assume.That(Vector3.Distance(enemy.transform.position, spawn), Is.GreaterThan(0.001f),
                "The corpse never moved, so putting it back proves nothing.");

            enemy.Respawn();
            yield return null;

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth));
            Assert.That(enemy.isAlive, Is.True);
            Assert.That(Vector3.Distance(enemy.transform.position, spawn), Is.LessThan(0.001f),
                "The enemy respawned somewhere other than where it started.");
            Assert.That(enemy.GetComponent<Rigidbody>().linearVelocity.magnitude, Is.LessThan(0.01f),
                "The respawned enemy kept the velocity that its corpse had.");
        }

        [UnityTest]
        public IEnumerator Respawn_MakesTheEnemyPunchableAgain()
        {
            var enemy = CreateEnemy(Vector3.zero);
            var settings = new PunchSettings();

            PunchToDeath(enemy, settings);

            enemy.Respawn();
            yield return null;

            var outcome = enemy.ReceivePunch(Swing(k_StandardPunchSpeed), settings);

            Assert.That(outcome.landed, Is.True, "The respawned enemy cannot be punched.");
            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth - outcome.damage));
        }

        /// <summary>
        /// A hidden corpse still has a collider, and a live enemy walking into it would be blocked
        /// by something the player cannot see.
        /// </summary>
        [UnityTest]
        public IEnumerator Death_TakesTheCorpseOutOfTheWorldAndBringsItBack()
        {
            var enemy = CreateEnemy(Vector3.zero);
            var settings = new PunchSettings();

            PunchToDeath(enemy, settings);

            Assert.That(enemy.GetComponent<Renderer>().enabled, Is.True,
                "The corpse vanished on the frame it died, so the killing blow is never seen.");

            // Long enough to cover the corpse lingering and then the respawn timer.
            yield return new WaitForSeconds(5.5f);

            Assert.That(enemy.isAlive, Is.True, "The enemy never came back, so the POC can only be tried once.");
            Assert.That(enemy.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(enemy.GetComponent<Collider>().enabled, Is.True);
        }
    }
}
