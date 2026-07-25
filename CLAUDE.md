# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity **6000.3.10f1** VR multiplayer game built on top of Unity's VR Multiplayer Template (hence the `VRMP` / `XRMultiplayer` naming everywhere). Stack: XR Interaction Toolkit 3.3, OpenXR (Meta Quest + Android XR feature sets), Netcode for GameObjects 2.9 in **distributed authority** mode, Unity Gaming Services (Authentication, Multiplayer Sessions, Vivox), URP 17.3.

Target is Android/Quest (`AndroidMinSdkVersion 30`). Quality tiers are `Performant` / `Balanced` / `High Fidelity`.

`Assets/VRMPAssets/` and `Assets/Editor/` are the template's code. `Assets/Samples/` is vendored XRI/XR Hands sample code — treat as third-party and don't edit it.

**The actual game lives in `Assets/HordeAttack/`** (`Runtime/`, `Editor/`, `Tests/EditMode/`, each with its own asmdef). It is a co-op VR horde survival game: waves of enemies advance on the players, who repel them by punching, and enemies can also be grabbed with the grip and thrown into each other. **`PLAN.md` at the repo root is the source of truth for progress — read it before doing anything and resume from the first incomplete phase.**

Because hand-editing Unity YAML is fragile, the POC scene is generated from code rather than committed as an opaque asset: `Tools > HordeAttack > 1. Generar Escena POC` (`HordePocSceneBuilder`). Scene construction is deliberately split from the menu entry point so tests can exercise the real builder. Follow that pattern for new scenes and prefabs.

## Commands

Unity editor lives at `C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe`.

**Any `-batchmode` command fails while the editor has the project open** — Unity holds an exclusive lock (`Temp/UnityLockfile`). Check with `tasklist | grep -i unity` before trying; the editor is usually running during a work session.

```bash
# Only when the editor is CLOSED:
# Compile check without the GUI
"/c/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe" -batchmode -quit -nographics \
  -projectPath "<proj>" -logFile - | grep -E "error CS|Compilation failed"

# Run tests (com.unity.test-framework 1.6.0 + code coverage 1.3.0 are installed)
"<unity>" -batchmode -projectPath "<proj>" -runTests -testPlatform EditMode \
  -testResults "results.xml" -logFile -
# Single test: add -testFilter "Namespace.Class.TestMethod"
```

**While the editor IS open**, the way to verify a C# change is to let the editor recompile (it does so on regaining focus) and then read its live log:

```bash
grep -nE "error CS|Compilation failed" "C:/Users/rvazq/AppData/Local/Unity/Editor/Editor.log" | tail -20
```

That log is the authoritative record of the current compile state — it is where the API Updater rewrites and Burst/ILPP failures show up too.

Tests live in `Assets/HordeAttack/Tests/EditMode/` and `Assets/HordeAttack/Tests/PlayMode/`. The full verified invocation, including coverage:

```bash
"<unity>" -batchmode -nographics -projectPath "<proj>" \
  -runTests -testPlatform EditMode -testFilter "HordeAttack.Tests.*" \
  -testResults "<out>/results.xml" \
  -enableCodeCoverage -coverageResultsPath "<out>/coverage" \
  -coverageOptions "generateHtmlReport;generateAdditionalMetrics;assemblyFilters:+HordeAttack,+HordeAttack.Editor" \
  -logFile "<out>/tests.log"
```

**Do not trust the process exit code alone** — a run that never executed any test has been observed exiting 0. Parse `results.xml` and check the `passed`/`total`/`failed` attributes on the root `<test-run>` element.

**Unity Gaming Services can only be initialized in play mode.** `UnityServices.InitializeAsync()` throws `ServicesInitializationException: You are attempting to initialize Unity Services in Edit Mode` from any editor script or EditMode test, regardless of dashboard state — so anything that touches UGS has to live in the runtime assembly and be exercised from PlayMode. `HordeAttack.Tests.UgsPreflightTests` (category `UgsPreflight`) is the standing check that Authentication and Multiplayer/Sessions are actually enabled on the linked project; whether a service is switched on exists only server-side, so an offline check would prove nothing. Swap `-testPlatform EditMode` for `PlayMode` in the invocation above to run it.

