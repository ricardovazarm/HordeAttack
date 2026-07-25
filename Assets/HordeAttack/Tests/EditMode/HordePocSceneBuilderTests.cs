using System.Linq;
using HordeAttack.EditorTools;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        [Test]
        public void Build_PutsTheDummiesInFrontOfThePlayerNotAcrossTheArena()
        {
            var origin = PlayerOrigin().transform.position;

            foreach (var dummy in Dummies())
            {
                var distance = Vector3.Distance(
                    new Vector3(origin.x, 0f, origin.z),
                    new Vector3(dummy.transform.position.x, 0f, dummy.transform.position.z));

                Assert.That(distance, Is.EqualTo(HordePocLayout.k_DummyRingRadius).Within(k_Tolerance),
                    $"{dummy.name} is {distance:F2} m from the player instead of {HordePocLayout.k_DummyRingRadius} m.");
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
        public void Build_PlacesDummiesOnTheRingAroundThePlayer()
        {
            foreach (var dummy in Dummies())
            {
                var position = dummy.transform.position;
                var horizontalDistance = new Vector2(position.x, position.z).magnitude;

                Assert.That(horizontalDistance, Is.EqualTo(HordePocLayout.k_DummyRingRadius).Within(k_Tolerance),
                    $"{dummy.name} is not on the dummy ring.");
                Assert.That(position.y, Is.EqualTo(HordePocLayout.k_DummyCenterHeight).Within(k_Tolerance),
                    $"{dummy.name} is not standing at the right height.");
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

        [Test]
        public void Build_LeavesFistsWithoutCollidersUntilPunchingExists()
        {
            foreach (var fist in Fists())
            {
                Assert.That(fist.GetComponent<Collider>(), Is.Null,
                    $"{fist.name} has a collider and would shove the dummies around on contact.");
            }
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
