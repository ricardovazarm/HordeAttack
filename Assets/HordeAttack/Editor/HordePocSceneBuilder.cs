using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRMultiplayer;

namespace HordeAttack.EditorTools
{
    /// <summary>
    /// Builds the HordeAttack proof-of-concept scene from code.
    /// </summary>
    /// <remarks>
    /// Unity scenes are YAML and editing them by hand is error prone, so the POC scene is
    /// generated instead of committed as an opaque asset. <see cref="PopulateScene"/> is kept
    /// separate from the menu entry point so tests can build into a throwaway additive scene
    /// without disturbing whatever the user currently has open.
    /// </remarks>
    public static class HordePocSceneBuilder
    {
        public const string k_SceneDirectory = "Assets/HordeAttack/Scenes";
        public const string k_ScenePath = k_SceneDirectory + "/HordePOC.unity";

        /// <summary>Player rig prefab shipped with the VR Multiplayer template.</summary>
        public const string k_PlayerRigPrefabPath =
            "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRMPT_XR_Origin_Setup.prefab";

        /// <summary>Number of stationary reference dummies placed around the player.</summary>
        public const int k_ReferenceDummyCount = 3;

        public const string k_MaterialDirectory = "Assets/HordeAttack/Materials";

        /// <summary>Material used for the fist markers, created on first use.</summary>
        /// <remarks>
        /// The fists get a material of their own rather than borrowing one from the template. The
        /// template's avatar materials are placeholders that the avatar system tints at runtime —
        /// <c>Skin.mat</c>, despite the name, is authored purple — so reusing them means the POC
        /// inherits a colour nobody chose.
        /// </remarks>
        public const string k_FistMaterialPath = k_MaterialDirectory + "/Fist.mat";

        /// <summary>URP's standard lit shader, the one the rest of the project renders with.</summary>
        public const string k_LitShaderName = "Universal Render Pipeline/Lit";

        static readonly Color k_FistColor = new Color(0.85f, 0.66f, 0.52f);

        [MenuItem("Tools/HordeAttack/1. Generar Escena POC")]
        public static void GenerateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PopulateScene(scene);

            Directory.CreateDirectory(k_SceneDirectory);
            EditorSceneManager.SaveScene(scene, k_ScenePath);
            AssetDatabase.Refresh();

            Utils.Log($"Escena POC generada en {k_ScenePath}. Dale Play con el Quest conectado por Link.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(k_ScenePath));
        }

        /// <summary>
        /// Creates the full POC hierarchy inside <paramref name="scene"/>: ground, lighting,
        /// the player rig, an arena center marker, and the reference dummies.
        /// </summary>
        /// <remarks>
        /// Every object is explicitly moved into the target scene because newly created
        /// GameObjects otherwise land in whichever scene is currently active.
        /// </remarks>
        public static void PopulateScene(Scene scene)
        {
            if (!scene.IsValid())
                throw new System.ArgumentException("Target scene is not valid.", nameof(scene));

            CreateGround(scene);
            CreateLight(scene);
            CreatePlayerRig(scene);

            var arenaCenter = new GameObject(HordePocLayout.k_ArenaCenterName);
            Adopt(arenaCenter, scene);

            CreateReferenceDummies(scene, arenaCenter.transform);
        }

        static void CreateGround(Scene scene)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = HordePocLayout.k_GroundName;
            ground.isStatic = true;

            // A thin box rather than a Plane: planes are single sided and their MeshCollider
            // behaves badly when a thrown enemy clips through from below.
            const float thickness = 0.2f;
            ground.transform.localScale = new Vector3(
                HordePocLayout.k_ArenaRadius * 2f, thickness, HordePocLayout.k_ArenaRadius * 2f);
            ground.transform.position = new Vector3(0f, -thickness * 0.5f, 0f);

            Adopt(ground, scene);
        }

        static void CreateLight(Scene scene)
        {
            var lightObject = new GameObject(HordePocLayout.k_LightName);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Adopt(lightObject, scene);
        }

        static void CreatePlayerRig(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PlayerRigPrefabPath);
            if (prefab == null)
            {
                Utils.LogError(
                    $"No se encontró el rig del jugador en {k_PlayerRigPrefabPath}. " +
                    "La escena se genera sin rig y no se podrá probar en el visor.");
                return;
            }

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = HordePocLayout.k_PlayerRigName;
            rig.transform.position = Vector3.zero;

