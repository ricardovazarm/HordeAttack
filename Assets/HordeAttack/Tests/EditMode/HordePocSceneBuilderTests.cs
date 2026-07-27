using System.Linq;
using HordeAttack.EditorTools;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Builds the POC scene for real and inspects the resulting hierarchy.
    /// </summary>
    /// <remarks>
    /// The hierarchy is built into the active scene and every object it created is destroyed
    /// again in teardown. Creating a dedicated scene would be tidier, but Unity refuses to open
    /// an additive scene while an untitled unsaved one is present, which is exactly the state
    /// batch mode starts in. Building into the active scene works in both batch mode and the
    /// editor, and leaves whatever the developer had open untouched.
    /// </remarks>
    public class HordePocSceneBuilderTests
    {
        const float k_Tolerance = 1e-3f;

        Scene m_Scene;
        GameObject[] m_Created;

        [SetUp]
        public void SetUp()
        {
            m_Scene = EditorSceneManager.GetActiveScene();
            var before = new System.Collections.Generic.HashSet<GameObject>(m_Scene.GetRootGameObjects());

            HordePocSceneBuilder.PopulateScene(m_Scene);

            m_Created = m_Scene.GetRootGameObjects().Where(go => !before.Contains(go)).ToArray();

            // Colliders cache their world bounds and only refresh when the physics engine
            // syncs, which does not happen on its own outside play mode. Without this, every
            // collider still reports the untransformed shape it was created with.
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Created == null)
                return;

            foreach (var go in m_Created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            m_Created = null;
        }

        /// <summary>
        /// Finds a root object among those the builder just created, so a same-named object
        /// that already existed in the developer's scene cannot satisfy an assertion.
        /// </summary>
        GameObject Root(string name) => m_Created.FirstOrDefault(go => go != null && go.name == name);

        [Test]
        public void Build_CreatesGroundWithACollider()
        {
            var ground = Root(HordePocLayout.k_GroundName);

            Assert.That(ground, Is.Not.Null, "The arena has no ground object.");
            Assert.That(ground.GetComponent<Collider>(), Is.Not.Null,
                "The ground needs a collider or the player and enemies fall through it.");
        }

        [Test]
        public void Build_PutsTheGroundSurfaceAtFloorLevel()
        {
            var ground = Root(HordePocLayout.k_GroundName);
            var top = ground.GetComponent<Collider>().bounds.max.y;

            Assert.That(top, Is.EqualTo(0f).Within(k_Tolerance),
                "The walkable surface must sit at y=0; the XR rig is placed at the origin.");
        }

        [Test]
        public void Build_CoversTheFullArenaWithGround()
        {
            var bounds = Root(HordePocLayout.k_GroundName).GetComponent<Collider>().bounds;

            Assert.That(bounds.extents.x, Is.GreaterThanOrEqualTo(HordePocLayout.k_ArenaRadius - k_Tolerance));
            Assert.That(bounds.extents.z, Is.GreaterThanOrEqualTo(HordePocLayout.k_ArenaRadius - k_Tolerance));
        }

        [Test]
        public void Build_AddsADirectionalLight()
        {
            var light = Root(HordePocLayout.k_LightName)?.GetComponent<Light>();

            Assert.That(light, Is.Not.Null, "The scene would render black without a light.");
            Assert.That(light.type, Is.EqualTo(LightType.Directional));
        }

        [Test]
        public void Build_InstantiatesThePlayerRig()
        {
            var rig = Root(HordePocLayout.k_PlayerRigName);

            Assert.That(rig, Is.Not.Null,
                $"Player rig missing. The template prefab at {HordePocSceneBuilder.k_PlayerRigPrefabPath} " +
                "may have moved or been renamed.");
        }

        /// <summary>
        /// Asserts on the <see cref="XROrigin"/> rather than the prefab root, because they are not
        /// the same place: the template nests its XR Origin 12 m behind the root. A root at the
        /// arena center leaves the player standing off the edge of the ground.
        /// </summary>
        [Test]
        public void Build_StandsThePlayerAtTheArenaCenter()
        {
            var origin = PlayerOrigin();

            Assert.That(origin.transform.position, Is.EqualTo(Vector3.zero).Using(Vector3Comparer.Instance),
                "The player does not start at the arena center.");
        }

        [Test]
        public void Build_StandsThePlayerOnTheGround()
        {
            var origin = PlayerOrigin().transform.position;
            var ground = Root(HordePocLayout.k_GroundName).GetComponent<Collider>().bounds;

            Assert.That(ground.Contains(new Vector3(origin.x, ground.center.y, origin.z)), Is.True,
                $"The player starts at {origin}, which is outside the ground plate spanning {ground.min} to {ground.max}.");
        }

        /// <summary>
        /// Measured from the player rather than from the world origin. Those are not the same place
        /// if the rig is ever moved, and the distance that matters is how much warning the player
        /// gets — at 3 m they got about a second, which read as starting the game already grabbed.
        /// </summary>
        [Test]
        public void Build_StartsTheDummiesFarEnoughAwayToBeSeenComing()
        {
            var origin = PlayerOrigin().transform.position;

            foreach (var dummy in Dummies())
            {
                var distance = Vector3.Distance(
                    new Vector3(origin.x, 0f, origin.z),
                    new Vector3(dummy.transform.position.x, 0f, dummy.transform.position.z));

                Assert.That(distance, Is.InRange(
                        HordePocLayout.k_SpawnNearDistance - k_Tolerance,
                        HordePocLayout.k_SpawnFarDistance + k_Tolerance),
                    $"{dummy.name} starts {distance:F2} m from the player, outside the " +
                    $"{HordePocLayout.k_SpawnNearDistance}-{HordePocLayout.k_SpawnFarDistance} m band.");
            }
        }

        [Test]
        public void Build_StartsEveryDummyInFrontOfThePlayer()
        {
            var origin = PlayerOrigin().transform;

            foreach (var dummy in Dummies())
            {
                var toDummy = dummy.transform.position - origin.position;
                toDummy.y = 0f;

                Assert.That(Vector3.Dot(origin.forward, toDummy.normalized), Is.GreaterThan(0f),
                    $"{dummy.name} starts behind or beside the player, who would be grabbed by " +
                    "something they never saw.");
            }
        }

        [Test]
        public void Build_StaggersHowFarAwayTheDummiesStart()
        {
            var origin = PlayerOrigin().transform.position;
            var distances = Dummies()
                .Select(d => Vector2.Distance(
                    new Vector2(origin.x, origin.z),
                    new Vector2(d.transform.position.x, d.transform.position.z)))
                .ToArray();

            for (int i = 0; i < distances.Length; i++)
            {
                for (int j = i + 1; j < distances.Length; j++)
                {
                    Assert.That(Mathf.Abs(distances[i] - distances[j]), Is.GreaterThan(0.1f),
                        $"Two dummies start {distances[i]:F2} m and {distances[j]:F2} m out, close " +
                        "enough to arrive together and read as a formation.");
                }
            }
        }

        /// <summary>
        /// A creature spawned past the edge of the plate has nothing under it and drops out of the
        /// world before it ever reaches the player.
        /// </summary>
        [Test]
        public void Build_StartsEveryDummyOnTheGround()
        {
            var ground = Root(HordePocLayout.k_GroundName).GetComponent<Collider>().bounds;

            foreach (var dummy in Dummies())
            {
                var footprint = new Vector3(dummy.transform.position.x, ground.center.y, dummy.transform.position.z);

                Assert.That(ground.Contains(footprint), Is.True,
                    $"{dummy.name} starts at {dummy.transform.position}, off a plate spanning " +
                    $"{ground.min} to {ground.max}.");
            }
        }

        XROrigin PlayerOrigin()
        {
            var rig = Root(HordePocLayout.k_PlayerRigName);
            Assert.That(rig, Is.Not.Null, "Player rig missing.");

            var origin = rig.GetComponentInChildren<XROrigin>(true);
            Assert.That(origin, Is.Not.Null, "The player rig has no XROrigin.");

            return origin;
        }

        [Test]
        public void Build_CreatesTheConfiguredNumberOfDummies()
        {
            var dummies = Dummies();

            Assert.That(dummies.Length, Is.EqualTo(HordePocSceneBuilder.k_ReferenceDummyCount));
        }

        [Test]
        public void Build_StandsDummiesAtTheRightHeight()
        {
            foreach (var dummy in Dummies())
            {
                Assert.That(dummy.transform.position.y, Is.EqualTo(HordePocLayout.k_DummyCenterHeight).Within(k_Tolerance),
                    $"{dummy.name} is not standing at the right height.");
            }
        }

        /// <summary>
        /// How fast a creature may close on the player is a design limit, not a physics one: past
        /// it the gap between seeing one coming and having it on you is shorter than a reaction.
        /// </summary>
        [Test]
        public void Build_KeepsDummiesAtOrBelowTheDesignSpeedLimit()
        {
            foreach (var dummy in Dummies())
            {
                float speed = dummy.GetComponent<EnemyLocomotion>().settings.moveSpeed;

                Assert.That(speed, Is.GreaterThan(0f), $"{dummy.name} cannot move at all.");
                Assert.That(speed, Is.LessThanOrEqualTo(EnemyLocomotionSettings.k_MaxMoveSpeed),
                    $"{dummy.name} closes at {speed:F1} m/s, above the " +
                    $"{EnemyLocomotionSettings.k_MaxMoveSpeed:F1} m/s the player can react to.");
            }
        }

        /// <summary>
        /// The grip half of the defence. Velocity tracking is not a preference here: it is the only
        /// movement type that leaves the Rigidbody dynamic while it is carried, and therefore the
        /// only one where letting go hands the creature the speed of the throw instead of dropping
        /// it at the player's feet.
        /// </summary>
        [Test]
        public void Build_MakesEveryDummyGrabbableAndThrowable()
        {
            foreach (var dummy in Dummies())
            {
                Assert.That(dummy.TryGetComponent<EnemyGrabInteractable>(out var grab), Is.True,
                    $"{dummy.name} cannot be picked up with the grip.");
                Assert.That(grab.movementType, Is.EqualTo(XRBaseInteractable.MovementType.VelocityTracking),
                    $"{dummy.name} is carried as {grab.movementType}, so throwing it would just drop it.");
                Assert.That(grab.throwOnDetach, Is.True,
                    $"{dummy.name} does not carry any velocity when released.");
            }
        }

        /// <summary>
        /// The grip has to keep counting for as long as it is squeezed, not just on the frame it is
        /// pressed.
        /// </summary>
        /// <remarks>
        /// This is what makes the punch and the grab able to share a hand at all. The fist stands
        /// down while the grip is held, so the player closes their hand before reaching — and under
        /// the template's <c>StateChange</c> default that early squeeze counts for one frame and is
        /// gone by the time the hand arrives. They would suppress their own punch and grab nothing.
        /// </remarks>
        [Test]
        public void Build_LeavesTheGripOpenWhileItIsHeld()
        {
            var rig = Root(HordePocLayout.k_PlayerRigName);
            Assert.That(rig, Is.Not.Null, "Player rig missing.");

            var interactors = rig.GetComponentsInChildren<XRDirectInteractor>(true);
            Assert.That(interactors, Is.Not.Empty, "The rig has nothing to grab with.");

            foreach (var interactor in interactors)
            {
                Assert.That(interactor.selectActionTrigger,
                    Is.EqualTo(XRBaseInputInteractor.InputTriggerType.State),
                    $"'{interactor.name}' selects on {interactor.selectActionTrigger}, so squeezing " +
                    "the grip before reaching an enemy stops counting a frame later and the hand " +
                    "arrives empty.");
            }
        }

        [Test]
        public void Build_LetsEveryDummyHurtWhatItIsThrownInto()
        {
            foreach (var dummy in Dummies())
            {
                Assert.That(dummy.GetComponent<ImpactDetector>(), Is.Not.Null,
                    $"{dummy.name} passes through everything it is thrown at without a mark.");
            }
        }

        [Test]
        public void Build_MakesDummiesFaceThePlayer()
        {
            foreach (var dummy in Dummies())
            {
                var toCenter = -new Vector3(dummy.transform.position.x, 0f, dummy.transform.position.z).normalized;

                Assert.That(Vector3.Angle(dummy.transform.forward, toCenter), Is.LessThan(1f),
                    $"{dummy.name} is not facing the arena center.");
            }
        }

        /// <summary>
        /// Enemies are gnomes, not adults. Asserting the rendered bounds rather than the transform
        /// scale means the check still holds if the primitive is swapped for a real model.
        /// </summary>
        [Test]
        public void Build_MakesDummiesGnomeSized()
        {
            foreach (var dummy in Dummies())
            {
                var bounds = dummy.GetComponent<Collider>().bounds;

                Assert.That(bounds.size.y, Is.EqualTo(HordePocLayout.k_DummyHeight).Within(k_Tolerance),
                    $"{dummy.name} is {bounds.size.y:F2} m tall instead of {HordePocLayout.k_DummyHeight} m.");
                Assert.That(bounds.min.y, Is.EqualTo(0f).Within(k_Tolerance),
                    $"{dummy.name} does not have its feet on the ground.");
            }
        }

        [Test]
        public void Build_KeepsDummiesShorterThanThePlayer()
        {
            // The camera offset is where the headset sits, so it is the closest thing to eye
            // height available outside play mode.
            var eyeHeight = CameraOffset().position.y;
            Assert.That(eyeHeight, Is.GreaterThan(0f), "The rig reports no eye height.");

            foreach (var dummy in Dummies())
            {
                Assert.That(dummy.GetComponent<Collider>().bounds.max.y, Is.LessThan(eyeHeight),
                    $"{dummy.name} is taller than the player, so the horde reads as adults.");
            }
        }

        [Test]
        public void Build_GivesDummiesAPhysicsBodySoTheyCanBePunched()
        {
            foreach (var dummy in Dummies())
            {
                var body = dummy.GetComponent<Rigidbody>();

                Assert.That(body, Is.Not.Null, $"{dummy.name} has no Rigidbody and cannot be knocked back.");
                Assert.That(body.isKinematic, Is.False, $"{dummy.name} is kinematic and would ignore punches.");
                Assert.That(dummy.GetComponent<Collider>(), Is.Not.Null, $"{dummy.name} has no collider to hit.");
            }
        }

        [Test]
        public void Build_SeparatesDummiesFromEachOther()
        {
            var dummies = Dummies();

            for (int i = 0; i < dummies.Length; i++)
            {
                for (int j = i + 1; j < dummies.Length; j++)
                {
                    var distance = Vector3.Distance(dummies[i].transform.position, dummies[j].transform.position);
                    Assert.That(distance, Is.GreaterThan(1f),
                        $"{dummies[i].name} and {dummies[j].name} are spawned on top of each other.");
                }
            }
        }

        [Test]
        public void Build_PutsAFistOnEveryHandAnchor()
        {
            foreach (var anchorName in HordePocLayout.k_HandAnchorNames)
            {
                var anchor = CameraOffset().Find(anchorName);
                Assert.That(anchor, Is.Not.Null, $"The rig has no '{anchorName}' anchor.");
                Assert.That(anchor.Find(HordePocLayout.k_FistName), Is.Not.Null,
                    $"'{anchorName}' has no fist, so that hand is invisible in the headset.");
            }
        }

        /// <summary>
        /// The template's own controller meshes are invisible because their renderer points at a
        /// material that is not in the project, and Unity draws nothing for a null material without
        /// logging anything. Asserting the material resolves is what stops that from recurring.
        /// </summary>
        [Test]
        public void Build_GivesEveryFistARenderableMaterial()
        {
            foreach (var fist in Fists())
            {
                var renderer = fist.GetComponent<MeshRenderer>();

                Assert.That(renderer, Is.Not.Null, $"{fist.name} has no renderer.");
                Assert.That(renderer.enabled, Is.True, $"{fist.name} has its renderer disabled.");
                Assert.That(renderer.sharedMaterial, Is.Not.Null,
                    $"{fist.name} has no material, so it renders as nothing at all.");
            }
        }

        /// <summary>
        /// A material whose shader is missing or fails to compile still has a non-null shader —
        /// Unity swaps in the error shader, which is what paints objects magenta in the headset.
        /// Only <c>isSupported</c> tells the two apart.
        /// </summary>
        [Test]
        public void Build_GivesEveryFistAShaderThatActuallyCompiles()
        {
            foreach (var fist in Fists())
            {
                var shader = fist.GetComponent<MeshRenderer>().sharedMaterial.shader;

                Assert.That(shader, Is.Not.Null, $"{fist.name}'s material has no shader.");
                Assert.That(shader.isSupported, Is.True,
                    $"{fist.name} uses shader '{shader.name}', which does not compile on this platform; " +
                    "it would render as the magenta error material.");
                Assert.That(shader.name, Is.EqualTo(HordePocSceneBuilder.k_LitShaderName));
            }
        }

        /// <summary>
        /// Pins the decision that the fists own their material instead of borrowing one from the
        /// template. The avatar materials are runtime-tinted placeholders — <c>Skin.mat</c> is
        /// authored purple — so reusing them means shipping a colour nobody picked.
        /// </summary>
        [Test]
        public void Build_UsesTheDedicatedFistMaterial()
        {
            var expected = AssetDatabase.LoadAssetAtPath<Material>(HordePocSceneBuilder.k_FistMaterialPath);
            Assert.That(expected, Is.Not.Null,
                $"The builder did not author {HordePocSceneBuilder.k_FistMaterialPath}.");

            foreach (var fist in Fists())
            {
                Assert.That(fist.GetComponent<MeshRenderer>().sharedMaterial, Is.EqualTo(expected),
                    $"{fist.name} is not using the dedicated fist material.");
            }
        }

        [Test]
        public void Build_SizesFistsLikeAHand()
        {
            foreach (var fist in Fists())
            {
                var size = fist.transform.lossyScale;

                Assert.That(size.x, Is.EqualTo(HordePocLayout.k_FistDiameter).Within(1e-2f),
                    $"{fist.name} is not hand sized; the rig may be scaling it.");
                Assert.That(fist.transform.localPosition, Is.EqualTo(Vector3.zero).Using(Vector3Comparer.Instance),
                    $"{fist.name} is offset from the hand it belongs to.");
            }
        }

        /// <summary>
        /// A solid collider would shove enemies around with raw collision response on every graze,
        /// which is exactly the mushy feel the speed-threshold punch model exists to avoid.
        /// </summary>
        [Test]
        public void Build_GivesEveryFistAPunchTriggerRatherThanASolidCollider()
        {
            foreach (var fist in Fists())
            {
                var collider = fist.GetComponent<Collider>();

                Assert.That(collider, Is.Not.Null, $"{fist.name} has no collider and can never hit anything.");
                Assert.That(collider.isTrigger, Is.True,
                    $"{fist.name}'s collider is solid, so it would push enemies around instead of punching them.");
            }
        }

        /// <summary>
        /// The trigger is authored in world meters but a <see cref="SphereCollider"/> stores its
        /// radius in local units, and the fist is scaled down to hand size. Asserting on the world
        /// bounds is what catches the conversion being dropped.
        /// </summary>
        [Test]
        public void Build_SizesThePunchTriggerInWorldMeters()
        {
            foreach (var fist in Fists())
            {
                var radius = fist.GetComponent<Collider>().bounds.extents.x;

                Assert.That(radius, Is.EqualTo(HordePocLayout.k_PunchTriggerRadius).Within(1e-3f),
                    $"{fist.name}'s punch trigger is {radius:F3} m across instead of " +
                    $"{HordePocLayout.k_PunchTriggerRadius:F3} m; the local/world scale conversion is wrong.");
            }
        }

        /// <summary>
        /// Unity only reports trigger contacts reliably for a collider that a Rigidbody moves;
        /// without one a fist swung through an enemy is a teleporting static body and hits get
        /// dropped. Kinematic, because the hand's pose comes from tracking, not from physics.
        /// </summary>
        [Test]
        public void Build_DrivesThePunchTriggerWithAKinematicBody()
        {
            foreach (var fist in Fists())
            {
                var body = fist.GetComponent<Rigidbody>();

                Assert.That(body, Is.Not.Null, $"{fist.name} has no Rigidbody, so its trigger fires unreliably.");
                Assert.That(body.isKinematic, Is.True,
                    $"{fist.name}'s Rigidbody is dynamic and would fall out of the player's hand.");
                Assert.That(body.useGravity, Is.False, $"{fist.name} is affected by gravity.");
            }
        }

        [Test]
        public void Build_GivesEveryFistThePunchComponents()
        {
            foreach (var fist in Fists())
            {
                Assert.That(fist.GetComponent<PointVelocityTracker>(), Is.Not.Null,
                    $"{fist.name} cannot measure how fast it is swinging.");
                Assert.That(fist.GetComponent<PunchDetector>(), Is.Not.Null,
                    $"{fist.name} cannot land a punch.");
            }
        }

        /// <summary>
        /// Handedness cannot be read off the transform at runtime, because haptics are addressed by
        /// controller. A fist that buzzes the wrong hand is the kind of bug only the headset shows.
        /// </summary>
        [Test]
        public void Build_TellsEachFistWhichHandItIs()
        {
            var offset = CameraOffset();

            foreach (var anchorName in HordePocLayout.k_HandAnchorNames)
            {
                var detector = offset.Find(anchorName).Find(HordePocLayout.k_FistName)
                    .GetComponent<PunchDetector>();

                Assert.That(detector.hand, Is.EqualTo(HordePocLayout.HandSideOf(anchorName)),
                    $"The fist on '{anchorName}' would vibrate the other controller.");
            }
        }

        /// <summary>
        /// The hit flash and the death colour are driven through a MaterialPropertyBlock, and
        /// setting a property a shader does not declare is a silent no-op — no warning, no error,
        /// just an enemy that never reacts visibly to being punched.
        /// </summary>
        [Test]
        public void Build_GivesDummiesAMaterialTheHitFlashCanTint()
        {
            foreach (var dummy in Dummies())
            {
                var material = dummy.GetComponent<MeshRenderer>().sharedMaterial;

                Assert.That(material, Is.Not.Null, $"{dummy.name} has no material.");
                Assert.That(material.shader.isSupported, Is.True,
                    $"{dummy.name} uses shader '{material.shader.name}', which would render as the " +
                    "magenta error material in the headset.");
                Assert.That(material.HasColor("_BaseColor"), Is.True,
                    $"{dummy.name}'s shader has no _BaseColor, so being punched changes nothing on screen.");
            }
        }

        [Test]
        public void Build_MakesTheDummiesPunchable()
        {
            foreach (var dummy in Dummies())
            {
                var enemy = dummy.GetComponent<HordeEnemy>();

                Assert.That(enemy, Is.Not.Null, $"{dummy.name} is not an enemy and cannot be punched.");
                Assert.That(enemy.maxHealth, Is.GreaterThan(1),
                    $"{dummy.name} dies to a single punch; enemies are meant to take 2-3.");
            }
        }

        /// <summary>
        /// Mass no longer decides how far a punch throws an enemy — knockback is applied as a
        /// velocity, so distance is the same whatever the creature weighs. It still decides how
        /// they shove each other about, and Fase 2b's thrown-enemy impacts read off it.
        /// </summary>
        [Test]
        public void Build_GivesDummiesTheMassTheRestOfThePhysicsAssumes()
        {
            foreach (var dummy in Dummies())
            {
                Assert.That(dummy.GetComponent<Rigidbody>().mass,
                    Is.EqualTo(HordePocLayout.k_DummyMass).Within(1e-3f));
            }
        }

        /// <summary>
        /// How far a punch throws is the punch model's business — see
        /// <c>PunchResolverTests.Defaults_ThrowAnEnemyTheDistanceTheDesignCallsFor</c>. What the
        /// arena has to answer for is whether the creature lands back on the plate afterwards.
        /// </summary>
        [Test]
        public void Build_LeavesRoomForAPunchedEnemyToLandOnTheArena()
        {
            var settings = new PunchSettings();

            Assert.That(settings.maxKnockbackDistance, Is.LessThan(HordePocLayout.k_ArenaRadius),
                $"A full power punch throws an enemy {settings.maxKnockbackDistance:F1} m across an " +
                $"arena that only reaches {HordePocLayout.k_ArenaRadius:F1} m, so it lands off the " +
                "edge of the world.");
        }

        [Test]
        public void Build_GivesThePlayerABodyForTheHordeToAimAt()
        {
            var body = PlayerBody();
            var proxy = body.GetComponent<PlayerBodyProxy>();

            Assert.That(proxy, Is.Not.Null, "Nothing derives a torso from the headset pose.");
            Assert.That(proxy.head, Is.Not.Null,
                "The body proxy has no head to follow, so it would sit at the rig origin forever.");
            Assert.That(proxy.head, Is.EqualTo(PlayerOrigin().Camera.transform),
                "The body follows something other than the player's camera.");

            Assert.That(body.GetComponent<PlayerLatchTarget>(), Is.Not.Null,
                "The player is not a latch target, so no enemy can ever find them.");
            Assert.That(body.GetComponent<LatchFeedback>(), Is.Not.Null,
                "Nothing tells the player they were grabbed.");
        }

        [Test]
        public void Build_HangsTheBodyOffTheCameraOffset()
        {
            Assert.That(PlayerBody().transform.parent, Is.EqualTo(CameraOffset()),
                "The body has to hang off the camera offset, which is the rig's floor plane; " +
                "anywhere else and eye height is measured against the wrong zero.");
        }

        [Test]
        public void Build_CreatesEveryBodyAnchor()
        {
            var body = PlayerBody().transform;

            foreach (var layout in HordePocLayout.k_BodyAnchors)
            {
                var child = body.Find(layout.name);
                Assert.That(child, Is.Not.Null, $"The player has no '{layout.name}' anchor.");

                var anchor = child.GetComponent<LatchAnchor>();
                Assert.That(anchor, Is.Not.Null, $"'{layout.name}' is an empty object, not an anchor.");
                Assert.That(anchor.height, Is.EqualTo(layout.height),
                    $"'{layout.name}' is in the wrong band, so the wrong style of enemy aims at it.");
                Assert.That(anchor.heightFraction, Is.EqualTo(layout.heightFraction).Within(k_Tolerance));
                Assert.That(anchor.hangDrop, Is.EqualTo(layout.hangDrop).Within(k_Tolerance));
                Assert.That(anchor.bodyOffset.x, Is.EqualTo(layout.bodyOffset.x).Within(k_Tolerance));
                Assert.That(anchor.bodyOffset.y, Is.EqualTo(layout.bodyOffset.y).Within(k_Tolerance));
            }
        }

        [Test]
        public void Build_StartsEveryAnchorFree()
        {
            foreach (var anchor in PlayerBody().GetComponentsInChildren<LatchAnchor>(true))
            {
                Assert.That(anchor.isFree, Is.True,
                    $"'{anchor.name}' starts out taken and nothing could ever latch onto it.");
            }
        }

        [Test]
        public void Build_PutsAnArmAnchorOnEveryHand()
        {
            var offset = CameraOffset();

            foreach (var anchorName in HordePocLayout.k_HandAnchorNames)
            {
                var arm = offset.Find(anchorName).Find(HordePocLayout.k_ArmAnchorName);

                Assert.That(arm, Is.Not.Null, $"'{anchorName}' has no arm anchor to be grabbed by.");
                Assert.That(arm.GetComponent<LatchAnchor>(), Is.Not.Null);
                Assert.That(arm.localPosition.z, Is.LessThan(0f),
                    "The arm anchor is in front of the hand instead of back toward the elbow.");
            }
        }

        /// <summary>
        /// The exact mechanism <see cref="PunchDetector"/> uses to refuse to punch a creature
        /// hanging off its own arm: the anchor has to be inside the fist's own branch of the
        /// hierarchy. Move it anywhere else and the fist starts beating an enemy off its own
        /// forearm every time the player waves.
        /// </summary>
        [Test]
        public void Build_KeepsEachArmAnchorInsideItsOwnFistsBranch()
        {
            var offset = CameraOffset();

            foreach (var anchorName in HordePocLayout.k_HandAnchorNames)
            {
                var hand = offset.Find(anchorName);
                var fist = hand.Find(HordePocLayout.k_FistName);
                var arm = hand.Find(HordePocLayout.k_ArmAnchorName);

                Assert.That(arm.IsChildOf(fist.parent), Is.True,
                    $"The arm anchor on '{anchorName}' is outside that fist's branch, so the fist " +
                    "would punch whatever is holding that very arm.");
            }
        }

        [Test]
        public void Build_GivesEveryDummyTheComponentsItNeedsToHunt()
        {
            foreach (var dummy in Dummies())
            {
                Assert.That(dummy.GetComponent<EnemyLocomotion>(), Is.Not.Null,
                    $"{dummy.name} cannot walk toward anyone.");
                Assert.That(dummy.GetComponent<PointVelocityTracker>(), Is.Not.Null,
                    $"{dummy.name} cannot report how fast it is being carried, so a punch thrown at " +
                    "it while it holds the player would be measured against the wrong velocity.");
            }
        }

        /// <summary>
        /// Both ways of arriving have to be on screen from the first Play, because a wave of
        /// identical creatures reads as a queue rather than as a swarm. There is no spawner to mix
        /// them until Fase 3.
        /// </summary>
        [Test]
        public void Build_MixesLeapersAndClingers()
        {
            var styles = Dummies().Select(d => d.GetComponent<EnemyLocomotion>().style).ToArray();

            Assert.That(styles, Has.Some.EqualTo(LatchStyle.Leaper), "Nothing jumps at the player.");
            Assert.That(styles, Has.Some.EqualTo(LatchStyle.Clinger), "Nothing arrives on foot.");
        }

        /// <summary>
        /// An enemy turns kinematic twice in an ordinary life — mid-leap and while holding on — and
        /// Unity refuses <c>ContinuousDynamic</c> on kinematic bodies, logging a warning and
        /// downgrading it every single time.
        /// </summary>
        [Test]
        public void Build_UsesACollisionModeThatSurvivesGoingKinematic()
        {
            foreach (var dummy in Dummies())
            {
                Assert.That(dummy.GetComponent<Rigidbody>().collisionDetectionMode,
                    Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative),
                    $"{dummy.name} would spam the console every time it jumped.");
            }
        }

        GameObject PlayerBody()
        {
            var body = CameraOffset().Find(HordePocLayout.k_PlayerBodyName);

            Assert.That(body, Is.Not.Null,
                $"The rig has no '{HordePocLayout.k_PlayerBodyName}'; enemies have nothing to walk toward.");

            return body.gameObject;
        }

        Transform CameraOffset()
        {
            var origin = PlayerOrigin();
            Assert.That(origin.CameraFloorOffsetObject, Is.Not.Null, "The XROrigin has no camera offset.");

            return origin.CameraFloorOffsetObject.transform;
        }

        GameObject[] Fists()
        {
            var offset = CameraOffset();
            var fists = HordePocLayout.k_HandAnchorNames
                .Select(name => offset.Find(name))
                .Where(anchor => anchor != null)
                .Select(anchor => anchor.Find(HordePocLayout.k_FistName))
                .Where(fist => fist != null)
                .Select(fist => fist.gameObject)
                .ToArray();

            Assert.That(fists.Length, Is.EqualTo(HordePocLayout.k_HandAnchorNames.Length),
                "Not every hand anchor got a fist.");

            return fists;
        }

        GameObject[] Dummies()
        {
            var arena = Root(HordePocLayout.k_ArenaCenterName);
            Assert.That(arena, Is.Not.Null, "Arena center marker is missing.");

            var root = arena.transform.Find(HordePocLayout.k_DummyRootName);
            Assert.That(root, Is.Not.Null, "Dummy container is missing.");

            return Enumerable.Range(0, root.childCount)
                .Select(i => root.GetChild(i).gameObject)
                .ToArray();
        }

        class Vector3Comparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            public static readonly Vector3Comparer Instance = new Vector3Comparer();

            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < k_Tolerance;
            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }
    }
}
