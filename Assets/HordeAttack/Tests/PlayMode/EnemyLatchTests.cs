using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Exercises an enemy taking hold of a player and every way that hold can end.
    /// </summary>
    /// <remarks>
    /// PlayMode, because all of it depends on things edit mode does not do: <c>Awake</c> running so
    /// the enemy knows its own health and where it came from, the transform hierarchy actually
    /// carrying a parented object along, and the physics engine turning an impulse into velocity.
    /// </remarks>
    public class EnemyLatchTests
    {
        /// <summary>Standing eye height the test player is built at.</summary>
        const float k_EyeHeight = 1.7f;

        readonly List<GameObject> m_Created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate rather than Destroy: PlayerLatchTarget keeps a static registry that
            // FindNearest reads, and deferred destruction would leave a test's player in it long
            // enough for the next test to walk toward it.
            foreach (var go in m_Created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            m_Created.Clear();
        }

        /// <summary>
        /// Builds a player with a head, a body and the anchors from the real layout.
        /// </summary>
        /// <remarks>
        /// The order is not cosmetic: <see cref="PlayerBodyProxy"/> and
        /// <see cref="PlayerLatchTarget"/> both collect anchors in <c>Awake</c>, which in play mode
        /// runs the instant the component is attached. Anchors added afterwards are invisible to
        /// both of them.
        /// </remarks>
        PlayerLatchTarget CreatePlayer(Vector3 position, bool withFeedback = false)
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

            var target = body.AddComponent<PlayerLatchTarget>();

            if (withFeedback)
                body.AddComponent<LatchFeedback>();

            return target;
        }

        /// <summary>
        /// Builds an enemy with no gravity, so that anything it is seen to do afterwards came from a
        /// punch rather than from falling.
        /// </summary>
        /// <remarks>
        /// Assembled switched off and activated at the end, so every <c>Awake</c> runs with the
        /// whole object already built — the same order a scene loaded from disk gives, and the only
        /// way a component can find the ones next to it.
        /// </remarks>
        HordeEnemy CreateEnemy(Vector3 position, Transform home = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.SetActive(false);
            go.name = "Test Enemy";
            m_Created.Add(go);

            // Parented before HordeEnemy is attached, because the enemy remembers in Awake where it
            // belongs so it can go back there after being punched off a player.
            if (home != null)
                go.transform.SetParent(home, false);

            go.transform.position = position;
            go.transform.localScale = Vector3.one * HordePocLayout.k_DummyScale;

            var body = go.AddComponent<Rigidbody>();
            body.mass = HordePocLayout.k_DummyMass;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            go.AddComponent<PointVelocityTracker>();
            var enemy = go.AddComponent<HordeEnemy>();

            go.SetActive(true);

            return enemy;
        }

        static Vector3 Swing(float speed) => Vector3.forward * speed;

        /// <summary>
        /// Waits until an impulse handed to the physics engine has been simulated. One
        /// <c>WaitForFixedUpdate</c> resumes before the step that turns forces into velocity.
        /// </summary>
        static IEnumerator SimulatePhysics()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        static LatchAnchor AnchorNamed(PlayerLatchTarget player, string name)
        {
            foreach (var anchor in player.anchors)
            {
                if (anchor.name == name)
                    return anchor;
            }

            Assert.Fail($"The test player has no '{name}' anchor.");

            return null;
        }

        [UnityTest]
        public IEnumerator Latch_TakesHoldOfThePlayer()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            Assert.That(player.TryLatch(enemy, LatchStyle.Clinger), Is.True, "Nothing was free to grab.");
            yield return null;

            Assert.That(enemy.isLatched, Is.True);
            Assert.That(enemy.state, Is.EqualTo(EnemyState.Latched));
            Assert.That(enemy.latchAnchor.occupant, Is.EqualTo(enemy));
            Assert.That(enemy.transform.IsChildOf(player.transform), Is.True,
                "The enemy is not parented to the player, so it would be left behind the moment " +
                "they walked away.");
            Assert.That(enemy.GetComponent<Rigidbody>().isKinematic, Is.True,
                "A dynamic body parented to a moving player fights its own transform.");
        }

        [UnityTest]
        public IEnumerator Latch_HangsTheEnemyBelowTheAnchorItGrabbed()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Leaper);
            yield return null;

            var anchor = enemy.latchAnchor;
            float drop = anchor.transform.position.y - enemy.transform.position.y;

            Assert.That(drop, Is.EqualTo(anchor.hangDrop).Within(1e-3f),
                $"The enemy sits {drop:F2} m below '{anchor.name}' instead of {anchor.hangDrop:F2} m.");
        }

        /// <summary>
        /// A creature holding on to you has its back to the world and its face to you. Facing the
        /// other way loses the one thing that makes being grabbed read as being grabbed.
        /// </summary>
        [UnityTest]
        public IEnumerator Latch_TurnsTheEnemyToFaceThePlayer()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 2f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            yield return null;

            var toPlayer = player.headPosition - enemy.transform.position;
            toPlayer.y = 0f;

            Assert.That(Vector3.Dot(enemy.transform.forward, toPlayer.normalized), Is.GreaterThan(0.5f),
                "The enemy latched on with its back to the player.");
        }

        [UnityTest]
        public IEnumerator Latch_KeepsTheEnemyRidingAlongWhenThePlayerMoves()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            yield return null;

            var before = enemy.transform.position;
            player.transform.root.position += new Vector3(3f, 0f, 0f);
            yield return null;

            Assert.That(enemy.transform.position.x - before.x, Is.EqualTo(3f).Within(1e-2f),
                "The player walked off and left the creature that was holding them behind.");
        }

        [UnityTest]
        public IEnumerator Latch_RefusesToPutTwoEnemiesOnTheSameSpot()
        {
            var player = CreatePlayer(Vector3.zero);
            var first = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            var second = CreateEnemy(new Vector3(0.1f, 0.5f, 1f));
            yield return null;

            player.TryLatch(first, LatchStyle.Clinger);
            player.TryLatch(second, LatchStyle.Clinger);
            yield return null;

            Assert.That(first.isLatched, Is.True);
            Assert.That(second.isLatched, Is.True);
            Assert.That(second.latchAnchor, Is.Not.EqualTo(first.latchAnchor),
                "Both creatures grabbed the same point and would render as one flickering lump.");
        }

        [UnityTest]
        public IEnumerator Latch_RunsOutOfPlacesToGrabRatherThanDoubleBooking()
        {
            var player = CreatePlayer(Vector3.zero);
            int capacity = player.anchors.Count;
            var latched = new List<HordeEnemy>();

            for (int i = 0; i <= capacity; i++)
                latched.Add(CreateEnemy(new Vector3(i * 0.1f, 0.5f, 1f)));

            yield return null;

            int taken = 0;
            foreach (var enemy in latched)
            {
                if (player.TryLatch(enemy, LatchStyle.Clinger))
                    taken++;
            }

            Assert.That(taken, Is.EqualTo(capacity),
                $"{taken} enemies took hold of a player with {capacity} anchors.");
            Assert.That(player.latchedCount, Is.EqualTo(capacity));
        }

        [UnityTest]
        public IEnumerator Latch_TellsThePlayerItHappened()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            HordeEnemy reported = null;
            player.OnEnemyLatched += (e, _) => reported = e;
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            yield return null;

            Assert.That(reported, Is.EqualTo(enemy),
                "Nothing was told that a creature took hold, so the player gets no haptics and no flash.");
        }

        /// <summary>
        /// The regression that matters most in this phase. A latched enemy is kinematic, and
        /// <c>AddForce</c> on a kinematic body is discarded without a word — so a punch would take
        /// health off a creature that stayed exactly where it was, which reads as the hit not
        /// registering at all.
        /// </summary>
        [UnityTest]
        public IEnumerator Punch_KnocksALatchedEnemyOffAndThrowsIt()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            yield return null;

            var outcome = enemy.ReceivePunch(Swing(4f), new PunchSettings());
            yield return SimulatePhysics();

            Assert.That(outcome.landed, Is.True);
            Assert.That(enemy.isLatched, Is.False, "The punch landed and the creature kept holding on.");

            var body = enemy.GetComponent<Rigidbody>();
            Assert.That(body.isKinematic, Is.False,
                "The enemy was let go but left kinematic, so nothing can ever move it again.");
            Assert.That(body.linearVelocity.magnitude, Is.GreaterThan(1f),
                $"The enemy came off at {body.linearVelocity.magnitude:F2} m/s — the impulse was " +
                "swallowed by the kinematic body it still had when the punch was applied.");
        }

        [UnityTest]
        public IEnumerator Punch_FreesTheAnchorForTheNextCreature()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            var anchor = enemy.latchAnchor;

            enemy.ReceivePunch(Swing(4f), new PunchSettings());
            yield return null;

            Assert.That(anchor.isFree, Is.True,
                "The anchor is still marked as taken, so nothing can ever grab that spot again.");
            Assert.That(player.latchedCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Punch_PutsTheEnemyBackUnderItsOriginalParent()
        {
            var home = new GameObject("Dummies");
            m_Created.Add(home);

            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f), home.transform);
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            enemy.ReceivePunch(Swing(4f), new PunchSettings());
            yield return null;

            Assert.That(enemy.transform.parent, Is.EqualTo(home.transform),
                "A creature punched off the player stayed in the player's hierarchy.");
        }

        [UnityTest]
        public IEnumerator Death_WhileLatched_LetsGoOfThePlayer()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            var settings = new PunchSettings();
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            var anchor = enemy.latchAnchor;

            for (int i = 0; i < 20 && enemy.isAlive; i++)
            {
                enemy.ReceivePunch(Swing(settings.maxSpeed), settings);
                player.TryLatch(enemy, LatchStyle.Clinger);
            }

            Assume.That(enemy.isAlive, Is.False, "The enemy survived twenty full power punches.");
            yield return null;

            Assert.That(enemy.isLatched, Is.False, "A corpse is riding on the player.");
            Assert.That(anchor.isFree, Is.True, "A dead creature is still holding an anchor hostage.");
            Assert.That(enemy.transform.IsChildOf(player.transform), Is.False);
        }

        [UnityTest]
        public IEnumerator Respawn_WhileLatched_LetsGoAndGoesHome()
        {
            var player = CreatePlayer(Vector3.zero);
            var spawn = new Vector3(2f, 0.5f, 3f);
            var enemy = CreateEnemy(spawn);
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            var anchor = enemy.latchAnchor;
            Assume.That(enemy.isLatched, Is.True);

            enemy.Respawn();
            yield return null;

            Assert.That(enemy.isLatched, Is.False);
            Assert.That(anchor.isFree, Is.True,
                "Recycling a creature left the anchor it was on reserved forever.");
            Assert.That(Vector3.Distance(enemy.transform.position, spawn), Is.LessThan(1e-2f),
                "The respawn put the enemy somewhere other than where it started, because the " +
                "position was set while it was still parented to the player.");
        }

        [UnityTest]
        public IEnumerator Disable_FreesTheAnchorEvenThoughNothingCalledDetach()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            var anchor = enemy.latchAnchor;

            enemy.gameObject.SetActive(false);
            yield return null;

            Assert.That(anchor.isFree, Is.True,
                "An anchor held by a switched-off creature is a spot nobody can ever use again.");
        }

        [UnityTest]
        public IEnumerator Detach_OnlyGivesBackTheAnchorToWhoeverActuallyHadIt()
        {
            var player = CreatePlayer(Vector3.zero);
            var first = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            var second = CreateEnemy(new Vector3(0.1f, 0.5f, 1f));
            yield return null;

            var anchor = AnchorNamed(player, HordePocLayout.k_BodyAnchors[0].name);

            Assume.That(anchor.TryOccupy(first), Is.True);
            anchor.Release(second);

            Assert.That(anchor.occupant, Is.EqualTo(first),
                "One creature let go and freed a spot that a different one was holding.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator CarrierVelocity_IsNothingWhileTheEnemyIsFree()
        {
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            Assert.That(enemy.carrierVelocity, Is.EqualTo(Vector3.zero),
                "A free enemy reporting a carrier velocity would have it subtracted from every " +
                "punch thrown at it, and hard punches would stop registering.");
        }

        /// <summary>
        /// What the relative-velocity punch model is built on: a creature riding on a walking player
        /// is genuinely moving through the world, and that motion has to be visible to the fists so
        /// they can subtract it.
        /// </summary>
        [UnityTest]
        public IEnumerator CarrierVelocity_ReportsHowFastThePlayerIsCarryingTheEnemy()
        {
            var player = CreatePlayer(Vector3.zero);
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            yield return null;

            const float speed = 2f;
            var root = player.transform.root;

            for (int frame = 0; frame < 20; frame++)
            {
                root.position += Vector3.right * (speed * Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.That(enemy.carrierVelocity.x, Is.EqualTo(speed).Within(0.5f),
                $"The enemy reports being carried at {enemy.carrierVelocity.x:F2} m/s while the " +
                $"player walks at {speed:F2} m/s.");
        }

        /// <summary>
        /// The vignette has to be invisible until something happens. A quad sitting 20 cm from the
        /// player's eyes with any tint at all would recolour the whole game.
        /// </summary>
        [UnityTest]
        public IEnumerator Feedback_ShowsNothingUntilSomethingTakesHold()
        {
            var player = CreatePlayer(Vector3.zero, withFeedback: true);
            var feedback = player.GetComponent<LatchFeedback>();
            yield return null;

            Assert.That(feedback.vignette, Is.Not.Null,
                "No vignette was built, so being grabbed shows nothing at all.");
            Assert.That(feedback.flashAlpha, Is.EqualTo(0f).Within(1e-3f),
                $"The vignette starts at {feedback.flashAlpha:F2} opacity, tinting the whole game red.");
        }

        [UnityTest]
        public IEnumerator Feedback_FlashesWhenACreatureTakesHold()
        {
            var player = CreatePlayer(Vector3.zero, withFeedback: true);
            var feedback = player.GetComponent<LatchFeedback>();
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            yield return null;

            Assert.That(feedback.flashAlpha, Is.GreaterThan(0f),
                "A creature took hold and the player was shown nothing.");
        }

        [UnityTest]
        public IEnumerator Feedback_FadesBackToNothing()
        {
            var player = CreatePlayer(Vector3.zero, withFeedback: true);
            var feedback = player.GetComponent<LatchFeedback>();
            var enemy = CreateEnemy(new Vector3(0f, 0.5f, 1f));
            yield return null;

            player.TryLatch(enemy, LatchStyle.Clinger);
            yield return new WaitForSeconds(1.5f);

            Assert.That(feedback.flashAlpha, Is.EqualTo(0f).Within(1e-3f),
                "The vignette never faded, so the player is left looking through red for the rest " +
                "of the session.");
        }

        /// <summary>
        /// The quad hangs in front of the camera and would be the first thing every punch trigger
        /// and every arriving creature ran into.
        /// </summary>
        [UnityTest]
        public IEnumerator Feedback_LeavesNoColliderInFrontOfTheEyes()
        {
            var player = CreatePlayer(Vector3.zero, withFeedback: true);
            var feedback = player.GetComponent<LatchFeedback>();
            yield return null;

            Assert.That(feedback.vignette.GetComponent<Collider>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator FindNearest_PicksThePlayerTheEnemyIsClosestTo()
        {
            var near = CreatePlayer(new Vector3(1f, 0f, 0f));
            CreatePlayer(new Vector3(20f, 0f, 0f));
            yield return null;

            Assert.That(PlayerLatchTarget.FindNearest(Vector3.zero), Is.EqualTo(near));
        }
    }
}
