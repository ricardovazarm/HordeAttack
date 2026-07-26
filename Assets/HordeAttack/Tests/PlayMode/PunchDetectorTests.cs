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
        /// <remarks>
        /// Parented before <see cref="PunchDetector"/> is attached, because the detector remembers
        /// in <c>Awake</c> which branch of the hierarchy it hangs from — that is how it knows which
        /// latched creature is on its own arm.
        /// </remarks>
        PunchDetector CreateFist(Vector3 position, Transform hand = null)
        {
            var go = new GameObject("Test Fist");
            m_Created.Add(go);

            if (hand != null)
                go.transform.SetParent(hand, false);

            go.transform.position = position;
            go.transform.localScale = Vector3.one * HordePocLayout.k_FistDiameter;

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = HordePocLayout.k_PunchTriggerLocalRadius;

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            go.AddComponent<PointVelocityTracker>();
            return go.AddComponent<PunchDetector>();
        }

        /// <summary>
        /// Builds a player with a body to be grabbed and one hand carrying a fist and an arm anchor.
        /// </summary>
        /// <remarks>
        /// Cut down to what these tests need — one hand rather than four, and no leaping — but wired
        /// the way the scene builder wires the real rig, because the two behaviours under test are
        /// entirely about which branch of the hierarchy things sit in.
        /// </remarks>
        PlayerLatchTarget CreateRig(Vector3 handPosition, out Transform hand, out PunchDetector fist)
        {
            var root = new GameObject("Test Rig");
            m_Created.Add(root);

            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = Vector3.up * 1.7f;

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);

            foreach (var layout in HordePocLayout.k_BodyAnchors)
            {
                var anchor = new GameObject(layout.name);
                anchor.transform.SetParent(body.transform, false);
                anchor.AddComponent<LatchAnchor>().Configure(
                    layout.height, layout.heightFraction, layout.bodyOffset, layout.hangDrop);
            }

            var handObject = new GameObject("Hand");
            handObject.transform.SetParent(root.transform, false);
            handObject.transform.position = handPosition;
            hand = handObject.transform;

            var arm = new GameObject(HordePocLayout.k_ArmAnchorName);
            arm.transform.SetParent(hand, false);
            arm.transform.localPosition = Vector3.back * HordePocLayout.k_ArmAnchorSetback;
            arm.AddComponent<LatchAnchor>().Configure(LatchHeight.Low, 0f, Vector2.zero, 0f);

            fist = CreateFist(handPosition, hand);

            body.AddComponent<PlayerBodyProxy>().head = head.transform;

            return body.AddComponent<PlayerLatchTarget>();
        }

        /// <summary>Builds an enemy that can report how fast it is being carried.</summary>
        /// <remarks>
        /// Assembled switched off and activated at the end, so every <c>Awake</c> runs with the
        /// whole object already built and the enemy can find the tracker next to it.
        /// </remarks>
        HordeEnemy CreateCarriedEnemy(Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.SetActive(false);
            go.name = "Test Enemy";
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.5f;
            m_Created.Add(go);

            var body = go.AddComponent<Rigidbody>();
            body.mass = HordePocLayout.k_DummyMass;
            body.useGravity = false;

            go.AddComponent<PointVelocityTracker>();
            var enemy = go.AddComponent<HordeEnemy>();

            go.SetActive(true);

            return enemy;
        }

        /// <summary>
        /// Puts <paramref name="enemy"/> on a specific anchor rather than whichever one the selector
        /// would pick, so a test can say exactly where the creature is holding on.
        /// </summary>
        /// <remarks>
        /// Goes through the same two calls the game does — claim the anchor, then attach — rather
        /// than reaching into the enemy, so these tests still exercise the real path.
        /// </remarks>
        static bool LatchOnto(PlayerLatchTarget player, HordeEnemy enemy, LatchAnchor anchor) =>
            anchor.TryOccupy(enemy) && player.CompleteLatch(enemy, anchor);

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

        /// <summary>Moves <paramref name="mover"/> a frame at a time for a fixed span of seconds.</summary>
        static IEnumerator Travel(Transform mover, Vector3 direction, float speed, float seconds)
        {
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
            {
                mover.position += direction * (speed * Time.unscaledDeltaTime);
                yield return null;
            }
        }

        /// <summary>
        /// Swings <paramref name="mover"/> around <paramref name="pivot"/>, the way a forearm swings
        /// from an elbow.
        /// </summary>
        /// <remarks>
        /// A rotation rather than a straight line, because that is the motion in which a fist and the
        /// anchor behind it genuinely move at different speeds: they sit at different radii, so the
        /// gap between them grows with how hard the punch is thrown. Sliding the arm sideways moves
        /// both at exactly the same velocity, which the relative-velocity subtraction cancels on its
        /// own — a test built on that motion proves nothing about the arm exclusion.
        /// </remarks>
        static IEnumerator SwingAround(Transform mover, Vector3 pivot, float degreesPerSecond, float seconds)
        {
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
            {
                mover.RotateAround(pivot, Vector3.up, degreesPerSecond * Time.unscaledDeltaTime);
                yield return null;
            }
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

        /// <summary>
        /// A creature holding your waist rides along with you, so the hand and the creature share
        /// every step you take. Measured in absolute terms that is a punch thrown at walking speed,
        /// several times a second, for as long as it holds on — the player would beat every enemy
        /// off simply by crossing the arena.
        /// </summary>
        [UnityTest]
        public IEnumerator Walking_WithACreatureHoldingOn_ThrowsNoPunches()
        {
            var rig = CreateRig(new Vector3(0.2f, 1f, 0.1f), out _, out _);
            var enemy = CreateCarriedEnemy(new Vector3(0.2f, 1f, 0.1f));
            yield return null;

            Assume.That(LatchOnto(rig, enemy, AnchorNamed(rig, "Chest Right")), Is.True);
            yield return null;

            // The whole player walks: the hand, the fist and the creature all move together.
            yield return Travel(rig.transform.root, Vector3.forward, k_StandardPunchSpeed, 1f);

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth),
                $"Walking across the room took the creature from {enemy.maxHealth} to {enemy.health}.");
        }

        /// <summary>
        /// The other half of the same rule: subtracting the ride must not stop a real punch from
        /// landing on a creature that is holding on. Beating them off is the whole defence.
        /// </summary>
        [UnityTest]
        public IEnumerator Punching_ACreatureHoldingOn_StillLands()
        {
            var start = new Vector3(0.2f, 1f, -0.9f);
            var rig = CreateRig(start, out var hand, out _);
            var enemy = CreateCarriedEnemy(new Vector3(0.2f, 1f, 0.1f));
            yield return null;

            Assume.That(LatchOnto(rig, enemy, AnchorNamed(rig, "Chest Right")), Is.True);
            yield return null;

            // Only the hand moves this time, so the fist genuinely closes on the creature.
            yield return Travel(hand, Vector3.forward, k_StandardPunchSpeed, 0.8f);

            Assert.That(enemy.health, Is.LessThan(enemy.maxHealth),
                "A punch thrown at a creature that had hold of the player did not register, so " +
                "there is no way to get it off.");
        }

        /// <summary>
        /// A creature on your forearm sits permanently inside that hand's punch trigger. Without an
        /// explicit exclusion, throwing a punch with that arm beats it off — the player would never
        /// need the other hand, and would never understand why.
        /// </summary>
        /// <remarks>
        /// The swing is a rotation about an elbow, which is the only motion that actually puts this
        /// to the test. The fist and the arm anchor sit at different radii, so a hard punch moves
        /// them at speeds that differ by more than the punch threshold, and the relative-velocity
        /// subtraction no longer cancels the two out. That is exactly the case the exclusion exists
        /// for, and the reason the cooldown cannot stand in for it.
        /// </remarks>
        [UnityTest]
        public IEnumerator PunchingWithAnArm_ThatHasACreatureOnIt_ThrowsNoPunchesAtThatCreature()
        {
            const float elbowToFist = 0.3f;

            var elbow = new Vector3(0f, 1f, 0f);
            var rig = CreateRig(elbow + Vector3.forward * elbowToFist, out var hand, out var fist);
            var enemy = CreateCarriedEnemy(hand.position);
            yield return null;

            Assume.That(LatchOnto(rig, enemy, AnchorNamed(rig, HordePocLayout.k_ArmAnchorName)), Is.True);
            yield return null;

            // Fast enough that the fist itself is travelling at real punching speed.
            yield return SwingAround(hand, elbow, 1000f, 0.4f);

            Assume.That(fist.GetComponent<PointVelocityTracker>().speed,
                Is.GreaterThan(new PunchSettings().minSpeed),
                "The swing was too slow to have been a punch at all, so nothing was proved.");

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth),
                $"Throwing a punch beat the creature holding that same arm from {enemy.maxHealth} " +
                $"down to {enemy.health}.");
            Assert.That(enemy.isLatched, Is.True);
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
