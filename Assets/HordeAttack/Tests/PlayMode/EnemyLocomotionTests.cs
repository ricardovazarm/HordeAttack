using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Drives enemies at a player with the real physics engine and the real clock.
    /// </summary>
    /// <remarks>
    /// These are the tests that cover the join between the steering maths, the Rigidbody and the
    /// latch bookkeeping, which is where the behaviour actually lives. None of it can be checked in
    /// edit mode: nothing calls <c>FixedUpdate</c>, and a velocity written to a Rigidbody only turns
    /// into movement when the physics engine steps.
    /// </remarks>
    public class EnemyLocomotionTests
    {
        const float k_EyeHeight = 1.7f;

        /// <summary>Height enemies are held at, so gravity can stay off and nothing falls.</summary>
        const float k_StandHeight = 0.5f;

        readonly List<GameObject> m_Created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            // Immediate, so the static player registry FindNearest reads is empty again before the
            // next test builds its own player.
            foreach (var go in m_Created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            m_Created.Clear();
        }

        PlayerLatchTarget CreatePlayer(Vector3 position)
        {
            var root = new GameObject("Test Player");
            root.transform.position = position;
            m_Created.Add(root);

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = Vector3.up * k_EyeHeight;

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);

            foreach (var layout in HordePocLayout.k_BodyAnchors)
            {
                var anchor = new GameObject(layout.name);
                anchor.transform.SetParent(body.transform, false);
                anchor.AddComponent<LatchAnchor>().Configure(
                    layout.height, layout.heightFraction, layout.bodyOffset, layout.hangDrop);
            }

            body.AddComponent<PlayerBodyProxy>().head = head.transform;

            return body.AddComponent<PlayerLatchTarget>();
        }

        /// <summary>
        /// Builds an enemy that hunts, with gravity off so the arena needs no floor and nothing the
        /// test measures comes from falling.
        /// </summary>
        /// <remarks>
        /// Assembled while the object is switched off and only then activated, which is the one way
        /// to get the same result a scene loaded from disk gives. Adding components to a live object
        /// runs each <c>Awake</c> as it is attached, so a component looks for its neighbours before
        /// they exist — and worse, attaching <see cref="EnemyLocomotion"/> first makes Unity satisfy
        /// its <c>RequireComponent</c> by adding a <see cref="HordeEnemy"/> of its own, leaving the
        /// test holding a different enemy from the one the locomotion drives.
        /// </remarks>
        HordeEnemy CreateHunter(Vector3 position, LatchStyle style)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.SetActive(false);
            go.name = $"Test {style}";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * HordePocLayout.k_DummyScale;
            m_Created.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = HordePocLayout.k_DummyMass;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            go.AddComponent<PointVelocityTracker>();
            var enemy = go.AddComponent<HordeEnemy>();
            go.AddComponent<EnemyLocomotion>().style = style;

            go.SetActive(true);

            return enemy;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        [UnityTest]
        public IEnumerator Walking_ClosesTheDistanceToThePlayer()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 5f), LatchStyle.Clinger);
            yield return null;

            float before = HorizontalDistance(enemy.transform.position, player.bodyPosition);
            yield return new WaitForSeconds(1f);
            float after = HorizontalDistance(enemy.transform.position, player.bodyPosition);

            Assert.That(after, Is.LessThan(before - 1f),
                $"The enemy went from {before:F2} m to {after:F2} m in a second; it is barely moving.");
        }

        [UnityTest]
        public IEnumerator Walking_TurnsToFaceWhereItIsGoing()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 5f), LatchStyle.Clinger);
            yield return new WaitForSeconds(0.5f);

            var toPlayer = player.bodyPosition - enemy.transform.position;
            toPlayer.y = 0f;

            Assert.That(Vector3.Dot(enemy.transform.forward, toPlayer.normalized), Is.GreaterThan(0.8f),
                "The enemy is walking sideways or backwards toward the player.");
        }

        /// <summary>
        /// A capsule driven along the floor tips over the moment it touches anything, and a horde of
        /// creatures lying on their sides sliding toward the player is not the intended read.
        /// </summary>
        [UnityTest]
        public IEnumerator Walking_KeepsTheEnemyOnItsFeet()
        {
            CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 5f), LatchStyle.Clinger);
            yield return new WaitForSeconds(0.3f);

            var constraints = enemy.GetComponent<Rigidbody>().constraints;

            Assert.That(constraints & RigidbodyConstraints.FreezeRotation,
                Is.EqualTo(RigidbodyConstraints.FreezeRotation),
                "Nothing is holding the walking enemy upright.");
        }

        [UnityTest]
        public IEnumerator Clinger_WalksInAndTakesHoldLow()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 2f), LatchStyle.Clinger);

            yield return new WaitForSeconds(2f);

            Assert.That(enemy.isLatched, Is.True, "The creature walked up to the player and did nothing.");
            Assert.That(enemy.latchAnchor.height, Is.EqualTo(LatchHeight.Low),
                $"A creature that arrived on foot ended up on '{enemy.latchAnchor.name}', which is " +
                "a high anchor it should have had to jump for.");
        }

        [UnityTest]
        public IEnumerator Leaper_JumpsAndEndsUpHoldingOnHigh()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 1.5f), LatchStyle.Leaper);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(enemy.state, Is.EqualTo(EnemyState.Leaping),
                "The creature was in range and stayed on the ground.");

            yield return new WaitForSeconds(1.5f);

            Assert.That(enemy.isLatched, Is.True, "The jump never ended in a hold.");
            Assert.That(enemy.latchAnchor.height, Is.EqualTo(LatchHeight.High),
                $"The leaper landed on '{enemy.latchAnchor.name}' rather than on the player's chest.");
        }

        [UnityTest]
        public IEnumerator Leaper_DoesNotJumpFromAcrossTheArena()
        {
            CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 6f), LatchStyle.Leaper);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(enemy.state, Is.EqualTo(EnemyState.Walking),
                "The creature launched itself from six meters away and would land on nothing.");
        }

        /// <summary>
        /// The kinematic trap, from the other side. An enemy mid-leap is kinematic so its scripted
        /// arc cannot be knocked off line by a stray collision — which also means
        /// <c>Rigidbody.AddForce</c> on it is discarded in silence. Without an explicit release, a
        /// punch would take health off a creature that calmly finished its jump onto the player who
        /// threw it.
        /// </summary>
        [UnityTest]
        public IEnumerator Punch_MidLeap_StopsTheJumpAndThrowsTheEnemy()
        {
            CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 1.5f), LatchStyle.Leaper);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assume.That(enemy.state, Is.EqualTo(EnemyState.Leaping), "The creature never left the ground.");

            var outcome = enemy.ReceivePunch(Vector3.back * 4f, new PunchSettings());

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            var body = enemy.GetComponent<Rigidbody>();

            Assert.That(outcome.landed, Is.True);
            Assert.That(enemy.isLatched, Is.False, "The punched creature completed its jump anyway.");
            Assert.That(body.isKinematic, Is.False, "The creature is still kinematic and cannot be moved.");
            Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(1f),
                $"The creature was knocked back at {body.linearVelocity.magnitude:F2} m/s, so the " +
                "impulse was swallowed by the kinematic body it had mid-jump.");
        }

        [UnityTest]
        public IEnumerator Punch_MidLeap_GivesBackTheSpotItWasAimingAt()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 1.5f), LatchStyle.Leaper);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assume.That(enemy.state, Is.EqualTo(EnemyState.Leaping));

            enemy.ReceivePunch(Vector3.back * 4f, new PunchSettings());
            yield return null;

            Assert.That(player.latchedCount, Is.Zero,
                "The anchor the interrupted jump had booked is still reserved, so the player " +
                "permanently lost a place to be grabbed.");
        }

        /// <summary>
        /// Locomotion writes the Rigidbody's velocity outright, so without a window in which it
        /// keeps its hands off, the physics step after a punch would overwrite the knockback with a
        /// walking velocity and the enemy would appear to shrug the hit off without moving.
        /// </summary>
        [UnityTest]
        public IEnumerator Knockback_IsNotErasedByTheNextWalkingStep()
        {
            CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 5f), LatchStyle.Clinger);
            yield return new WaitForSeconds(0.3f);

            // Thrown directly away from the player, against the direction it was walking.
            enemy.ReceivePunch(Vector3.forward * 5f, new PunchSettings());

            yield return new WaitForSeconds(0.2f);

            Assert.That(enemy.GetComponent<Rigidbody>().linearVelocity.z, Is.GreaterThan(0f),
                "A fifth of a second after the punch the enemy is already walking back in, so the " +
                "knockback was never visible.");
        }

        [UnityTest]
        public IEnumerator Recovery_EndsAndTheEnemyComesBackForMore()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 3f), LatchStyle.Clinger);
            yield return new WaitForSeconds(0.3f);

            enemy.ReceivePunch(Vector3.forward * 5f, new PunchSettings());

            // Measured once the knockback has played out rather than at the moment of the punch.
            // With no ground to brake against, the creature coasts several meters during the
            // recovery window, so comparing against where it was hit would only prove that a punch
            // throws things — which is Fase 1's job, not this test's.
            var settings = enemy.GetComponent<EnemyLocomotion>().settings;
            yield return new WaitForSeconds(settings.knockbackRecovery + 0.1f);
            float thrownTo = HorizontalDistance(enemy.transform.position, player.bodyPosition);

            yield return new WaitForSeconds(1f);

            Assert.That(HorizontalDistance(enemy.transform.position, player.bodyPosition),
                Is.LessThan(thrownTo - 1f),
                "The enemy never got up again after being punched, so one hit takes a creature out " +
                "of the fight for good.");
        }

        [UnityTest]
        public IEnumerator Latched_EnemiesStopDrivingThemselves()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateHunter(new Vector3(0f, k_StandHeight, 1f), LatchStyle.Clinger);
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            yield return new WaitForSeconds(0.3f);

            Assert.That(enemy.GetComponent<Rigidbody>().isKinematic, Is.True,
                "Locomotion handed the body back to physics while the creature was holding on, so " +
                "it would slide off the player it had hold of.");
            Assert.That(enemy.isLatched, Is.True);
        }
    }
}