Editor-only code cannot create an additive scene while an untitled unsaved scene is open, which is precisely the state batch mode starts in. Tests that need a scene should build into the active scene and destroy what they created in teardown. Also note that `Collider.bounds` is stale outside play mode until `Physics.SyncTransforms()` is called, so bounds assertions silently see the untransformed shape.

Builds are done through the editor (File > Build Profiles); there is no build script or CI. `Assets/Editor/EditorUtils/BuildIncrementer/BuildIdIncrementer.cs` auto-bumps the patch version and Android `bundleVersionCode` on build, but it is compiled out — uncomment `#define AUTO_INCREMENT_BUILD` at the top to enable it.

Scenes: `Assets/Scenes/SampleScene.unity` is the main scene (index 0, the full sample world with minigames); `BasicScene.unity` is a stripped-down scene, disabled in build settings.

### Testing multiplayer locally

Use **Multiplayer Play Mode** (`com.unity.multiplayer.playmode`, virtual players configured in `ProjectSettings/VirtualProjectsConfig.json`) — the `VRMP` assembly defines `HAS_MPPM` and `HAS_PARRELSYNC` and `AuthenticationManager` reads the MPPM `-name` / ParrelSync clone argument to give each instance a distinct UGS auth profile. Without that, clones share a profile and authentication collides.

Distributed-authority sessions need working UGS credentials. `SessionManager` falls back to `SessionType.LocalOnly` (direct `UnityTransport`, `StartHost`/`StartClient`, Vivox disabled) when `Application.internetReachability == NetworkReachability.NotReachable` — that path is the way to test networking offline.

## Architecture

### Distributed authority, not client-server

`NetworkTopology` is set to **DistributedAuthority** on the `Network Manager VR Multiplayer` prefab. This is the single most important thing to internalize:

- **`IsOwner` is the authority check, not `IsServer`.** Every state mutation is gated on `IsOwner`; non-owners request changes via `[Rpc(SendTo.Owner)]`.
- The session has a *session owner* that can migrate. Subscribe to `NetworkManager.Singleton.OnSessionOwnerPromoted` (or `XRINetworkGameManager.Instance.OnSessionOwnerPromoted`) to re-take responsibility for orphaned state.
- RPCs use the modern `[Rpc(SendTo.X)]` attribute with a `...Rpc` suffix — **not** `ServerRpc`/`ClientRpc`.
- The pervasive pattern is a two-hop broadcast: a `[Rpc(SendTo.Owner)] FooOwnerRpc(...)` that the owner handles and re-broadcasts as `[Rpc(SendTo.Everyone)] FooRpc(...)`. Everyone-RPCs open with `if (clientId != NetworkManager.Singleton.LocalClientId)` so the originator doesn't double-apply.
- `NetworkVariable`s are declared `ReadPermission.Everyone, WritePermission.Owner`.

### Entry points

`XRINetworkGameManager` (`Scripts/Network/NetworkManagers/`) is the central hub — a plain `MonoBehaviour` (not a `NetworkBehaviour`) with `[RequireComponent(typeof(SessionManager), typeof(AuthenticationManager))]`. Singleton via `XRINetworkGameManager.Instance`.

State is published through **`BindableVariable<T>`** (`Unity.XR.CoreUtils.Bindings.Variables`), not C# events — consume with `.Subscribe(cb)` / `.Unsubscribe(cb)` / `.Value`:
`Connected`, `CurrentConnectionState` (`None/Authenticating/Authenticated/Connecting/Connected`), `ConnectedRoomName`, `LocalPlayerName`, `LocalPlayerColor`, plus the static `SessionManager.status` for UI status text.

