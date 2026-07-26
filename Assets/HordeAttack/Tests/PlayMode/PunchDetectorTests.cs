using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Drives a fist through an enemy with the real physics engine and checks that a punch comes
    /// out of the other end.
    /// </summary>
    /// <remarks>
    /// This is the only test that covers the whole chain — trigger contact, the velocity the
    /// tracker measured, the punch model, and the damage — rather than any one link of it. The
    /// fist is moved frame by frame rather than teleported, because a teleport produces no measured
    /// velocity and would land nothing.
    /// </remarks>
    public class PunchDetectorTests
    {
        /// <summary>
        /// An ordinary swing. Deliberately in the lower half of the power range so a hit is worth
        /// exactly one damage, which is what makes "how many punches landed" readable from health.
        /// </summary>
        const float k_StandardPunchSpeed = 3f;

        /// <summary>How far in front of the enemy a swing starts, in meters.</summary>
        const float k_Runway = 1f;

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
        /// Builds an enemy that is pinned in place.
        /// </summary>
        /// <remarks>
        /// Frozen rather than kinematic: a kinematic body would make <see cref="HordeEnemy"/> skip
        /// applying knockback entirely, so the test would no longer exercise the same path the game
        /// takes. Freezing keeps the code path and stops the enemy being punched out of reach
        /// between one swing and the next.
        /// </remarks>
        HordeEnemy CreateEnemy()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Test Enemy";
            go.transform.position = Vector3.zero;
            go.transform.localScale = Vector3.one * 0.5f;
            m_Created.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = HordePocLayout.k_DummyMass;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;

            return go.AddComponent<HordeEnemy>();
        }

        /// <summary>Builds a fist wired the same way the scene builder wires the ones on the rig.</summary>
        PunchDetector CreateFist(Vector3 position)
        {
            var go = new GameObject("Test Fist");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * HordePocLayout.k_FistDiameter;
            m_Created.Add(go);

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = HordePocLayout.k_PunchTriggerLocalRadius;

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            go.AddComponent<HandVelocityTracker>();
            return go.AddComponent<PunchDetector>();
        }

        /// <summary>
        /// Moves <paramref name="fist"/> along <paramref name="direction"/> a frame at a time, so the
        /// velocity tracker sees real motion over real elapsed time.
        /// </summary>
        static IEnumerator Swing(Transform fist, Vector3 direction, float speed, float distance)
        {
            float travelled = 0f;

            // A bound rather than a while(true): if the frame clock ever stops advancing, the test
            // should fail on its assertions rather than hang the whole run.
            for (int frame = 0; travelled < distance && frame < 5000; frame++)
            {
                float step = speed * Time.unscaledDeltaTime;
                fist.position += direction * step;
                travelled += step;

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Swing_ThroughAnEnemy_LandsExactlyOnePunch()
        {
            var enemy = CreateEnemy();
            var fist = CreateFist(new Vector3(0f, 0f, -k_Runway));

            yield return Swing(fist.transform, Vector3.forward, k_StandardPunchSpeed, k_Runway * 2f);

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth - 1),
                $"A single swing through the enemy took it from {enemy.maxHealth} to {enemy.health}. " +
                "One hit per swing is what the re-hit cooldown is for.");
        }

        [UnityTest]
        public IEnumerator Swing_TooSlowly_LandsNothingAtAll()
        {
            var enemy = CreateEnemy();
            var settings = new PunchSettings();
            var fist = CreateFist(new Vector3(0f, 0f, -k_Runway));

            yield return Swing(fist.transform, Vector3.forward, settings.minSpeed * 0.5f, k_Runway * 2f);

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth),
                "Slowly pushing a hand through an enemy counted as a punch.");
        }

        [UnityTest]
        public IEnumerator Swing_TwiceInARow_LandsTwoPunches()
        {
            var enemy = CreateEnemy();
            var fist = CreateFist(new Vector3(0f, 0f, -k_Runway));

            yield return Swing(fist.transform, Vector3.forward, k_StandardPunchSpeed, k_Runway * 2f);
            yield return Swing(fist.transform, Vector3.back, k_StandardPunchSpeed, k_Runway * 2f);

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth - 2),
                "The second swing did not land; the cooldown is locking the enemy out for too long.");
        }

        /// <summary>
        /// A hand resting inside an enemy must not grind its health down. Trigger stay events keep
        /// firing every physics step for as long as the overlap lasts, and only the speed threshold
        /// and the cooldown stand between that and an instant kill.
        /// </summary>
        [UnityTest]
        public IEnumerator RestingInsideAnEnemy_DoesNotKeepDealingDamage()
        {
            var enemy = CreateEnemy();
            var fist = CreateFist(new Vector3(0f, 0f, -k_Runway));

            yield return Swing(fist.transform, Vector3.forward, k_StandardPunchSpeed, k_Runway);
            int afterContact = enemy.health;

            // The hand is now on top of the enemy and stops dead. Wait out several cooldowns.
            yield return new WaitForSeconds(1f);

            Assert.That(enemy.health, Is.EqualTo(afterContact),
                $"A motionless hand inside the enemy drained it from {afterContact} to {enemy.health}.");
        }

        [UnityTest]
        public IEnumerator Swing_PastAnEnemy_LeavesItAlone()
        {
            var enemy = CreateEnemy();

            // Same swing, a meter to the side.
            var fist = CreateFist(new Vector3(1f, 0f, -k_Runway));

            yield return Swing(fist.transform, Vector3.forward, k_StandardPunchSpeed, k_Runway * 2f);

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth),
                "A punch thrown into thin air damaged an enemy a meter away.");
        }
    }
}