            Adopt(rig, scene);
            CenterRigOnArena(rig);
            CreateHandVisuals(rig);
        }

        /// <summary>
        /// Moves the rig so that its <see cref="XROrigin"/> — the point the player actually stands
        /// on — lands at the arena center.
        /// </summary>
        /// <remarks>
        /// Placing the prefab root at the origin is not enough: the template parks
        /// <c>XR Origin (XR Rig)</c> at z = -12 inside its own prefab, so a root at the origin puts
        /// the player 11.58 m behind it, past the edge of a 20 m arena. Cancelling the descendant's
        /// world offset works regardless of how the template nests things in future versions.
        /// </remarks>
        static void CenterRigOnArena(GameObject rig)
        {
            var origin = rig.GetComponentInChildren<XROrigin>(true);
            if (origin == null)
            {
                Utils.LogError("El rig no tiene XROrigin; no se puede centrar en la arena.");
                return;
            }

            // Zeroing the origin's world position also drops it to y=0, which is what the floor
            // tracking mode expects: the rig's floor plane coincides with the arena ground.
            rig.transform.position -= origin.transform.position;
        }

        /// <summary>
        /// Attaches a visible fist to every hand anchor on the rig.
        /// </summary>
        /// <remarks>
        /// The template's own controller meshes never render: their renderers reference a material
        /// that does not exist anywhere in the project, and a null material makes Unity skip the
        /// draw silently, with no error. Rather than patch third-party prefab YAML, the POC brings
        /// its own hand visual, which is also what the punch collider will hang off in Fase 1.
        /// </remarks>
        static void CreateHandVisuals(GameObject rig)
        {
            var offset = FindCameraOffset(rig);
            if (offset == null)
            {
                Utils.LogError(
                    "No se encontró el Camera Offset en el rig; la escena se genera sin puños visibles.");
                return;
            }

            var material = LoadOrCreateFistMaterial();

            foreach (var anchorName in HordePocLayout.k_HandAnchorNames)
            {
                var anchor = offset.Find(anchorName);
                if (anchor == null)
                {
                    Utils.LogWarning($"El rig no tiene el ancla de mano '{anchorName}'.");
                    continue;
                }

                CreateFist(anchor, material);
            }
        }

        /// <summary>
        /// Returns the fist material, authoring it as a project asset the first time it is needed.
        /// </summary>
        /// <remarks>
        /// Generated rather than committed for the same reason the scene is: a material is YAML
        /// too, and a broken reference inside one is invisible until it renders. Building it from
        /// a shader looked up by name means a missing shader fails loudly here instead of showing
        /// up as a magenta blob in the headset.
        /// </remarks>
        static Material LoadOrCreateFistMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_FistMaterialPath);
            if (material != null)
                return material;

            var shader = Shader.Find(k_LitShaderName);
            if (shader == null)
            {
                Utils.LogError($"No se encontró el shader '{k_LitShaderName}'; los puños saldrán sin material.");
                return null;
            }

            material = new Material(shader) { color = k_FistColor };

            Directory.CreateDirectory(k_MaterialDirectory);
            AssetDatabase.CreateAsset(material, k_FistMaterialPath);

            return material;
        }

        static void CreateFist(Transform anchor, Material material)
        {
            var fist = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fist.name = HordePocLayout.k_FistName;

            fist.transform.SetParent(anchor, false);
            fist.transform.localPosition = Vector3.zero;
            fist.transform.localScale = Vector3.one * HordePocLayout.k_FistDiameter;

            // The primitive's collider would shove the dummies around on contact. Punching is
            // Fase 1 and gets a trigger sized to the swing; until then the fist is purely visual.
            Object.DestroyImmediate(fist.GetComponent<Collider>());

            if (material != null)
                fist.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// Locates the rig's camera offset, the transform every hand anchor hangs from.
        /// </summary>
        /// <remarks>
        /// Asks <see cref="XROrigin"/> rather than walking a hardcoded path, so renaming the rig
        /// root (which this builder does) cannot break it.
        /// </remarks>
        static Transform FindCameraOffset(GameObject rig)
        {
            var origin = rig.GetComponentInChildren<XROrigin>(true);
            if (origin == null || origin.CameraFloorOffsetObject == null)
                return null;

            return origin.CameraFloorOffsetObject.transform;
        }

        static void CreateReferenceDummies(Scene scene, Transform parent)
        {
            var root = new GameObject(HordePocLayout.k_DummyRootName);
            root.transform.SetParent(parent, false);

            for (int i = 0; i < k_ReferenceDummyCount; i++)
            {
                var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                dummy.name = $"{HordePocLayout.k_DummyPrefix}{i}";

                // Ring position is horizontal; height is applied separately so the facing
                // direction stays level instead of tilting toward the ground.
                var ringPosition = HordePocLayout.RingPosition(i, k_ReferenceDummyCount, HordePocLayout.k_DummyRingRadius);

                dummy.transform.SetParent(root.transform, false);
                dummy.transform.localPosition = ringPosition + Vector3.up * HordePocLayout.k_DummyCenterHeight;
                dummy.transform.localScale = Vector3.one * HordePocLayout.k_DummyScale;

                // Face the arena center, so it is obvious which way a dummy is oriented once
                // they start walking toward the player in a later phase.
                dummy.transform.localRotation = Quaternion.LookRotation(-ringPosition.normalized, Vector3.up);

                var body = dummy.AddComponent<Rigidbody>();

                // A gnome, not an adult: light enough that a solid punch visibly throws it.
                // The real knockback curve gets tuned in Fase 1.
                body.mass = 20f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }

        /// <summary>Moves a freshly created root object into the scene being built.</summary>
        static void Adopt(GameObject go, Scene scene)
        {
            if (go.scene != scene)
                SceneManager.MoveGameObjectToScene(go, scene);
        }
    }
}