Plain `Action` events: `OnPlayerStateChanged(ulong, bool)`, `OnConnectionUpdated(string)`, `OnConnectionFailedAction(string)`, `OnSessionOwnerPromoted(ulong)`.

Other statics worth knowing: `XRINetworkPlayer.LocalPlayer`, `XRINetworkGameManager.LocalId`, `XRINetworkGameManager.AuthenicationId` (yes, misspelled in the source — match it), `PlayerHudNotification.Instance.ShowText(msg, displayTime)` for toasts. Player lookup is `Instance.TryGetPlayerByID(ulong, out XRINetworkPlayer)`.

### Connection flow

`Awake` → `AuthenticationManager.Authenticate()` (`UnityServices.InitializeAsync` with a per-instance profile, then `SignInAnonymouslyAsync`) → state `Authenticated`. Then one of `QuickJoinLobby()`, `JoinLobbyByCode()`, `JoinLobbySpecific()`, `CreateNewLobby()`, all gated by `AbleToConnect()`. `SessionManager` drives the Multiplayer Sessions SDK (`MultiplayerService.Instance.CreateOrJoinSessionAsync(...).WithDistributedAuthorityNetwork()`); session metadata uses short property keys `"j"/"r"/"b"/"s"/"e"` (join code, region, build id, scene, editor). **Nothing calls `StartClient()` in DA mode** — the session SDK owns the NGO connection. Player prefab spawn → `XRINetworkPlayer.OnNetworkSpawn` → `OnLocalClientStarted` → state `Connected`. `VoiceChatManager` then logs into Vivox and joins a positional channel keyed on the session id.

### Networked interactables

`NetworkBaseInteractable` (`Scripts/Network/NetworkInteractions/`) is the base class for anything grabbable. It implements `IXRSelectFilter`/`IXRHoverFilter` and registers itself into the `XRBaseInteractable`'s filter lists in `Awake` — that filter is what makes an object held by a remote player ungrabbable locally.

Grab → ownership handoff: `OnSelectEnteredLocal` → `OnSelectOwnerRpc(true, localId)` → owner calls `SetOwnershipLock(false)` + `ChangeOwnership(clientId)` and broadcasts `OnSelectRpc`. On release the object relinquishes back to the session owner after `relinquishOwnershipTime` (5s) via `ResetObjectToSessionOwnerRpc`.

To add a new networked interactable, inherit `NetworkBaseInteractable` and override the `virtual` hooks — `Selected(bool)`, `Activated(bool)`, `Hovered(bool)`, `OnIsInteractingChanged`, `ResetObject()`. Note these run on **all** clients, not just the owner. See `NetworkXRKnob`, `NetworkXRLever`, `NetworkProjectileLauncher` for worked examples. Unity Events are exposed in pairs: `*NetworkedEventServer` (owner only) and `*NetworkedEventAll`.

`NetworkPhysicsInteractable` adds Rigidbody handling and **deliberately disables its `ClientNetworkTransform` while an ownership request is in flight** so grabs feel instant under latency, re-enabling it in `OnGainedOwnership`/`OnLostOwnership`. Fast-moving collisions can steal ownership (`OnCollisionEnter` → `RequestOwnership()`); `OwnershipTransferBlocked()` is the guard list.

`ClientNetworkTransform` overrides `OnIsServerAuthoritative()` to return a serialized field defaulting to **false** — transforms are owner-authoritative.

Networked prefabs must be registered in the `NetworkPrefabsList` assets under `Assets/VRMPAssets/Prefabs/NetworkedPrefabs/` (`_KinematicNetworkPrefabsList`, `_VelocityNetworkPrefabsList`). `GenerateDefaultNetworkPrefabs` is off, so nothing is auto-registered.

### Avatar replication

`XRINetworkPlayer` replicates identity with NetworkVariables only, no RPCs: `m_PlayerName`, `m_PlayerColor`, `m_PlayerVoiceId`, `m_PlatformType`, `selfMuted`. Local writes are driven by subscriptions to the static bindables.

