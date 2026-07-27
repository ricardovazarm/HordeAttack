using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Exercises taking an enemy in the grip: prising one off the player, carrying it, and letting
    /// go of it hard enough to matter.
    /// </summary>
    /// <remarks>
    /// The grab runs through the real interaction toolkit — a real
    /// <see cref="XRInteractionManager"/>, a real <see cref="XRDirectInteractor"/> — because almost
    /// everything that can go wrong here is the toolkit's bookkeeping rather than this project's
    /// code: which parent it will restore, whether it remembers the body as kinematic, whether the
    /// throw velocity survives. A stand-in interactor would prove none of it.
    /// <para>
    /// Selection is driven with <c>StartManualInteraction</c>, which is the toolkit's own way of
    /// saying "act as if the grip were held down". It leaves every other part of the selection path
    /// exactly as it is in the headset; only the button is simulated.
    /// </para>
    /// </remarks>
    public class EnemyGrabTests
    {
        /// <summary>Standing eye height the test player is built at.</summary>
        const float k_EyeHeight = 1.7f;

        /// <summary>Speed a hand is dragged at when the test wants a real throw, in m/s.</summary>
        const float k_ThrowSpeed = 4f;

        /// <summary>How far a hand starts from what it is reaching for, in meters.</summary>
        const float k_Reach = 1f;

        /// <summary>
        /// Speed a hand travels at when it is reaching for something, in m/s.
        /// </summary>
        /// <remarks>
        /// Comfortably above <see cref="PunchSettings.minSpeed"/>, which is the point: an ordinary
        /// reach in VR clears the punch threshold without the player intending anything of the sort,
        /// and that is the whole reason the grip has to arbitrate.
        /// </remarks>
        const float k_ReachSpeed = 3f;

        /// <summary>
        /// Length of a frame while these tests run, in seconds. Roughly a Quest's.
        /// </summary>
        /// <remarks>
        /// The clock is pinned because the toolkit's throw smoothing declines to measure anything on
        /// a frame shorter than a millisecond — a guard against a frame-timing bug on Quest. A
        /// headless batch run has nothing to draw and turns frames over in about 0.2 ms, so every
        /// sample would be thrown away and every throw would come out at zero: the test would fail
        /// on an artefact of having no screen rather than on anything the game does.
        /// <para>
        /// <c>Time.captureDeltaTime</c> makes each frame advance the clock by exactly this much
        /// regardless of how fast the machine really is, which also makes these tests repeatable
        /// instead of dependent on the load of whatever else is running.
        /// </para>
        /// </remarks>
        const float k_FrameTime = 1f / 72f;

        readonly List<GameObject> m_Created = new List<GameObject>();

        XRInteractionManager m_Manager;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = k_FrameTime;

            var go = new GameObject("Test Interaction Manager");
            m_Created.Add(go);
            m_Manager = go.AddComponent<XRInteractionManager>();
        }

        [TearDown]
        public void TearDown()
        {
            // Restored first, and unconditionally: leaving the clock pinned would follow the run into
            // every test after this one.
            Time.captureDeltaTime = 0f;

            // DestroyImmediate rather than Destroy, for the same reason the latch tests use it: the
            // static registries in PlayerLatchTarget and HordeEnemy would otherwise hold a test's
            // objects long enough for the next one to find them.
            foreach (var go in m_Created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            m_Created.Clear();
        }

        /// <summary>
        /// Builds a hand that can take hold of things, optionally hanging off an existing arm.
        /// </summary>
        /// <remarks>
        /// The grip is wired to the toolkit's own manual input source, which is what it is there for:
        /// the reader answers whatever the test last queued, and everything downstream of it — the
        /// logical select state the fist reads, the interactor's decision to select — is the real
        /// thing running on real input. A rig in the headset drives the same reader from the grip
        /// button instead.
        /// <para>
        /// The manual path is available here precisely because a test hand has no controller
        /// component above it: with one, the toolkit takes its older input route and ignores the
        /// reader. That is the route the template's rig actually uses, which is why the fist reads
        /// the logical state — the one place both routes agree — rather than the reader.
        /// </para>
        /// </remarks>
        HandRig CreateHand(Vector3 position, Transform arm = null)
        {
            var go = new GameObject("Test Hand Interactor");
            go.SetActive(false);
            m_Created.Add(go);

            if (arm != null)
                go.transform.SetParent(arm, false);

            go.transform.position = position;

            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.1f;

            var interactor = go.AddComponent<XRDirectInteractor>();
            interactor.interactionManager = m_Manager;
            interactor.selectActionTrigger = HordePocLayout.k_GripTrigger;
            interactor.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;

            go.SetActive(true);

            return new HandRig(interactor);
        }

        /// <summary>
        /// Builds an enemy that can be grabbed, configured the way the scene builder configures the
        /// dummies.
        /// </summary>
        /// <remarks>
        /// Assembled switched off and activated at the end. It is the only order in which every
        /// <c>Awake</c> sees the finished object, and here it matters in both directions:
        /// <see cref="HordeEnemy"/> looks for the interactable next to it and
        /// <see cref="EnemyGrabInteractable"/> looks for the enemy, so no sequence of
        /// <c>AddComponent</c> calls on a live object can satisfy both.
        /// <para>
        /// Gravity is off so that anything the creature is later seen to do came from the grab or
        /// the throw rather than from falling.
        /// </para>
        /// </remarks>
        HordeEnemy CreateEnemy(Vector3 position, bool withLocomotion = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.SetActive(false);
            go.name = "Test Enemy";
            m_Created.Add(go);

            go.transform.position = position;
            go.transform.localScale = Vector3.one * HordePocLayout.k_DummyScale;

            var body = go.AddComponent<Rigidbody>();
            body.mass = HordePocLayout.k_DummyMass;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grab = go.AddComponent<EnemyGrabInteractable>();
            grab.interactionManager = m_Manager;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = true;

            go.AddComponent<PointVelocityTracker>();
            go.AddComponent<ImpactDetector>();

            // Before the locomotion, which declares [RequireComponent(typeof(HordeEnemy))]. Added
            // the other way round, Unity satisfies that requirement by creating a HordeEnemy of its
            // own, and the explicit AddComponent that follows is refused outright — HordeEnemy is
            // [DisallowMultipleComponent] — so this method would hand the test back a null.
            var enemy = go.AddComponent<HordeEnemy>();

            if (withLocomotion)
                go.AddComponent<EnemyLocomotion>().style = LatchStyle.Clinger;

            go.SetActive(true);

            return enemy;
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

        /// <summary>Puts <paramref name="enemy"/> on a named anchor through the real latch path.</summary>
        static bool LatchOnto(PlayerLatchTarget player, HordeEnemy enemy, LatchAnchor anchor) =>
            anchor.TryOccupy(enemy) && player.CompleteLatch(enemy, anchor);

        /// <summary>Moves <paramref name="mover"/> a frame at a time for a fixed span of seconds.</summary>
        /// <remarks>
        /// Stepped on <c>Time.deltaTime</c> rather than the unscaled clock, because that is the one
        /// the toolkit divides by when it works out how fast a released object was travelling. With
        /// the frame length pinned by <see cref="k_FrameTime"/> the two clocks no longer agree, and
        /// moving the hand on the real one would have it cover a fifty-fifth of the distance the
        /// toolkit then credits to a full frame — a throw at a twentieth of the intended speed.
        /// </remarks>
        static IEnumerator Travel(Transform mover, Vector3 direction, float speed, float seconds)
        {
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.deltaTime)
            {
                mover.position += direction * (speed * Time.deltaTime);
                yield return null;
            }
        }

        /// <summary>Runs the physics engine for <paramref name="steps"/> fixed updates.</summary>
        static IEnumerator Simulate(int steps)
        {
            for (int i = 0; i < steps; i++)
                yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator Grab_TakesHoldOfALooseEnemy()
        {
            var hand = CreateHand(Vector3.zero);
            var enemy = CreateEnemy(Vector3.zero);
            yield return null;

            hand.Grab(enemy);
            yield return null;

            Assert.That(enemy.isHeld, Is.True, "The grip closed on the enemy and nothing happened.");
            Assert.That(enemy.state, Is.EqualTo(EnemyState.Grabbed));
            Assert.That(enemy.holder, Is.EqualTo(hand.transform),
                "The enemy does not know which hand has it, so no fist can tell to leave it alone.");
        }

        /// <summary>
        /// A carried enemy has to follow the hand, or "se queda colgando de tu mano" is not what the
        /// player sees.
        /// </summary>
        [UnityTest]
        public IEnumerator Grab_CarriesTheEnemyAlongWithTheHand()
        {
            var hand = CreateHand(Vector3.zero);
            var enemy = CreateEnemy(Vector3.zero);
            yield return null;

            hand.Grab(enemy);
            yield return null;

            var start = enemy.transform.position;
            yield return Travel(hand.transform, Vector3.right, 1f, 0.5f);
            yield return Simulate(2);

            float carried = Vector3.Distance(enemy.transform.position, start);

            Assert.That(carried, Is.GreaterThan(0.2f),
                $"The hand moved half a meter and the creature came {carried:0.00} m with it.");
        }

        /// <summary>
        /// The headline of the phase: an enemy that has hold of you comes off when you grab it.
        /// </summary>
        [UnityTest]
        public IEnumerator Grab_TearsALatchedEnemyOffThePlayer()
        {
            var player = CreatePlayer(Vector3.zero);
            var anchor = AnchorNamed(player, "Chest Right");
            var enemy = CreateEnemy(anchor.transform.position);
            var hand = CreateHand(anchor.transform.position);
            yield return null;

            Assume.That(LatchOnto(player, enemy, anchor), Is.True);
            yield return null;

            hand.Grab(enemy);
            yield return null;

            Assert.That(enemy.isLatched, Is.False, "The creature is being carried and still holding on.");
            Assert.That(anchor.isFree, Is.True,
                "The anchor is still marked as taken, so nothing else can ever use that spot.");
            Assert.That(enemy.state, Is.EqualTo(EnemyState.Grabbed));
        }

        /// <summary>
        /// A creature that was clinging to the player is kinematic and parented to a latch anchor.
        /// The toolkit records both of those the instant it takes over, and puts them back when the
        /// hand lets go — so an enemy that was not freed <em>first</em> is thrown by being hung back
        /// on the player's chest, frozen in mid-air.
        /// </summary>
        [UnityTest]
        public IEnumerator Release_OfATornOffEnemy_DoesNotHangItBackOnThePlayer()
        {
            var player = CreatePlayer(Vector3.zero);
            var anchor = AnchorNamed(player, "Chest Right");
            var home = new GameObject("Dummies");
            m_Created.Add(home);

            var enemy = CreateEnemy(anchor.transform.position);
            enemy.transform.SetParent(home.transform, true);

            var hand = CreateHand(anchor.transform.position);
            yield return null;

            Assume.That(LatchOnto(player, enemy, anchor), Is.True);
            yield return null;

            hand.Grab(enemy);
            yield return null;

            hand.Release();
            yield return null;
            yield return Simulate(2);

            Assert.That(enemy.transform.IsChildOf(anchor.transform), Is.False,
                "Letting go put the creature back on the player's chest.");
            Assert.That(enemy.isLatched, Is.False);
            Assert.That(enemy.GetComponent<Rigidbody>().isKinematic, Is.False,
                "The creature was dropped frozen: the toolkit put back the kinematic flag it had " +
                "while it was clinging to the player, so the throw goes nowhere.");
        }

        /// <summary>
        /// Throwing is the whole point of holding one. The velocity has to survive both the toolkit's
        /// end-of-frame detach and the creature's own locomotion, which would otherwise overwrite it
        /// on the very next physics step and leave the throw dead on the spot.
        /// </summary>
        [UnityTest]
        public IEnumerator Release_ThrowsTheEnemyAtTheSpeedOfTheHand()
        {
            CreatePlayer(new Vector3(0f, 0f, -6f));

            var hand = CreateHand(Vector3.zero);
            var enemy = CreateEnemy(Vector3.zero, withLocomotion: true);
            yield return null;

            hand.Grab(enemy);
            yield return null;

            yield return Travel(hand.transform, Vector3.right, k_ThrowSpeed, 0.4f);

            hand.Release();
            yield return null;
            yield return Simulate(2);

            var thrown = enemy.GetComponent<Rigidbody>().linearVelocity;

            Assert.That(thrown.magnitude, Is.GreaterThan(1f),
                $"The hand was moving at {k_ThrowSpeed} m/s and the creature left it at " +
                $"{thrown.magnitude:0.00} m/s, which is a drop rather than a throw.");
            Assert.That(Vector3.Dot(thrown.normalized, Vector3.right), Is.GreaterThan(0.5f),
                $"The creature was thrown toward {thrown.normalized} rather than the way the hand " +
                "was going.");
        }

        /// <summary>
        /// A creature in the player's fist must not climb back onto them, and must not walk while it
        /// is being carried — its own locomotion writes a velocity every physics step and would fight
        /// the hand for the body.
        /// </summary>
        [UnityTest]
        public IEnumerator Held_EnemyNeitherWalksNorTakesHoldOfYou()
        {
            var player = CreatePlayer(Vector3.zero);

            // Well inside latch range: without standing down, it would take hold immediately.
            var enemy = CreateEnemy(new Vector3(0.3f, 1f, 0.3f), withLocomotion: true);
            var hand = CreateHand(new Vector3(0.3f, 1f, 0.3f));
            yield return null;

            hand.Grab(enemy);
            yield return Simulate(20);

            Assert.That(enemy.isLatched, Is.False,
                "The creature climbed onto the player out of the hand that was holding it.");
            Assert.That(player.latchedCount, Is.Zero);
            Assert.That(enemy.isHeld, Is.True, "The hand lost the creature partway through.");
            Assert.That(enemy.state, Is.EqualTo(EnemyState.Grabbed));
        }

        /// <summary>
        /// The punch trigger and the grip share a hand, so a carried creature sits permanently inside
        /// the trigger of the fist carrying it. Without an exclusion the player would beat to death
        /// whatever they picked up simply by moving their arm, and would never see why.
        /// </summary>
        [UnityTest]
        public IEnumerator PunchingWithTheHandThatIsHoldingAnEnemy_ThrowsNoPunchesAtIt()
        {
            var arm = new GameObject("Test Hand");
            m_Created.Add(arm);
            arm.transform.position = new Vector3(0f, 1f, 0f);

            var hand = CreateHand(arm.transform.position, arm.transform);
            var fist = CreateFist(arm.transform.position, arm.transform);
            var enemy = CreateEnemy(arm.transform.position);
            yield return null;

            hand.Grab(enemy);
            yield return null;

            yield return Travel(arm.transform, Vector3.forward, 5f, 0.5f);

            Assume.That(fist.GetComponent<PointVelocityTracker>().speed,
                Is.GreaterThan(new PunchSettings().minSpeed),
                "The arm never moved fast enough to have thrown a punch, so nothing was proved.");

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth),
                $"Carrying the creature about beat it from {enemy.maxHealth} down to {enemy.health}.");
            Assert.That(enemy.isHeld, Is.True);
        }

        /// <summary>
        /// A corpse left in the player's hand is velocity-tracked back to it the moment it respawns
        /// at its spawn point, so dying has to make the hand let go.
        /// </summary>
        [UnityTest]
        public IEnumerator Dying_WhileHeld_MakesTheHandLetGo()
        {
            var hand = CreateHand(Vector3.zero);
            var enemy = CreateEnemy(Vector3.zero);
            yield return null;

            hand.Grab(enemy);
            yield return null;

            enemy.ReceiveImpact(enemy.maxHealth);
            yield return null;

            Assert.That(enemy.isAlive, Is.False);
            Assert.That(enemy.isHeld, Is.False, "The player is still holding the corpse.");
            Assert.That(hand.interactor.hasSelection, Is.False,
                "The hand still believes it is holding something, so it will not grab anything else.");
        }

        /// <summary>
        /// The bug this whole arbitration exists for, reproduced: in the headset a loose creature
        /// could not be picked up at all, because reaching for it registered as a punch and the
        /// knockback threw it five meters before the fingers had closed. The only creature that
        /// could be grabbed was one already clinging to the player, which is close enough that the
        /// hand barely has to move.
        /// </summary>
        [UnityTest]
        public IEnumerator ReachingForALooseEnemy_WithTheGripHeld_GrabsItInsteadOfPunchingIt()
        {
            var arm = CreateArm(new Vector3(0f, 1f, -k_Reach), out var hand, out var fist);
            var enemy = CreateEnemy(new Vector3(0f, 1f, 0f));
            yield return null;

            // Closed before setting off, the way a player reaches for something they mean to catch.
            hand.SqueezeGrip(true);
            yield return null;

            yield return Travel(arm, Vector3.forward, k_ReachSpeed, k_Reach / k_ReachSpeed);

            Assume.That(fist.GetComponent<PointVelocityTracker>().speed,
                Is.GreaterThan(new PunchSettings().minSpeed),
                "The hand crept up on the enemy below punching speed, so nothing was arbitrated.");

            Assert.That(enemy.health, Is.EqualTo(enemy.maxHealth),
                $"Reaching for the creature punched it down to {enemy.health} on the way in.");
            Assert.That(enemy.isHeld, Is.True,
                "The creature was not punched, but it was not caught either — the hand came away " +
                "with nothing.");
        }

        /// <summary>
        /// The other half of the rule, and the one that stops the fix from turning into "the fist
        /// never lands anything": an open hand still punches.
        /// </summary>
        [UnityTest]
        public IEnumerator ReachingForALooseEnemy_WithTheGripOpen_StillPunchesIt()
        {
            var arm = CreateArm(new Vector3(0f, 1f, -k_Reach), out var hand, out _);
            var enemy = CreateEnemy(new Vector3(0f, 1f, 0f));
            yield return null;

            hand.SqueezeGrip(false);
            yield return null;

            yield return Travel(arm, Vector3.forward, k_ReachSpeed, k_Reach / k_ReachSpeed);

            Assert.That(enemy.health, Is.LessThan(enemy.maxHealth),
                "An open hand driven through a creature at punching speed left it untouched.");
            Assert.That(enemy.isHeld, Is.False);
        }

        /// <summary>
        /// Holding a closed hand still and letting a creature walk into it has to catch it.
        /// </summary>
        /// <remarks>
        /// This is what the grip mode buys. The template ships its hands selecting on
        /// <c>StateChange</c>, where squeezing with nothing in reach counts for one frame and then
        /// stops — so a player who closed their hand early would suppress their own punch and then
        /// catch nothing when the creature arrived, which is worse than either half alone.
        /// </remarks>
        [UnityTest]
        public IEnumerator Grip_HeldBeforeContact_CatchesAnEnemyThatWalksIntoTheHand()
        {
            var hand = CreateHand(new Vector3(0f, 1f, 0f));
            var enemy = CreateEnemy(new Vector3(0f, 1f, -0.6f));
            yield return null;

            hand.SqueezeGrip(true);
            yield return null;

            // Walking pace, straight at a hand that never moves. Nothing here is a punch.
            enemy.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * 1f;
            yield return Simulate(40);

            Assert.That(enemy.isHeld, Is.True,
                "The creature walked into a closed hand and straight out the other side.");
        }

        /// <summary>
        /// Letting go of the grip has to hand the fist back immediately, or a player who throws a
        /// creature and swings at the next one finds their punch missing for no visible reason.
        /// </summary>
        [UnityTest]
        public IEnumerator Grip_ReleasedAfterCarrying_LetsTheHandPunchAgain()
        {
            var arm = CreateArm(new Vector3(0f, 1f, 0f), out var hand, out _);
            var carried = CreateEnemy(new Vector3(0f, 1f, 0f));
            var next = CreateEnemy(new Vector3(0f, 1f, 3f));
            yield return null;

            hand.SqueezeGrip(true);
            yield return null;
            Assume.That(carried.isHeld, Is.True, "The hand never picked the first creature up.");

            hand.SqueezeGrip(false);
            yield return null;
            Assume.That(carried.isHeld, Is.False, "The hand never let go.");

            // Straight on to the next one, with nothing between the release and the swing.
            arm.position = next.transform.position + Vector3.back * k_Reach;
            yield return Travel(arm, Vector3.forward, k_ReachSpeed, k_Reach / k_ReachSpeed);

            Assert.That(next.health, Is.LessThan(next.maxHealth),
                "The hand stayed switched off after the grip was released, so the punch that " +
                "follows a throw lands nothing.");
        }

        /// <summary>
        /// Builds an arm carrying both the things that fight over a hand: the grab interactor and
        /// the fist, as siblings under one transform, exactly as the scene builder arranges them.
        /// </summary>
        Transform CreateArm(Vector3 position, out HandRig hand, out PunchDetector fist)
        {
            var arm = new GameObject("Test Hand");
            m_Created.Add(arm);
            arm.transform.position = position;

            hand = CreateHand(position, arm.transform);
            fist = CreateFist(position, arm.transform);

            return arm.transform;
        }

        /// <summary>
        /// A creature has to be let go of to be a weapon: shoving the one in your fist through the
        /// horde must not hurt anybody.
        /// </summary>
        /// <remarks>
        /// Velocity tracking drives a held body straight through whatever is in its way, at whatever
        /// speed the hand asks for. Counted as impacts, that would let a player mow the horde down by
        /// walking into it with a gnome in their fist, and chip away at the gnome for free while they
        /// did it.
        /// </remarks>
        [UnityTest]
        public IEnumerator Held_EnemyDrivenThroughAnotherOne_HurtsNeither()
        {
            var bystander = CreateEnemy(new Vector3(0f, 1f, 1.5f));
            var weapon = CreateEnemy(new Vector3(0f, 1f, -1.5f));
            var hand = CreateHand(new Vector3(0f, 1f, -1.5f));
            yield return null;

            hand.Grab(weapon);
            yield return null;

            // Straight through where the bystander is standing, fast enough that a real throw at
            // this speed would take a chunk out of both of them.
            yield return Travel(hand.transform, Vector3.forward, 10f, 0.5f);
            yield return Simulate(4);

            Assert.That(bystander.health, Is.EqualTo(bystander.maxHealth),
                $"Carrying a creature through the horde beat a bystander down to {bystander.health}.");
            Assert.That(weapon.health, Is.EqualTo(weapon.maxHealth),
                $"The creature being carried was worn down to {weapon.health} by being shoved about.");
        }

        /// <summary>
        /// Nothing may climb onto the player out of their own fist, whichever route asks for it.
        /// </summary>
        [UnityTest]
        public IEnumerator Held_EnemyRefusesAnAnchorEvenWhenHandedOne()
        {
            var player = CreatePlayer(Vector3.zero);
            var anchor = AnchorNamed(player, "Chest Right");
            var enemy = CreateEnemy(anchor.transform.position);
            var hand = CreateHand(anchor.transform.position);
            yield return null;

            hand.Grab(enemy);
            yield return null;

            Assume.That(anchor.TryOccupy(enemy), Is.True);
            enemy.AttachTo(anchor);

            Assert.That(enemy.isLatched, Is.False,
                "A creature in the player's fist took hold of them anyway.");
            Assert.That(enemy.state, Is.EqualTo(EnemyState.Grabbed));
        }

        /// <summary>
        /// Respawning a creature that is still in someone's hand has to prise it out first. It is
        /// alive again, so nothing else will make the hand let go, and the toolkit would drag it
        /// straight back out of the spawn point it was just returned to.
        /// </summary>
        [UnityTest]
        public IEnumerator Respawn_WhileHeld_MakesTheHandLetGo()
        {
            CreatePlayer(new Vector3(0f, 0f, -6f));

            var spawn = new Vector3(0f, 0f, 5f);
            var enemy = CreateEnemy(spawn, withLocomotion: true);

            // Grabbed where it stands, then carried well away from where it started.
            var hand = CreateHand(spawn);
            yield return null;

            hand.Grab(enemy);
            yield return null;
            yield return Travel(hand.transform, Vector3.back, 4f, 0.4f);

            Assume.That(Vector3.Distance(hand.transform.position, spawn), Is.GreaterThan(1f),
                "The hand never carried the creature anywhere, so nothing was proved.");

            enemy.Respawn();
            yield return null;
            yield return Simulate(4);

            Assert.That(enemy.isHeld, Is.False, "The hand still has the respawned creature.");
            Assert.That(Vector3.Distance(enemy.transform.position, spawn), Is.LessThan(0.5f),
                $"The creature respawned at {spawn} and is at {enemy.transform.position}, back " +
                "toward the hand that never let go of it.");
        }

        /// <summary>Builds a fist wired the way the scene builder wires the ones on the rig.</summary>
        PunchDetector CreateFist(Vector3 position, Transform hand)
        {
            var go = new GameObject("Test Fist");
            m_Created.Add(go);

            // Parented before PunchDetector is attached: the detector remembers in Awake which
            // branch of the hierarchy it hangs from, and that is how it recognises its own arm.
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
        /// A hand under test, with the two verbs these tests need.
        /// </summary>
        /// <remarks>
        /// A thin wrapper rather than a stand-in interactor: every call goes straight to the real
        /// toolkit. <c>StartManualInteraction</c> is the toolkit's supported way of holding the grip
        /// down without an input device, so nothing about the selection path is faked.
        /// </remarks>
        readonly struct HandRig
        {
            public readonly XRDirectInteractor interactor;

            public HandRig(XRDirectInteractor interactor) => this.interactor = interactor;

            public Transform transform => interactor.transform;

            /// <summary>
            /// Squeezes or opens the grip, as the player's finger would.
            /// </summary>
            /// <remarks>
            /// The queued state takes effect on the <em>next</em> frame, which is the toolkit's
            /// design, so callers have to let a frame pass before the squeeze means anything.
            /// </remarks>
            public void SqueezeGrip(bool squeezed) =>
                interactor.selectInput.QueueManualState(squeezed, squeezed ? 1f : 0f);

            public void Grab(HordeEnemy enemy)
            {
                var grab = enemy.GetComponent<EnemyGrabInteractable>();

                // Cast because the toolkit still carries a deprecated overload taking the concrete
                // XRBaseInteractable, and that is the one overload resolution picks otherwise.
                interactor.StartManualInteraction((IXRSelectInteractable)grab);

                Assume.That(grab.isSelected, Is.True,
                    "The toolkit refused the grab, so the test never got as far as its subject.");
            }

            public void Release() => interactor.EndManualInteraction();
        }
    }
}
