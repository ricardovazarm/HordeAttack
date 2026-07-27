using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
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
        /// <remarks>
        /// Ten is enough for the fan to read as a horde rather than as a handful of test targets.
        /// They still fit: at the mid spawn distance the arc leaves about a meter between neighbours,
        /// several times an enemy's own width.
        /// </remarks>
        public const int k_ReferenceDummyCount = 10;


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
            KeepTheGripOpenWhileHeld(rig);
            CreateHandVisuals(rig);
            CreatePlayerBody(rig);
        }

        /// <summary>
        /// Makes the grip mean "my hand is open to grab" for as long as it is squeezed, instead of
        /// only on the frame it is pressed.
        /// </summary>
        /// <remarks>
        /// The template's rig ships its direct interactors on <c>StateChange</c>, where squeezing
        /// with nothing in reach counts for a single frame and then stops counting. That is unusable
        /// here, because <see cref="PunchDetector"/> now stands down while the grip is held: the
        /// player has to be able to close their hand <em>before</em> arriving, or they punch the
        /// creature away on the way in. Under <c>StateChange</c> squeezing early would suppress the
        /// punch and then grab nothing — worse than either half on its own.
        /// <para>
        /// <c>State</c> also buys the other half of the gesture: hold the grip, keep still, and the
        /// creature walking at you ends up in your fist.
        /// </para>
        /// <para>
        /// Set from code rather than by editing the prefab's YAML, which is third-party and fragile —
        /// the same reason the POC brings its own fists instead of patching the template's hands.
        /// </para>
        /// </remarks>
        static void KeepTheGripOpenWhileHeld(GameObject rig)
        {
            var interactors = rig.GetComponentsInChildren<XRDirectInteractor>(true);
            if (interactors.Length == 0)
            {
                Utils.LogWarning(
                    "El rig no tiene ningún XRDirectInteractor; no se podrá agarrar nada con el grip.");
                return;
            }

            foreach (var interactor in interactors)
                interactor.selectActionTrigger = HordePocLayout.k_GripTrigger;
        }

        /// <summary>
        /// Builds the torso stand-in the horde walks toward and takes hold of.
        /// </summary>
        /// <remarks>
        /// A VR rig is a head and two hands with nothing in between, so there is no body to grab and
        /// nowhere for a leaper to aim. <see cref="PlayerBodyProxy"/> derives one from the headset
        /// pose; this hangs the anchors off it and gives the player the components that make it a
        /// target at all.
        /// </remarks>
        static void CreatePlayerBody(GameObject rig)
        {
            var origin = rig.GetComponentInChildren<XROrigin>(true);
            var offset = FindCameraOffset(rig);

            if (origin == null || offset == null || origin.Camera == null)
            {
                Utils.LogError(
                    "El rig no tiene cámara o Camera Offset; la escena se genera sin cuerpo del jugador " +
                    "y los enemigos no tendrán a quién agarrarse.");
                return;
            }

            var body = new GameObject(HordePocLayout.k_PlayerBodyName);
            body.transform.SetParent(offset, false);

            var proxy = body.AddComponent<PlayerBodyProxy>();
            proxy.head = origin.Camera.transform;

            foreach (var layout in HordePocLayout.k_BodyAnchors)
                CreateBodyAnchor(body.transform, layout);

            CreateArmAnchors(offset);

            // Added after the anchors exist: PlayerLatchTarget collects them in Awake, and
            // LatchFeedback needs the target to subscribe to. In a built scene the order of
            // AddComponent does not matter, but it does the moment a test builds one at runtime.
            body.AddComponent<PlayerLatchTarget>();
            body.AddComponent<LatchFeedback>();
        }

        static void CreateBodyAnchor(Transform body, HordePocLayout.LatchAnchorLayout layout)
        {
            var anchor = new GameObject(layout.name);
            anchor.transform.SetParent(body, false);

            anchor.AddComponent<LatchAnchor>().Configure(
                layout.height, layout.heightFraction, layout.bodyOffset, layout.hangDrop);
        }

        /// <summary>
        /// Hangs an anchor just behind each hand, so enemies can take hold of an arm.
        /// </summary>
        /// <remarks>
        /// On all four hand anchors, for the same reason the fists are: XRI decides at runtime
        /// whether the controller branch or the hand-tracking branch is live, and the builder cannot
        /// know which. The half that is switched off is skipped at runtime rather than here —
        /// see <see cref="PlayerLatchTarget"/>.
        /// <para>
        /// These are placed in the hand's own local space and are deliberately not children of the
        /// body proxy: an arm goes where the controller goes, and a torso-relative height would put
        /// the anchor in mid-air whenever the player raised a hand.
        /// </para>
        /// </remarks>
        static void CreateArmAnchors(Transform offset)
        {
            foreach (var anchorName in HordePocLayout.k_HandAnchorNames)
            {
                var hand = offset.Find(anchorName);
                if (hand == null)
                    continue;

                var arm = new GameObject(HordePocLayout.k_ArmAnchorName);
                arm.transform.SetParent(hand, false);
                arm.transform.localPosition = Vector3.back * HordePocLayout.k_ArmAnchorSetback;

                // The placement fields are left at zero: nothing drives them, because this anchor is
                // positioned by the hand it hangs from rather than by the body proxy.
                arm.AddComponent<LatchAnchor>().Configure(LatchHeight.Low, 0f, Vector2.zero, 0f);
            }
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

                CreateFist(anchor, material, anchorName);
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

        /// <summary>
        /// Builds one fist: the visible sphere, the trigger that notices enemies, and the
        /// components that measure the swing and resolve the punch.
        /// </summary>
        static void CreateFist(Transform anchor, Material material, string anchorName)
        {
            var fist = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fist.name = HordePocLayout.k_FistName;

            fist.transform.SetParent(anchor, false);
            fist.transform.localPosition = Vector3.zero;
            fist.transform.localScale = Vector3.one * HordePocLayout.k_FistDiameter;

            if (material != null)
                fist.GetComponent<MeshRenderer>().sharedMaterial = material;

            MakePunchTrigger(fist);

            fist.AddComponent<PointVelocityTracker>();
            fist.AddComponent<PunchDetector>().hand = HordePocLayout.HandSideOf(anchorName);
        }

        /// <summary>
        /// Turns the fist's primitive collider into the volume that registers punches.
        /// </summary>
        /// <remarks>
        /// A trigger, not a solid collider: a solid fist would shove enemies around with raw physics
        /// on every graze, and knockback is supposed to come from the punch model, not from the
        /// collision. The kinematic Rigidbody is what makes the trigger fire reliably — without one
        /// Unity treats a moving collider as a displaced static body, which is both expensive and
        /// unreliable for contact events.
        /// </remarks>
        static void MakePunchTrigger(GameObject fist)
        {
            var trigger = fist.GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = HordePocLayout.k_PunchTriggerLocalRadius;

            var body = fist.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
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

                // A fan in front of the player, each at its own distance. Horizontal only; height is
                // applied separately so the facing direction stays level instead of tilting toward
                // the ground.
                var spawn = HordePocLayout.ApproachPosition(
                    i, k_ReferenceDummyCount,
                    HordePocLayout.k_SpawnNearDistance,
                    HordePocLayout.k_SpawnFarDistance,
                    HordePocLayout.k_SpawnArcDegrees);

                dummy.transform.SetParent(root.transform, false);
                dummy.transform.localPosition = spawn + Vector3.up * HordePocLayout.k_DummyCenterHeight;
                dummy.transform.localScale = Vector3.one * HordePocLayout.k_DummyScale;

                // Facing the player from the moment the scene loads, so the first thing the player
                // sees is a group already looking at them rather than one that turns once it starts
                // moving.
                dummy.transform.localRotation = Quaternion.LookRotation(-spawn.normalized, Vector3.up);

                var body = dummy.AddComponent<Rigidbody>();

                // A gnome, not an adult: light enough that a solid punch visibly throws it.
                body.mass = HordePocLayout.k_DummyMass;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                // Speculative rather than ContinuousDynamic, which Unity refuses on kinematic
                // bodies and silently downgrades with a warning every time. An enemy goes kinematic
                // twice in an ordinary life — once mid-leap and once while holding on — so that
                // warning would fire constantly.
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                dummy.AddComponent<PointVelocityTracker>();

                // HordeEnemy goes on before EnemyLocomotion, which declares
                // [RequireComponent(typeof(HordeEnemy))]. The other way round Unity satisfies that
                // requirement by creating a HordeEnemy itself, and the explicit AddComponent that
                // follows is then refused — HordeEnemy is [DisallowMultipleComponent] — so the
                // dummy would end up wired by accident rather than on purpose.
                dummy.AddComponent<HordeEnemy>();
                dummy.AddComponent<ImpactDetector>();
                MakeGrabbable(dummy);

                // Alternating, so the POC always shows both ways of arriving without needing a
                // spawner to mix them.
                dummy.AddComponent<EnemyLocomotion>().style =
                    i % 2 == 0 ? LatchStyle.Leaper : LatchStyle.Clinger;
            }
        }

        /// <summary>
        /// Turns a dummy into something the grip can pick up and throw.
        /// </summary>
        /// <remarks>
        /// Velocity tracking rather than kinematic movement, and that is the choice this whole phase
        /// rests on: it leaves the Rigidbody dynamic while it is being carried, which is the only
        /// movement type where letting go hands the creature the hand's real velocity instead of
        /// dropping it on the spot. Throwing is the point.
        /// <para>
        /// Dynamic attach keeps the creature where it was grabbed instead of snapping its centre
        /// into the palm. On an enemy clinging to the player's chest that difference is the whole
        /// gesture — the player closes their grip on it where it actually is and pulls it off, rather
        /// than watching it teleport into their hand first.
        /// </para>
        /// </remarks>
        static void MakeGrabbable(GameObject dummy)
        {
            var grab = dummy.AddComponent<EnemyGrabInteractable>();

            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = true;

            // Left on, which is what puts a released enemy back under the Dummies root rather than
            // leaving it as a loose root object. HordeEnemy.Detach has already reparented it there
            // by the time the toolkit records where to put it back.
            grab.retainTransformParent = true;
        }

        /// <summary>Moves a freshly created root object into the scene being built.</summary>
        static void Adopt(GameObject go, Scene scene)
        {
            if (go.scene != scene)
                SceneManager.MoveGameObjectToScene(go, scene);
        }
    }
}