Head and hand poses are **not** NetworkVariables — `LateUpdate` on the owner copies the XR Origin camera/hand transforms into child transforms that carry `NetworkTransform`/`ClientNetworkTransform`. Finger poses go through `XRHandPoseReplicator` using `NetworkList<Vector3>`/`NetworkList<float>` with three bandwidth tiers (`m_FidelityLevel` 0/1/2). `XRAvatarIK` is purely local cosmetic — it derives torso/neck from the already-replicated head transform and is reused by the offline avatar.

Voice amplitude is never networked; each client reads `VivoxParticipant.ParticipantAudioEnergyChanged` locally and drives the mouth blend shape in `XRAvatarVisuals`.

### Minigame framework

`MiniGameBase : MonoBehaviour, IMiniGame` and `MiniGameManager : NetworkBehaviour` sit on the **same GameObject** (`[RequireComponent]`). `MiniGameBase` is not abstract — subclass it and override `Start()`, `SetupGame()`, `StartGame()`, `UpdateGame(float)`, `FinishGame(bool)`, `RemoveInteractables()`, always calling `base.X()` first. Note `Start()` itself is virtual and does the component caching.

The manager owns the lifecycle through `NetworkVariable<GameState> networkedGameState` (`None/PreGame/InGame/PostGame`); its `OnValueChanged` fans out to `SetPreGameState`/`SetInGameState`/`SetPostGameState`, which call the corresponding `MiniGameBase` methods. Only the manager's `Update()` ticks `UpdateGame`, and only while `InGame`. Player roster is replicated with `NetworkList<ulong> m_CurrentPlayers` / `m_QueuedUpPlayers`; readiness comes from `SubTrigger` volumes.

Scores are **not** NetworkVariables — each client accumulates locally and broadcasts the absolute total via `[Rpc(SendTo.Everyone)] SubmitScoreRpc(score, clientId, finishGameOnScoreSubmit)`; every peer re-sorts (`GameType.Time` ascending, `GameType.Score` descending) into `ScoreboardSlot` UI. Only the record `m_BestAllScore` is a NetworkVariable, written by the owner in `StopGameOwnerRpc()`.

Reference implementations: `Slingshot/`, `WhackAPig/`, `Climber/` under `MiniGames/MiniGameScripts/`.

## Conventions

- Namespaces: `XRMultiplayer` for nearly everything, `XRMultiplayer.MiniGames` for minigames. Some template-derived files use `UnityEngine.XR.Templates.VRMultiplayer` or `UnityEngine.XR.Content.Interaction`.
- Fields: `m_PascalCase` for private/serialized, `s_` for statics, `k_` for consts. `[SerializeField]` on private fields with `[Header]`/`[Tooltip]`. Public properties are `camelCase` wrapping an `m_` backing field.
- **Log with `Utils.Log` / `Utils.LogWarning` / `Utils.LogError`** (`Scripts/Helpers/Utils.cs`), not `Debug.Log` — they add the `[XRMultiplayer]` prefix and respect `Utils.s_LogLevel`.
- Reuse `Pooler` / `PoolerProjectiles` (`GetItem()` / `ReturnItem()`) for spawned transients rather than instantiating; `SubTrigger` (`Action<Collider,bool> OnTriggerAction`) for trigger volumes; `TextButton.UpdateButton(...)` in `Utils.cs` for dynamically relabeled buttons.
- Components find each other with `TryGetComponent(out x)`, `GetComponentInParent<T>()`, `FindFirstObjectByType<T>()` — never `GameObject.Find`.
- Coroutines are stored in `IEnumerator m_XRoutine` fields so they can be stopped and restarted.
- XML `<summary>` doc comments on nearly every public member; `<inheritdoc/>` on Unity messages.
- Detection uses tags `"Target"` and `"PlayerHand"`, then `GetComponentInParent<XRINetworkPlayer>()` / `<NetworkPhysicsInteractable>()` and an `.IsOwner` check before scoring.

