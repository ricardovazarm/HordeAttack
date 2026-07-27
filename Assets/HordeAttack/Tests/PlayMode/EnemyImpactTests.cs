using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Throws enemies into each other and into the floor with the real physics engine, and checks
    /// what comes out.
    /// </summary>
    /// <remarks>
    /// PlayMode because none of it means anything without a running simulation: the speed an impact
    /// is scored on is the one Unity reports from an actual contact, and the contact normal that
    /// separates "drove into the floor" from "skidded along it" only exists once two colliders have
    /// really met.
    /// <para>
    /// The enemies are launched by writing a velocity rather than by being punched, so a failure here
    /// is about the impact model and not about the punch that would otherwise have produced the
    /// projectile. The tie between the two models — a punch landing must not be a lethal impact — is
    /// pinned in <c>ImpactResolverTests</c>, where it can be stated as an inequality between the two
    /// sets of tuning instead of hoping a simulation happens to reproduce it.
    /// </para>
    /// </remarks>
    public class EnemyImpactTests
    {
        /// <summary>
        /// A throw hard enough to count and soft enough to be worth exactly one damage, in m/s.
        /// </summary>
        /// <remarks>
        /// Sits with room on both sides of it in the creature band, so a small loss of speed to the
        /// angle of the contact cannot drop it below the threshold and it cannot creep up into the
        /// next damage step either. That is what makes "one damage each" readable as "charged once".
        /// </remarks>
        const float k_ModestThrowSpeed = 8f;

        /// <summary>A shove, well under anything that should count, in m/s.</summary>
        const float k_ShoveSpeed = 3f;

        /// <summary>A committed downward slam, in m/s.</summary>
        const float k_SlamSpeed = 9f;

        /// <summary>Letting go of a creature at head height, in m/s.</summary>
        const float k_DropSpeed = 4f;

        /// <summary>Standing eye height the test player is built at.</summary>
        const float k_EyeHeight = 1.7f;

        /// <summary>How many physics steps a test waits before giving up on a collision.</summary>
        const int k_MaxSteps = 120;

        readonly List<GameObject> m_Created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in m_Created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            m_Created.Clear();
        }

        /// <summary>
        /// Builds an enemy that only moves because something moved it.
        /// </summary>
        /// <remarks>
        /// No locomotion and no gravity by default, so the speed it hits things at is the speed the
        /// test gave it. Rotation is frozen so a capsule cannot tip over on contact and turn a clean
        /// head-on impact into a glancing one.
        /// </remarks>
        HordeEnemy CreateEnemy(Vector3 position, bool withGravity = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.SetActive(false);
            go.name = "Test Enemy";
            m_Created.Add(go);

            go.transform.position = position;
            go.transform.localScale = Vector3.one * HordePocLayout.k_DummyScale;

            var body = go.AddComponent<Rigidbody>();
            body.mass = HordePocLayout.k_DummyMass;
            body.useGravity = withGravity;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            go.AddComponent<PointVelocityTracker>();
            go.AddComponent<ImpactDetector>();

            var enemy = go.AddComponent<HordeEnemy>();

            go.SetActive(true);

            return enemy;
        }

        /// <summary>Builds a solid floor with its surface at y = 0, like the arena's.</summary>
        void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Test Ground";
            ground.transform.localScale = new Vector3(20f, 0.2f, 20f);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            m_Created.Add(ground);
        }

        /// <summary>Builds a player with the anchors from the real layout, as a latch target.</summary>
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

        static LatchAnchor AnchorNamed(PlayerLatchTarget player, string name)
        {
            foreach (var anchor in player.anchors)
            {
                if (anchor.name == name)
                    return anchor;
            }

            Assert.Fail($"The test rig has no '{name}' anchor.");

            return null;
        }

        static void Launch(HordeEnemy enemy, Vector3 velocity) =>
            enemy.GetComponent<Rigidbody>().linearVelocity = velocity;

        /// <summary>
        /// Runs the physics engine until one of <paramref name="enemies"/> loses health, or until the
        /// step budget runs out.
        /// </summary>
        /// <remarks>
        /// Stopping on the first change matters. Two bodies that have just collided stay in contact
        /// and can report a second one a few steps later, so a test that simply waited a fixed time
        /// and then looked would sometimes see the damage of one impact and sometimes of two — and
        /// "charged exactly once" is precisely what several of these are asserting.
        /// </remarks>
        static IEnumerator UntilHurt(params HordeEnemy[] enemies)
        {
            for (int step = 0; step < k_MaxSteps; step++)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy.health < enemy.maxHealth)
                        yield break;
                }

                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>Runs the physics engine for <paramref name="steps"/> fixed updates.</summary>
        static IEnumerator Simulate(int steps)
        {
            for (int i = 0; i < steps; i++)
                yield return new WaitForFixedUpdate();
        }

        /// <summary>
        /// The heart of the phase: a creature let go of at speed is a weapon, and it costs both of
        /// them.
        /// </summary>
        /// <remarks>
        /// Also the check that Unity reporting the same collision to both bodies does not charge the
        /// pair twice: exactly one damage each is the whole assertion, not "some damage".
        /// </remarks>
        [UnityTest]
        public IEnumerator Throw_IntoAnotherEnemy_HurtsBothOfThemExactlyOnce()
        {
            var struck = CreateEnemy(Vector3.zero);
            var thrown = CreateEnemy(new Vector3(0f, 0f, -2f));
            yield return null;

            Launch(thrown, Vector3.forward * k_ModestThrowSpeed);
            yield return UntilHurt(thrown, struck);

            Assert.That(struck.health, Is.EqualTo(struck.maxHealth - 1),
                $"The creature that was hit went from {struck.maxHealth} to {struck.health}.");
            Assert.That(thrown.health, Is.EqualTo(thrown.maxHealth - 1),
                $"The creature that was thrown went from {thrown.maxHealth} to {thrown.health}. " +
                "Both sides of a collision pay, and each of them pays once.");
        }

        [UnityTest]
        public IEnumerator Shoving_IntoAnotherEnemy_HurtsNeither()
        {
            var struck = CreateEnemy(Vector3.zero);
            var thrown = CreateEnemy(new Vector3(0f, 0f, -2f));
            yield return null;

            Launch(thrown, Vector3.forward * k_ShoveSpeed);
            yield return Simulate(k_MaxSteps);

            Assert.That(struck.health, Is.EqualTo(struck.maxHealth),
                "Two creatures bumping into each other counted as a throw.");
            Assert.That(thrown.health, Is.EqualTo(thrown.maxHealth));
        }

        /// <summary>
        /// Throwing a creature at one that has hold of you is the other way to get it off, and the
        /// interesting part is that a clinging creature is kinematic and parented to the player: it
        /// has to be let go of, or it takes the damage and stays exactly where it was.
        /// </summary>
        [UnityTest]
        public IEnumerator Throw_AtALatchedEnemy_KnocksItOffThePlayer()
        {
            var player = CreatePlayer(Vector3.zero);
            var anchor = AnchorNamed(player, "Waist Right");

            var clinging = CreateEnemy(anchor.transform.position);
            yield return null;

            Assume.That(anchor.TryOccupy(clinging) && player.CompleteLatch(clinging, anchor), Is.True);
            yield return null;

            var thrown = CreateEnemy(clinging.transform.position + Vector3.back * 2f);
            yield return null;

            Launch(thrown, Vector3.forward * k_ModestThrowSpeed);
            yield return UntilHurt(thrown, clinging);

            Assert.That(clinging.health, Is.LessThan(clinging.maxHealth),
                "The thrown creature went straight through the one holding the player.");
            Assert.That(clinging.isLatched, Is.False,
                "It took the hit and kept its grip, so throwing things is no way to get one off.");
            Assert.That(anchor.isFree, Is.True);
        }

        [UnityTest]
        public IEnumerator Slamming_IntoTheGround_KillsAHealthyEnemy()
        {
            CreateGround();
            var enemy = CreateEnemy(new Vector3(0f, 1.5f, 0f));
            yield return null;

            Launch(enemy, Vector3.down * k_SlamSpeed);
            yield return UntilHurt(enemy);

            Assert.That(enemy.isAlive, Is.False,
                $"Spiking a gnome into the floor at {k_SlamSpeed} m/s left it on " +
                $"{enemy.health} health.");
        }

        [UnityTest]
        public IEnumerator Dropping_OntoTheGround_LeavesTheEnemyUnharmed()
        {
            CreateGround();
            var enemy = CreateEnemy(new Vector3(0f, 1.5f, 0f));
            yield return null;

            Launch(enemy, Vector3.down * k_DropSpeed);
            yield return Simulate(k_MaxSteps);

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth),
                "Letting a creature go at head height killed it; a drop is not a throw.");
        }

        /// <summary>
        /// An enemy that was punched skids across the arena in contact with the floor the whole way.
        /// Scored on raw closing speed that is a full power impact on every physics step, and the
        /// creature would die of sliding rather than of anything the player did to it.
        /// </summary>
        [UnityTest]
        public IEnumerator Skidding_AcrossTheGround_LeavesTheEnemyUnharmed()
        {
            CreateGround();

            // Resting height, so it is already touching the floor and only travelling sideways.
            var enemy = CreateEnemy(new Vector3(0f, HordePocLayout.k_DummyCenterHeight, 0f), withGravity: true);
            yield return null;

            Launch(enemy, Vector3.forward * 12f);
            yield return Simulate(k_MaxSteps);

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth),
                $"Sliding along the ground ground the creature down to {enemy.health} health.");
        }
    }
}