## Gotchas

- `OnGainedOwnership`/`OnLostOwnership` fire on the server even when it is not the owner — existing overrides re-check `IsOwner` to work around it. Keep doing that.
- `NetworkBaseInteractable.OnNetworkSpawn` sets `DontDestroyWithOwner = true`, so objects outlive the player who held them; `m_ResetObjectOnDisconnect` snaps them back to the pose captured in `OnEnable`.
- `OnSelectExitedLocal` early-outs on `!IsSpawned` because XRI fires select-exit during destruction.
- `NetworkSocketInteractor` disables its socket for 0.5s after spawn so it doesn't grab objects mid-spawn.
- `MiniGame_Climber.OnDestroy` unsubscribes a freshly-allocated lambda from `SubTrigger.OnTriggerAction` (`MiniGame_Climber.cs:29` subscribes one closure, `:39` subtracts a different one), so the handler is never actually removed. Pre-existing bug; don't copy the pattern.
- **MPPM namespace churn.** The package renamed `Unity.Multiplayer.Playmode` → `Unity.Multiplayer.PlayMode` (capital M). Unity's API Updater already stripped the stale `using` from `XRINetworkGameManager.cs`, leaving an **empty `#if UNITY_EDITOR && HAS_MPPM` block at line 13** — that emptiness is expected, not a merge accident. The one real MPPM call site is `XRINetworkGameManager.cs:231` (`Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor`), fully qualified. Separately, `AuthenticationManager.cs:8` has a leftover `using UnityEngine.XR.Interaction.Toolkit.UI;` inside its `#if HAS_MPPM` that has nothing to do with MPPM; it is harmless because `CheckMPPM()` only reads `Environment.GetCommandLineArgs()` and touches no MPPM types.
- **The player rig prefab is not centered on its own root.** `XRMPT_XR_Origin_Setup` nests `XR Origin (XR Rig)` at `z = -12` inside the prefab, so placing the prefab root at a spawn point puts the player 11.58 m behind it. Position the `XROrigin` descendant, not the root — `HordePocSceneBuilder.CenterRigOnArena` does this by subtracting the origin's world position from the root. Tests that assert on the root transform will pass while the player stands off the map.
- **The template's avatar materials are runtime-tinted placeholders.** `Skin.mat` is authored purple (`_BaseColor` 0.59/0.52/0.76), not skin coloured — the avatar system overwrites the colour at runtime via `XRINetworkGameManager.LocalPlayerColor`. Reusing one for anything outside the avatar ships a colour nobody chose. `HordePocSceneBuilder` authors its own `Assets/HordeAttack/Materials/Fist.mat` instead. Note that magenta in the headset is ambiguous: it means either the error shader or one of these placeholders, and `material.shader != null` cannot tell them apart — the error shader is non-null. Use `shader.isSupported`.
- **The template's controller meshes never render.** `ControllerCombined` (under `XRControllerLeftModel` / `XRControllerRightModel`, both standalone prefabs and embedded in `XRMPT_XR_Origin_Setup`) has a `MeshRenderer` pointing at material GUID `be3083a5f26d4e859d594ecbe632f87e`, which exists nowhere in `Assets/`, `Packages/`, or `Library/PackageCache/`. A null material makes Unity skip the draw call silently — no log, no pink error material — so the hands look absent while the ray interactors still work. That symptom (rays but no hands) is this, not a scene bug. `HordePocSceneBuilder` sidesteps it by parenting its own fist to all four hand anchors instead of patching third-party prefab YAML; which anchor is live is decided at runtime by XRI's `XRInputModalityManager`, so the builder cannot pick just one.
- Editing Unity YAML assets (`.unity`, `.prefab`, `.asset`, `.meta`) by hand is risky — prefer doing it through the editor. `.vscode/settings.json` hides these from the file explorer by default.
