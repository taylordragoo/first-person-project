# CAS + Tactical Character Multiplayer Plan

## Progress

| Step | Status | Summary |
|---:|:---|---|
| 1 | DONE | NGO 2.13.1, Transport 6.5.0, Playmode 2.0.2, Tools 2.2.10 installed. Core asmdef at `Assets/FPSProject/Multiplayer/Core/`. |
| 2 | DONE | `INetworkSessionBootstrap` + `DirectNetworkSessionBootstrap` + `DevSessionLauncher`. NetworkManager scene at `Assets/Scenes/MultiplayerTest.unity`. Minimal test cube spawns as host. |
| 3 | DONE | Multiplayer prefab variant + owner-only init (`NetworkCasPlayer`, `NetworkFPSExampleController`). Prefab at `Assets/FPSProject/Multiplayer/Prefabs/CAS_Player_Network.prefab`. Host spawns with LocalOwner mode, all owner-only components enabled, Tactical presentation armed. |
| 4 | DONE | Core data contracts (`OwnerMotionSample`, `ProxyPresentationState`, `PlayerSimulationMode`, `MultiplayerTuningSettings`), owner 20Hz motion submission, host validation (`HostMotionValidator`), remote interpolation buffer (`ProxyInterpolationBuffer`), soft/hard corrections, late-join snapshots. |
| 5 | DONE | `ApplyRemotePresentationState` CAS animator driver with edge detection for crouch/jump/landing/moving/aim transitions. Proxies write animator parameters directly, feed CharacterCamera pitch/yaw, and drive body yaw without running the full CAS Update path. |
| 6 | DONE | `NetworkWeaponCatalog` (4 plan weapons: TR15, WK-11 Viper, Herrington Police, Mk14 EBR) with stable IDs, magazine/cadence/fire-mode config, and authoritative ballistics. `NetworkWeaponState` NetworkBehaviour with `EquippedWeaponId`, `ActiveFireMode`, `ActiveReloadState`, `LifeState`, `NetworkList<WeaponAmmoState>`. `NetworkTacticalShooterPlayer.ApplyEquippedWeapon(ushort)`. Networked weapon prefab variants implementing `INetworkTacticalWeaponPresentation`. |
| 7 | DONE | `IWeaponShotRouter` + `OfflineWeaponShotRouter` + `NetworkWeaponShotRouter`. `WeaponProp.SubmitCombatShot()` routed through the router. `WeaponCombatRuntime.ResolveAuthoritativeShot`/`PlayShotResult` split. `NetworkShotCommand`/`NetworkShotResult` structs. Host validates ownership, sequence, life state, equipped weapon, cadence, and ammunition; resolves damage exactly once. Owner never applies local damage. `NetworkWeaponPropBridge` wires CAS weapon props to the router. |
| 8 | DONE | `DeterministicShotRandom` xorshift64 PRNG seeded by shooter/weapon/sequence (never mutates Unity Random). Per-pellet deterministic cone spread. Herrington Police: 8 pellets, 12 dmg/pellet, 96 cap, single-pellet-per-target rule. `NetworkShotResult` fixed-capacity 8-impact collection. Owner predicted one-frame presentation (recoil/muzzle/casing/audio) with dedup by shot sequence; non-owners play third-person fire + tracer/impact. |
| 9 | DONE | `NetworkHitboxHistory` 250 ms rolling pose history per player. Analytical ray-vs-capsule intersection. Host records accepted poses; shot resolver maps client tick to host time, interpolates each target's historical capsule, tests ray against capsules, resolves environment obstruction at current time. Rejects shots older than 250 ms. No self-damage this milestone. |
| 10-12 | PENDING | See Implementation Order below. |

## Summary

Build a 2-8-player listen-server game using Netcode for GameObjects (NGO) and Unity Transport. CAS remains the sole owner of local input, movement, look, camera, and locomotion decisions. The Tactical rig remains presentation-only through `CasTacticalPlayerBridge`.

Movement is owner-authoritative for responsiveness, with host validation and correction. Weapon selection, ammunition, firing cadence, hit resolution, damage, health, death, and respawning are host-authoritative. Remote clients reconstruct animation from replicated high-level state; bones and IK transforms are never transmitted.

The first playable milestone includes:

- Movement, turning, jumping, crouching, forward-only sprinting, hip fire, and ADS.
- TR15, WK-11 Viper, Herrington 11-87 Police, and Mk14 EBR.
- Direct local host/client connections, followed by Sessions and Relay after the gameplay vertical slice passes.

## 1. Foundation and Assembly Boundaries

### Packages

- Install Unity 6-compatible releases of:
  - `com.unity.netcode.gameobjects`
  - `com.unity.transport`
  - `com.unity.multiplayer.playmode`
  - `com.unity.multiplayer.tools`
- Add `com.unity.services.multiplayer` only after direct host/client movement and combat pass.
- Commit the resolved `Packages/manifest.json` and `Packages/packages-lock.json` changes without altering unrelated package entries.

### Code layout

- Create `Assets/FPSProject/Multiplayer/Core/FPSProject.Multiplayer.Core.asmdef` for pure networking contracts, serialization, interpolation, validation, weapon catalog data, and other code that does not reference `Assembly-CSharp` types.
- Reference NGO, Unity Transport, and `FPSProject.Combat.Runtime` from the core assembly as required.
- Keep KINEMATION-specific adapters under `Assets/Integrations/KINEMATION/Multiplayer` in `Assembly-CSharp`. They must remain outside a named asmdef because the current `FPSExampleController`, `CasTacticalPlayerBridge`, and Tactical Shooter scripts also compile into `Assembly-CSharp`.
- Do not add asmdefs to third-party CAS or Tactical Shooter folders during this milestone.
- Keep the existing offline CAS and Tactical prefabs unchanged and independently testable.

### Prefabs and bootstrap

- Create a multiplayer prefab variant based on `Assets/Integrations/KINEMATION/CAS_Player_Example_FPS_Tactical.prefab`.
- Add one root `NetworkObject` and one `NetworkCasPlayer` to the multiplayer variant.
- Replace the normal FPS controller on the variant with `NetworkFPSExampleController`, which derives from `FPSExampleController` but preserves the existing CAS movement implementation.
- Use project-owned Tactical player/weapon subclasses and prefab variants for network presentation APIs; do not edit KINEMATION vendor source to add networking behavior.
- Add a persistent `NetworkManager`, Unity Transport, prefab registration, and a minimal development host/client launcher.
- Hide connection details behind `INetworkSessionBootstrap`:
  - `DirectNetworkSessionBootstrap` starts local/direct host and client connections.
  - `UnityServicesSessionBootstrap` later creates or joins Sessions using Relay.
  - Gameplay networking code must not depend on which bootstrap is active.

## 2. Ownership and Player Lifecycle

### Owner-only initialization

- The multiplayer prefab starts with `PlayerInput`, the Unity `Camera`, `AudioListener`, and local camera-effect components disabled.
- `NetworkFPSExampleController.Awake()` must not call the base input activation path. It caches `PlayerInput` but leaves it inactive.
- `NetworkCasPlayer.OnNetworkSpawn()` enables input, the rendered camera, `AudioListener`, recoil camera response, and the existing CAS motor only when `IsOwner` is true.
- Remote instances never process input, call `CharacterController.Move()`, update local look, or run a gameplay camera.
- Keep any non-rendering `CharacterCamera` logic required by CAS animation or the bridge alive on proxies; disable the Unity `Camera` and `AudioListener`, not the entire animation dependency hierarchy.
- Disable all owner-only components again in `OnNetworkDespawn()` and when the player dies.

### Simulation modes

`NetworkFPSExampleController` supports three explicit modes:

- `LocalOwner`: runs the existing CAS `Update()` path unchanged.
- `RemoteProxy`: skips CAS grounding, physics, movement, look input, and camera control; applies accepted presentation state to the hidden CAS source rig.
- `Disabled`: performs no input, motor, or proxy animation work, used during death/despawn.

`ApplyRemotePresentationState()` must:

- Apply replicated MoveX, MoveY, gait, body yaw, aim yaw, aim pitch, grounded/in-air, crouch, sprint, aiming, and moving state.
- Write CAS animator parameters directly instead of calling the full base controller update.
- Detect movement, crouch, jump, landing, and aim state edges so the existing CAS procedural transition modifiers still fire once.
- Feed replicated pitch to the Tactical presentation through the existing bridge.
- Preserve the bridge as presentation-only; it may not calculate movement or look direction.

## 3. Movement Synchronization

### Data contracts

Use separate inbound and outbound state types so client claims are not confused with host-owned state.

`OwnerMotionSample : INetworkSerializable` contains:

- Client sequence and synchronized network tick.
- Position, velocity, body yaw, aim yaw, and aim pitch.
- Local MoveX/MoveY, resolved gait, grounded/in-air, crouch, sprint, and aiming flags.
- It does not contain health, alive state, authoritative weapon state, or ammunition.

`ProxyPresentationState : INetworkSerializable` contains:

- The host-accepted motion fields.
- Server-owned alive/dead state and any presentation flags needed for death or respawn.
- It remains transient locomotion state; persistent weapon and life state use NetworkVariables.

### Owner-to-host flow

- Owners run the existing CAS motor every rendered frame and submit `OwnerMotionSample` at 20 Hz using unreliable-sequenced delivery.
- The host discards stale/out-of-order sequences, future timestamps, non-finite values, and samples outside configured world bounds.
- The host derives the permitted speed from server configuration and accepted crouch/sprint rules instead of trusting the submitted gait value.
- Maximum permitted displacement is `speedLimit * elapsedTime + 0.35 m` of latency/jitter grace.
- Sweep the player capsule from the last accepted position toward the candidate position against the static environment collision layer. Reject samples that cross blocking geometry.
- Reject incompatible states such as sprinting while crouched, sprinting without forward movement, aiming while sprinting, or firing while sprinting.
- The host updates its representation to the accepted pose and broadcasts `ProxyPresentationState` at 20 Hz.

### Interpolation and correction

- Remote proxies render approximately 100 ms behind the latest accepted host state.
- Interpolate position, body yaw, aim yaw, aim pitch, velocity, and locomotion parameters between buffered states.
- Extrapolate for no more than 100 ms, then hold the latest state until another sample arrives.
- If the owner diverges by 0.75-2 m, send a reliable correction and smooth it over 100 ms.
- If divergence exceeds 2 m, the position is non-finite, or the player leaves world bounds, hard-snap the owner and clear its interpolation history.
- This milestone does not implement movement rollback/replay; host correction is positional reconciliation only.
- When a client joins late, send one reliable current presentation snapshot for every existing player before normal unreliable updates continue.

### Tuning

Store send rate, interpolation delay, extrapolation cap, correction thresholds, movement validation grace, world bounds, and rewind duration in a project-owned `MultiplayerTuningSettings` ScriptableObject. The values above are the initial defaults and must not be scattered as hard-coded constants.

## 4. Authoritative Weapon State

### Stable catalog

Create `NetworkWeaponCatalog` with stable IDs:

| ID | Weapon | Magazine | Supported modes | Network ballistics |
|---:|---|---:|---|---|
| 1 | TR15 | 32 | Semi, 3-round burst, Auto | Hitscan |
| 2 | WK-11 Viper | 26 | Semi | Hitscan |
| 3 | Herrington 11-87 Police | 12 | Semi | 8-pellet buckshot |
| 4 | Mk14 EBR | 20 | Semi, Auto | Hitscan |

- Use Tactical settings as the source for presentation assets, cadence, magazine capacity, and supported modes.
- Store authoritative damage, range, ADS/hip spread, layer masks, impact library, and tracer settings in the catalog's project-owned ballistics entries.
- Do not reuse the unrelated legacy CAS pistol, SMG, or sniper ballistics as these weapons' server configuration.
- Validate catalog IDs, prefab references, capacities, allowed fire modes, and ballistics in `OnValidate()` and Edit Mode tests.

### Persistent state

Server-written, everyone-readable state includes:

- `NetworkVariable<ushort> EquippedWeaponId`
- `NetworkVariable<FireMode> ActiveFireMode`
- `NetworkVariable<ReloadState> ActiveReloadState`
- `NetworkVariable<PlayerLifeState> LifeState`
- `NetworkList<WeaponAmmoState>` with one entry per catalog weapon

Apply all current values during `OnNetworkSpawn()` before subscribing to change callbacks so late joiners immediately see the correct weapon, ammunition, mode, reload state, health, and life state.

### ID-based presentation

- Never synchronize next/previous weapon callbacks.
- Add `NetworkTacticalShooterPlayer.ApplyEquippedWeapon(ushort weaponId)` to select the mapped Tactical weapon directly.
- The owner sends a requested weapon ID; the host validates it and writes `EquippedWeaponId`; every client applies that accepted ID.
- Keep CAS's hidden gameplay item and Tactical's visible weapon mapped through the same catalog entry.
- Respawning resets all four ammunition entries and equips weapon ID 1 unless a later game-mode rule overrides it.

## 5. Shot Routing and Presentation

### Eliminate the local damage path

Add `IWeaponShotRouter` at the player root and route the existing `WeaponProp.SubmitCombatShot()` through it:

- Offline player: `OfflineWeaponShotRouter` forwards to the existing local `WeaponCombatRuntime`.
- Network owner: `NetworkWeaponShotRouter` converts the local request into a `NetworkShotCommand`; it never applies local damage.
- Host: validates the command, reconstructs a trusted `WeaponShotRequest` from the catalog and accepted player pose, and invokes authoritative resolution exactly once.
- Remote proxy: cannot submit shots.

Refactor `WeaponCombatRuntime` into two explicit operations:

- `ResolveAuthoritativeShot(...)` performs spread, obstruction, hit resolution, and damage without requiring a local Camera component.
- `PlayShotResult(...)` plays tracers, impacts, and decals without applying damage.
- Preserve the existing offline `SubmitShot(...)` facade by having it call both operations locally.

### Shot command and validation

`NetworkShotCommand : INetworkSerializable` contains:

- Weapon ID, monotonically increasing shot sequence, and synchronized client tick.
- Aim yaw/pitch or normalized aim direction.
- It contains no ScriptableObjects, prefab references, GameObjects, damage values, ammunition, or client-selected hit results.

The host validates:

- Sender owns the player and the sequence is newer than the last accepted command.
- Player is alive and the requested weapon is currently equipped.
- Player is not sprinting, reloading, or otherwise action-locked.
- Fire mode is allowed, cadence has elapsed, and authoritative ammunition is available.
- Aim direction is within a configured tolerance of the accepted player aim.
- Shot tick is not in the future and is no more than 250 ms old.

After acceptance, the host decrements ammunition, records cadence, resolves the shot, and broadcasts `NetworkShotResult`. Rejected commands receive a reliable state correction but no shot event.

### Deterministic spread and shotgun behavior

- Generate spread from a host-owned deterministic seed derived from shooter ID, weapon ID, and accepted shot sequence.
- Use a local deterministic PRNG; never call `UnityEngine.Random.InitState` or otherwise mutate Unity's global random state.
- TR15, Viper, and Mk14 resolve one hitscan ray per accepted shot.
- Herrington Police consumes one shell and resolves eight pellets using one deterministic cone pattern.
- Initial Police values are eight pellets, 12 damage per pellet, 50 m maximum range, and a 4-degree hip-fire cone; ADS spread is taken from the catalog and must be smaller than hip spread.
- A single pellet damages a target at most once, while separate pellets may each apply damage. Total close-range damage is therefore capped at 96 before any later armor or falloff system.
- `NetworkShotResult` supports multiple endpoints/impacts through a fixed-capacity collection sized for the eight-pellet shotgun.

### Presentation ownership and deduplication

- Networked Tactical weapons use project-owned subclasses implementing `INetworkTacticalWeaponPresentation`; they expose presentation-only fire, reload, ammo, and fire-mode methods without independently scheduling fire or deciding ammunition.
- Do not call `TacticalShooterWeapon.StartFiring()` as the authoritative network fire loop because it mutates local ammo and uses `Invoke` for cadence.
- On the owning client, an accepted input may immediately predict one recoil response, CAS camera shake, Tactical fire animation, muzzle flash, casing, and fire sound.
- Cache predicted presentation by shot sequence.
- When confirmation for that sequence arrives, the owner/host does not replay recoil, camera shake, muzzle flash, casing, animation, or fire audio. It reconciles ammunition and plays authoritative tracer/impact results.
- Non-owning clients play Tactical third-person fire presentation plus tracer/impact results, without local camera shake or recoil input.
- CAS's shared recoil response runs once per local predicted shot. Tactical presentation methods must not invoke a second shared recoil or camera-shake path.
- If the host rejects a predicted shot, correct ammunition/state and stop any continuing automatic-fire presentation; already-played single-frame muzzle/audio effects are not rolled back.

## 6. Lag-Compensated Hitscan

- Maintain 250 ms of host-side accepted pose history for every living player.
- Record position, body yaw, capsule center, height, radius, and crouch state using the host's accepted movement timeline.
- For each accepted shot, map the client tick to host network time, interpolate every potential target's capsule at that time, and test the authoritative ray or pellet rays against those historical capsules.
- Resolve environment obstruction against the current host physics world. A historical player hit is valid only when it is closer than the current-time environment obstruction along that ray.
- Use analytical ray-versus-capsule tests rather than moving live `CharacterController` objects during rewind.
- The first milestone has one body damage zone and no headshot multiplier.
- Reject shots older than the 250 ms history window rather than clamping them to the oldest pose.
- Projectile rewind remains deferred.

## 7. Health, Death, and Respawn

- Add `NetworkHealth : NetworkBehaviour, IDamageable` with 100 maximum health.
- Only the host can apply `DamageInfo` and write health/life state.
- Ignore self-damage for this milestone; teams and friendly-fire rules remain deferred.
- On death:
  - Cancel firing and reloading.
  - Set simulation mode to `Disabled` for the owner.
  - Disable collision and hit registration.
  - Disable or hide the live Tactical/CAS presentation consistently on every client.
- Respawn after three seconds at an authored `NetworkSpawnPoint` that is clear and preferably distant from living players.
- Respawn resets transform, interpolation buffers, health, movement state, all weapon ammunition, fire mode, reload state, and predicted-shot caches.
- Re-enable collision and the appropriate owner/proxy simulation mode only after the reset state has been applied.

## 8. Sessions, Relay, and Performance

- Prove the gameplay vertical slice first with `DirectNetworkSessionBootstrap` and Multiplayer Play Mode.
- After the vertical slice passes, install Multiplayer Services and implement `UnityServicesSessionBootstrap` with anonymous authentication, a host-created Session using Relay, and join-by-code.
- Keep matchmaking, parties, reconnect recovery, host migration, teams, and dedicated servers out of this milestone.
- Profile eight simultaneous hybrid players.
- Add an animation LOD:
  - Close and visible: full CAS source animation, bridge retargeting, Tactical procedural animation, and foot IK.
  - Distant or offscreen: reduce procedural/foot-IK update frequency while preserving root locomotion, weapon state, and hit registration.
- Do not optimize away the hidden CAS source rig until profiling proves it necessary.

## Public Interfaces and Types

- `MultiplayerTuningSettings`
- `OwnerMotionSample : INetworkSerializable`
- `ProxyPresentationState : INetworkSerializable`
- `NetworkFPSExampleController`
  - `SetSimulationMode(PlayerSimulationMode mode)`
  - `CaptureOwnerMotionSample(...)`
  - `ApplyRemotePresentationState(...)`
- `NetworkCasPlayer : NetworkBehaviour`
- `INetworkSessionBootstrap`
- `NetworkWeaponCatalog`
- `WeaponAmmoState : INetworkSerializable`
- `NetworkShotCommand : INetworkSerializable`
- `NetworkShotResult : INetworkSerializable`
- `IWeaponShotRouter`
- `INetworkTacticalWeaponPresentation`
- `NetworkTacticalShooterPlayer.ApplyEquippedWeapon(ushort weaponId)`
- `NetworkHealth : NetworkBehaviour, IDamageable`
- `NetworkHitboxHistory`

## Implementation Order

> Progress tracker. Steps marked **[DONE]** are complete; **[IN PROGRESS]** are active; **[PENDING]** remain. Notes record concrete file paths, GUIDs, and decisions made during implementation.

### 1. [DONE] Install NGO/Transport/testing packages and add the core assembly without changing gameplay.

**Packages installed** (Unity 6000.5.4f1):

- `com.unity.netcode.gameobjects` 2.13.1
- `com.unity.transport` 6.5.0 (transitive via NGO; not added to manifest explicitly per plan — "without altering unrelated package entries")
- `com.unity.multiplayer.playmode` 2.0.2
- `com.unity.multiplayer.tools` 2.2.10

`com.unity.services.multiplayer` is deliberately **not** installed yet (deferred until after the vertical slice, per Section 8).

`Packages/manifest.json` and `Packages/packages-lock.json` were updated by Unity Package Manager; no unrelated entries were modified.

**Core assembly created:**
- `Assets/FPSProject/Multiplayer/Core/FPSProject.Multiplayer.Core.asmdef`
  - rootNamespace: `FPSProject.Multiplayer.Core`
  - references: `FPSProject.Combat.Runtime` (GUID `b805ca460311543e89ca46007f995c22`), `Unity.Netcode.Runtime`, `Unity.Networking.Transport` (required transitively because `UnityTransport.ConnectionData` references `NetworkEndpoint`).
  - `autoReferenced: true`, no platform restrictions, no unsafe code.

**KINEMATION adapter location:** Per plan, adapters that reference `Assembly-CSharp` types (CAS controller, Tactical player, bridge) will live under `Assets/Integrations/KINEMATION/Multiplayer` in `Assembly-CSharp` (no asmdef). Third-party CAS/Tactical folders remain untouched.

**Compiles clean — 0 errors.**

### 2. [DONE] Add direct host/client bootstrap and spawn a minimal network object.

**Bootstrap contracts and implementations** (all in core assembly):
- `Assets/FPSProject/Multiplayer/Core/Bootstrap/INetworkSessionBootstrap.cs` — interface with `IsStarted`, `StartHost()`, `StartClient()`, `Stop()`.
- `Assets/FPSProject/Multiplayer/Core/Bootstrap/DirectNetworkSessionBootstrap.cs` — `MonoBehaviour, INetworkSessionBootstrap`. Uses `UnityTransport.SetConnectionData(address, port)`. Resolves `NetworkManager` on same GameObject if not serialized.
- `Assets/FPSProject/Multiplayer/Core/Bootstrap/DevSessionLauncher.cs` — dev-only keyboard driver (H=host, C=client, X=stop).

**Minimal test prefab:**
- `Assets/FPSProject/Multiplayer/Core/Test/MinimalTestCube.cs` — `NetworkBehaviour` that colors the cube green for owner, red for remote, and randomizes host spawn position.
- `Assets/FPSProject/Multiplayer/Prefabs/MinimalNetworkTestCube.prefab` — Cube + `NetworkObject` + `MinimalTestCube`.

**Test scene:**
- `Assets/Scenes/MultiplayerTest.unity` — contains:
  - `NetworkManager` GO with `NetworkManager` + `UnityTransport` + `DirectNetworkSessionBootstrap` + `DevSessionLauncher`. `NetworkConfig.NetworkTransport` wired to the transport; `MinimalNetworkTestCube` registered as both a network prefab and `PlayerPrefab`.
  - `Ground` plane, `DirectionalLight` (directional).

**Smoke test passed:** Entered play mode, called `DirectNetworkSessionBootstrap.StartHost()`, confirmed `IsListening=True`, `IsHost=True`, and `MinimalTestCube` spawned (CubeCount=1). Exited play mode cleanly.

### 3. [DONE] Build the multiplayer prefab variant and prove owner-only input/camera initialization.

**Adapter scripts (Assembly-CSharp, under `Assets/Integrations/KINEMATION/Multiplayer/`):**

- `NetworkFPSExampleController.cs` — derives from `FPSExampleController` (CAS_Demo.Scripts.FPS). Overrides `Awake()` to cache `PlayerInput` without activating it, and `Start()` to no-op (the network spawn path drives initialization via `InitializeAsOwner()` / `InitializeAsProxy()`). Adds three simulation modes via `SetSimulationMode(PlayerSimulationMode)`:
  - `LocalOwner`: runs the existing CAS `Update()` path unchanged.
  - `RemoteProxy`: skips CAS grounding, physics, movement, look input, and camera control; applies accepted presentation state.
  - `Disabled`: no input, motor, or proxy animation work.
  - `CaptureOwnerMotionSample(uint, int)` builds an `OwnerMotionSample` from the live CAS motor state.
  - `ApplyRemotePresentationState(SampledState)` writes animator parameters directly, detects movement/crouch/jump/landing/aim state edges so CAS procedural transition modifiers fire once, and feeds pitch to `CharacterCamera` for the bridge.
  - Caches and toggles owner-only components: `PlayerInput`, `Camera`, `AudioListener`, `RecoilAnimation`. `CharacterController` is disabled on proxies.

- `NetworkCasPlayer.cs` — `NetworkBehaviour` on the prefab root. Owns player lifecycle, the owner-to-host motion submission `ServerRpc`, the host-to-proxy broadcast `ClientRpc`, and the per-client interpolation/correction loop.
  - `OnNetworkSpawn()`: calls `InitializeAsOwner()` or `InitializeAsProxy()` on the controller, enables the Tactical presentation components in the correct order, and initializes the `ProxyInterpolationBuffer` for remote instances.
  - Owner path: submits `OwnerMotionSample` at 20 Hz via unreliable `SubmitMotionSampleServerRpc`.
  - Host path: `HostMotionValidator.Validate()` checks stale sequences, non-finite values, world bounds, speed/displacement, blocking-geometry capsule sweep, and incompatible state combinations (sprint+crouch, sprint without forward, aiming while sprinting). Accepted poses are recorded and broadcast at 20 Hz. Rejected samples trigger soft (smooth) or hard (snap) corrections.
  - Proxy path: `ProxyInterpolationBuffer.Sample()` interpolates 100 ms behind the latest accepted state, extrapolates up to 100 ms, then holds. The sampled state is applied via `ApplyRemotePresentationState`.
  - Late-join: `SendLateJoinSnapshot(ulong)` sends one reliable current snapshot per existing player to a new client.
  - `NotifyDeath()` / `NotifyRespawn(Vector3)` hooks for the future health system.

**Prefab builder (editor tool, under `Assets/Integrations/KINEMATION/Multiplayer/Editor/`):**

- `MultiplayerPrefabBuilder.cs` — `Tools/FPSProject/Build Multiplayer Player Prefab` menu item. Instantiates the offline `CAS_Player_Example_FPS_Tactical.prefab`, replaces `FPSExampleController` with `NetworkFPSExampleController` (preserving serialized field values), adds `NetworkObject` and `NetworkCasPlayer`, wires the controller reference, and disables owner-only components by default (`PlayerInput`, `Camera`, `AudioListener`, `RecoilAnimation`, `TacticalShooterPlayer`, `TacticalProceduralAnimation`). The Tactical components are disabled to avoid a component-order race where `TacticalShooterPlayer.Start` calls `EquipWeapon` before `TacticalProceduralAnimation.Start` has resolved its Animator; `NetworkCasPlayer.EnableTacticalPresentation()` enables them in the correct order during `OnNetworkSpawn`.

**Prefab:**
- `Assets/FPSProject/Multiplayer/Prefabs/CAS_Player_Network.prefab` — prefab variant of the offline CAS/Tactical player. Root components: `Transform`, `CharacterController`, `PlayerInput` (disabled), `MotionWarpingComponent`, `RecoilAnimation` (disabled), `ClimbComponent`, `VaultComponent`, `CharacterTrajectory`, `WeaponCombatRuntime`, `CasTacticalPlayerBridge`, `NetworkFPSExampleController`, `NetworkObject`, `NetworkCasPlayer`. Camera child: `Camera` (disabled), `AudioListener` (disabled), `CharacterCamera`. Tactical Presentation child: `TacticalShooterPlayer` (disabled), `TacticalProceduralAnimation` (disabled), `PlayerInput` (disabled), nested `FPCamera` with `Camera` (disabled) + `AudioListener` (disabled).

**Tuning asset:**
- `Assets/FPSProject/Multiplayer/Core/MultiplayerTuningSettings.asset` — `MultiplayerTuningSettings` ScriptableObject with plan defaults (20 Hz send rate, 100 ms interpolation delay, 100 ms extrapolation cap, 0.75 m soft / 2 m hard correction, 0.35 m validation grace, 500x200x500 world bounds, 250 ms rewind). Assigned to `NetworkCasPlayer.tuning` on the prefab.

**NetworkManager scene wiring:**
- `Assets/Scenes/MultiplayerTest.unity` — `NetworkManager.NetworkConfig.PlayerPrefab` set to `CAS_Player_Network`. Prefab registered in `NetworkConfig.Prefabs.NetworkPrefabsLists[0]`.

**Smoke test passed:** Entered play mode, started host via `DirectNetworkSessionBootstrap.StartHost()`. Confirmed: `IsHost=True`, `IsSpawned=True`, `IsOwner=True`, `SimulationMode=LocalOwner`, `IsOwnerInitialized=True`. All owner-only components enabled (`PlayerInput`, `Camera`, `AudioListener`, `RecoilAnimation`, `CharacterController`). Tactical presentation enabled with weapon equipped (`CAS_Rifle` / `SKM_WK-11_Viper_Body`). Tactical's own `PlayerInput` correctly disabled. Zero errors, zero warnings. Exited play mode cleanly.

### 4. [DONE] Implement owner movement submission, host validation, remote interpolation, and corrections.

**Core data contracts (in `Assets/FPSProject/Multiplayer/Core/Movement/`, core assembly):**

- `PlayerSimulationMode.cs` — enum: `LocalOwner`, `RemoteProxy`, `Disabled`.
- `OwnerMotionSample.cs` — `INetworkSerializable` struct: sequence, network tick, position, velocity, body yaw, aim yaw, aim pitch, MoveX/Y, gait, grounded/in-air/crouch/sprint/aiming/moving flags. No health, weapon, or ammunition state.
- `ProxyPresentationState.cs` — `INetworkSerializable` struct: host-accepted motion fields plus `IsAlive` for death/respawn presentation. Transient locomotion state; persistent weapon/life state uses NetworkVariables (step 6).
- `MultiplayerTuningSettings.cs` — `ScriptableObject` with send rate, interpolation delay, extrapolation cap, correction thresholds, validation grace, speed limits and multipliers, world bounds, rewind duration. `IsInsideWorldBounds(Vector3)` helper.

**Movement synchronization (core assembly):**

- `ProxyInterpolationBuffer.cs` — time-indexed snapshot buffer. `Add(state, localTime)` maintains ascending order and prunes old snapshots. `Sample(renderTime, out SampledState)` interpolates between the two snapshots bracketing `renderTime - interpolationDelay`, extrapolates up to `extrapolationCap` seconds (position += velocity * elapsed), then holds the last state. `Clear(ClearReason)` for hard corrections, respawns, and late joins.
- `HostMotionValidator.cs` — static validator. `Validate(sample, context)` checks: non-finite values, world bounds, state consistency (sprint+crouch, sprint without forward, aiming while sprinting), speed/displacement (`speedLimit * elapsed + grace`), and blocking-geometry capsule sweep (`Physics.CapsuleCast` against the static environment layer). Returns `ValidationResult` with `RejectReason` and debug message.

**Network plumbing (Assembly-CSharp adapter):**

- `NetworkCasPlayer` owner path: `OwnerUpdate()` accumulates time and submits `OwnerMotionSample` at 20 Hz via `SubmitMotionSampleServerRpc` (unreliable, `RequireOwnership = true`).
- `NetworkCasPlayer` host path: `HostValidateAndApply()` validates each sample, updates `HostClientState`, and marks `HasPendingBroadcast`. `HostBroadcastUpdate()` sends `BroadcastProxyStateClientRpc` at 20 Hz. Rejections trigger `SendSoftCorrectionClientRpc` (smooth over `correctionSmoothDuration`) or `SendHardCorrectionClientRpc` (snap + buffer clear).
- `NetworkCasPlayer` proxy path: `ProxyUpdate()` samples the `ProxyInterpolationBuffer` and calls `controller.ApplyRemotePresentationState(sampled)`.
- Late-join: `SendLateJoinSnapshot(ulong)` sends a reliable `SendLateJoinSnapshotClientRpc` to the joining client.

### 5. [DONE] Implement the remote CAS presentation driver and verify the existing CAS/Tactical bridge remains visually correct.

**Remote presentation driver (in `NetworkFPSExampleController`):**

- `ApplyRemotePresentationState(SampledState)` writes the interpolated remote state to the hidden CAS source rig:
  - Sets `transform.position` and `transform.rotation` (body yaw only) directly — the `CharacterController` is disabled on proxies.
  - Feeds `CharacterCamera.pitchInput` / `yawInput` / `isAiming` / `isCrouching` so the bridge's `Update()` reads the correct pitch and gait for Tactical presentation.
  - Sets `_aimRotation`, `_lookInput.y`, `_isAiming`, `_isCrouching`, `_isInAir`, `_moveInput`, `_gait`, `_animatorGait` so the base class fields reflect the replicated state.
  - Edge detection (with `_proxyFirstApply` reset on respawn/hard correction): crouch transition fires `stepCrouch`/`stepUncrouch` modifiers; jump trigger fires `Animator_Jumped` when grounded→in-air; moving state edge fires `startMoving`/`stopMoving` modifiers.
  - Writes animator parameters directly: `MoveX`, `MoveY`, `Gait`, `ViewWeight`, `AimingWeight`, `IsFirstPerson` (always false on proxies), `IsInAir`, `IsMoving`. Uses `KMath.ExpDecayAlpha` for smooth move-parameter interpolation, matching the base controller's smoothing.
- `ResetProxyState()` clears edge-detection state for respawn and hard correction.

**Bridge verification:** The `CasTacticalPlayerBridge` remains presentation-only. Its `Update()` reads `casCamera.pitchInput` and `casController.Gait`/`IsSprinting`/`IsAiming`/`IsCrouching` — all of which are now driven by `ApplyRemotePresentationState` on proxies. Its `LateUpdate()` retargets lower-body bone rotations and the crouch height offset from the CAS source rig onto the Tactical presentation rig, exactly as in the offline rig. No bridge source was modified. The bridge continues to receive presentation-only data through the same fields; it never calculates movement or look direction.

### 6. [DONE] Add stable weapon catalog IDs, persistent equipped/ammo/fire-mode state, and ID-based presentation.

**Core weapon state types (in `Assets/FPSProject/Multiplayer/Core/Weapons/`, core assembly):**

- `ReloadState.cs` — enum: `None`, `Reloading`.
- `PlayerLifeState.cs` — enum: `Alive`, `Dead`, `Respawning`.
- `WeaponAmmoState.cs` — `INetworkSerializable, IEquatable<WeaponAmmoState>`: WeaponId, CurrentAmmo, Capacity.
- `NetworkWeaponCatalog.cs` — `ScriptableObject` with `List<NetworkWeaponEntry>`. Each entry has stable `weaponId`, `displayName`, `tacticalWeaponPrefab`, `magazineCapacity`, `fireRateRpm`, `supportsSemi/Burst/Auto`, `burstRounds`, `isShotgun`, `pelletCount`, and `NetworkWeaponBallistics` (damage, maxRange, hitMask, hip/ads spread, tracer, impact library). `OnValidate()` rejects duplicate/zero IDs, bad capacities, no fire modes, ADS > hip spread. `TryGetEntry`, `IndexOf`, `Contains`, `AddEntry` (test-only), `ClearEntries` (test-only).
- `NetworkWeaponBallistics` — serializable class: damage, maxRange, hitMask, triggerInteraction, hipSpreadDegrees, adsSpreadDegrees, tracerPrefab/speed/lifetime, impactEffectLibrary.

**Catalog asset:** `Assets/FPSProject/Multiplayer/Core/Resources/NetworkWeaponCatalog.asset` — 4 entries:
- ID 1 TR15: 32 mag, 600 RPM, Semi/Burst/Auto, hitscan, 25 dmg, 100 m, 1.5°/0.1° spread.
- ID 2 WK-11 Viper: 26 mag, 450 RPM, Semi, hitscan, 30 dmg, 100 m, 1.2°/0.08°.
- ID 3 Herrington 11-87 Police: 12 mag, 90 RPM, Semi, shotgun 8 pellets, 12 dmg/pellet, 50 m, 4°/2.5°.
- ID 4 Mk14 EBR: 20 mag, 700 RPM, Semi/Auto, hitscan, 35 dmg, 120 m, 1.4°/0.1°.

**Tactical Presentation adapters (Assembly-CSharp, under `Assets/Integrations/KINEMATION/Multiplayer/`):**

- `INetworkTacticalWeaponPresentation.cs` — interface: `PlayNetworkFirePresentation`, `PlayNetworkReloadPresentation`, `PlayNetworkReloadEndPresentation`, `SetNetworkAmmo`, `SetNetworkFireMode`, `StopNetworkFiring`. Presentation-only; no ammo mutation or cadence scheduling.
- `NetworkTacticalShooterPlayer.cs` — derives from `TacticalShooterPlayer`. `InitializeNetwork(catalog)` builds weapon-ID-to-Tactical-index map. `ApplyEquippedWeapon(ushort)` equips by index, bypassing next/previous cycling. `GetActiveNetworkWeaponPresentation()` returns the equipped weapon's `INetworkTacticalWeaponPresentation`.
- `NetworkTacticalShooterWeapon.cs` — derives from `TacticalShooterWeapon`, implements `INetworkTacticalWeaponPresentation`. Fire/reload presentation mirrors the vendor path but stops before ammo decrement and `Invoke` cadence.
- `NetworkTacticalShotgun.cs` — derives from `TacticalShotgun`, implements `INetworkTacticalWeaponPresentation`. Per-shell reload loop presentation driven by host-authoritative reload state.

**Networked weapon prefab variants (in `Assets/FPSProject/Multiplayer/Prefabs/Weapons/`):**

- `W_TR15_Network.prefab`, `W_WK-11_Viper_Network.prefab`, `W_Herrington_11-87_Police_Network.prefab`, `W_Mk14EBR_Network.prefab` — prefab variants of the vendor weapons with the script component swapped to the networked subclass. Serialized fields preserved.

**Persistent state (Assembly-CSharp adapter):**

- `NetworkWeaponState.cs` — `NetworkBehaviour` on the player root. `NetworkVariable<ushort> EquippedWeaponId` (default 1), `NetworkVariable<FireMode> ActiveFireMode` (Semi), `NetworkVariable<ReloadState> ActiveReloadState` (None), `NetworkVariable<PlayerLifeState> LifeState` (Alive), `NetworkList<WeaponAmmoState> AmmoState` (one per catalog entry, in catalog order). All Server-write, Everyone-read. `OnNetworkSpawn` initializes the server list, applies current state to presentation, then subscribes to change callbacks. Server-side helpers: `ServerSetEquippedWeapon`, `ServerSetFireMode`, `ServerSetReloadState`, `ServerSetLifeState`, `ServerDecrementAmmo`, `ServerRefillAmmo`, `ServerRefillAllAmmo`, `ServerCheckAndRecordCadence`, `ServerResetForRespawn`. Client read helpers: `GetEquippedAmmo`, `GetEquippedCapacity`, `GetAmmoFor`.

**Player prefab wiring (`MultiplayerWeaponStateBuilder.cs` editor tool, `Tools/FPSProject/Build Multiplayer Weapon State (Step 6)`):**

- Replaces vendor `TacticalShooterPlayer` with `NetworkTacticalShooterPlayer` (serialized fields preserved).
- Swaps the `weaponPrefabs` array entries that have networked variants.
- Adds `NetworkWeaponState` to the root, wires the catalog reference.
- Wires `NetworkCasPlayer.tacticalPlayer` and `NetworkCasPlayer.weaponState`.

**NetworkCasPlayer weapon state integration:**

- `OnNetworkSpawn` resolves `tacticalPlayer` and `weaponState`, subscribes to `EquippedWeaponId`, `ActiveFireMode`, `ActiveReloadState`, `LifeState`, and `AmmoState` change callbacks.
- `InitializeTacticalPresentation` coroutine calls `tacticalPlayer.InitializeNetwork(catalog)` after the vendor Start populates the weapon list, then applies the current equipped weapon, ammo, and fire mode so late joiners see the correct state.
- Change handlers drive ID-based Tactical presentation: `ApplyEquippedWeaponPresentation`, `ApplyCurrentAmmoPresentation`, `ApplyCurrentFireModePresentation`.

**Tests:** `NetworkWeaponCatalogTests` (10 tests) — catalog lookup, ID uniqueness, capacity/fire-mode defaults, Resources asset has 4 plan weapons with correct defaults, ADS spread <= hip spread, `WeaponAmmoState` equality/hash. All 65 EditMode tests pass.

### 7. [DONE] Add the offline/network/server shot router and prove damage can execute only on the host.

**Core contracts (in `Assets/FPSProject/Multiplayer/Core/Weapons/`):**

- `IWeaponShotRouter.cs` — interface with `SubmitShot(in WeaponShotRequest, weaponId, shotSequence, networkTick, aimYaw, aimPitch, isAiming)`. Routes a local shot from `WeaponProp.SubmitCombatShot()` based on network role.
- `NetworkShotCommand.cs` — `INetworkSerializable`: WeaponId, ShotSequence, NetworkTick, AimYaw, AimPitch, AimDirection (normalized), IsAiming. No damage/ammo/prefab/hit fields.
- `NetworkShotResult.cs` — `INetworkSerializable`: WeaponId, ShotSequence, ShooterClientId, MuzzlePosition, ImpactCount, and 8 fixed `NetworkShotImpact` fields (Impact0..Impact7). `NetworkShotImpact` has Point, Normal, HitTargetNetworkId, IsPlayerHit. Capacity = 8.

**WeaponCombatRuntime refactor (`Assets/FPSProject/Combat/Runtime/WeaponCombatRuntime.cs`):**

- `SubmitShot(in WeaponShotRequest)` remains as the offline facade, now calling `ResolveAuthoritativeShot` then `PlayShotResult`.
- `ResolveAuthoritativeShot(in WeaponShotRequest)` returns `AuthoritativeShotResult` (muzzle, endpoint, hasHit, hit) and applies damage exactly once via `ResolveContact`. Does not require a local Camera; the caller supplies camera origin/direction.
- `PlayShotResult(in WeaponShotRequest, in AuthoritativeShotResult)` spawns tracers and impacts without applying damage. Call this on every client for presentation.
- `ResolveHitscanRay(in WeaponShotRequest, cameraOrigin, spreadDirection, muzzlePosition)` — single-ray resolution with caller-supplied spread direction, for the deterministic spread path.

**Shot routers (Assembly-CSharp adapter):**

- `OfflineWeaponShotRouter.cs` — `MonoBehaviour, IWeaponShotRouter`. Forwards to `WeaponCombatRuntime.SubmitShot` locally. Preserves offline behavior.
- `NetworkWeaponShotRouter.cs` — `MonoBehaviour, IWeaponShotRouter`. Owner-side: increments shot sequence, plays predicted one-frame presentation, caches it by sequence, sends `NetworkShotCommand` via `SubmitShotServerRpc` (unreliable, RequireOwnership). Never applies local damage. `OnShotResult` dedupes predicted presentation on the owner and plays third-person fire + tracer/impact on non-owners. `ClearPredictedShots` for respawn.
- `NetworkWeaponPropBridge.cs` — `NetworkBehaviour`. Owner-only. Pushes the current equipped weapon ID, network tick, and aim flag into every CAS `WeaponProp` every frame so `SubmitCombatShot` carries the correct values to the router.

**WeaponProp routing (`Assets/CAS Demo/Scripts/FPS/WeaponProp.cs`):**

- Added `_shotRouter`, `_networkWeaponId`, `_networkTick`, `_isAimingForRouter` fields.
- `SetShotRouter`, `SetNetworkWeaponId`, `SetNetworkTick`, `SetNetworkAiming` public setters for the adapter.
- `SubmitCombatShot` now routes through `IWeaponShotRouter` when present, falling back to direct `WeaponCombatRuntime.SubmitShot` when no router is set (offline compatibility).

**Host-side authoritative resolution (`NetworkCasPlayer.HostResolveShot`):**

- Validates: sender owns this player, shot sequence is newer than last accepted, player is alive, requested weapon is currently equipped, weapon ID is in the catalog, cadence has elapsed, ammunition is available. Rejects stale/duplicate/empty.
- Decrements authoritative ammo, records cadence, reconstructs a trusted `WeaponShotRequest` from the catalog (not the owner's local CAS settings), and resolves via `WeaponCombatRuntime.ResolveHitscanRay`.
- Broadcasts `NetworkShotResult` via `BroadcastShotResultClientRpc` (unreliable).

**Player prefab:** Step 6 builder adds `NetworkWeaponShotRouter` and `NetworkWeaponPropBridge` to the root.

**Tests:** `NetworkShotCommandTests`/`NetworkShotResultTests` (5 tests) — FastBufferWriter/Reader round trips for all fields, 8-impact round trip, command does not carry damage/ammo/prefab fields, Capacity == 8.

### 8. [DONE] Add prediction deduplication, deterministic spread, and all four weapon behaviors including Police buckshot.

**Deterministic spread (`Assets/FPSProject/Multiplayer/Core/Weapons/DeterministicShotRandom.cs`):**

- `BuildSeed(shooterClientId, weaponId, shotSequence)` — xorshift-style 64-bit seed combine; never zero.
- `SpreadCone(shooterClientId, weaponId, shotSequence, pelletIndex, baseDirection, halfAngleDegrees)` — xorshift64 PRNG seeded per (shooter, weapon, sequence, pellet), uniform disk distribution within the cone. Never mutates `UnityEngine.Random` state.

**Host-side per-pellet resolution (`NetworkCasPlayer.HostResolveShot`):**

- For each pellet (1 for hitscan, 8 for the Police shotgun), compute the deterministic spread direction via `DeterministicShotRandom.SpreadCone`.
- Resolve each pellet via `WeaponCombatRuntime.ResolveHitscanRay` with the spread-adjusted direction.
- Shotgun rules: single-pellet-per-target (tracked via `_shotgunDamagedTargetsThisShot` HashSet<NetworkObjectId>), total close-range damage capped at 96 (8 * 12), separate pellets may each apply damage up to the cap.
- `NetworkShotResult` carries up to 8 `NetworkShotImpact` entries.

**Prediction deduplication (`NetworkWeaponShotRouter`):**

- `SubmitShot` plays predicted one-frame presentation (recoil, muzzle flash, casing, fire animation, fire audio) via `INetworkTacticalWeaponPresentation.PlayNetworkFirePresentation` on the owner immediately, and caches by shot sequence.
- `OnShotResult`: owner removes the prediction from the cache (no replay). Non-owners play third-person fire presentation plus tracer/impact. Every client plays the authoritative tracer/impact via `PlayShotResultPresentation`.
- CAS's shared recoil response runs once per local predicted shot on the owner. Tactical presentation methods do not invoke a second shared recoil or camera-shake path.

**Tests:** `DeterministicShotRandomTests` (11 tests) — seed determinism/difference, zero-angle returns normalized base, spread stays within cone, normalized output, does not mutate Unity Random state. All 65 EditMode tests pass.

### 9. [DONE] Add lag-compensated historical capsule hit testing.

**Hitbox history (`Assets/FPSProject/Multiplayer/Core/Weapons/NetworkHitboxHistory.cs`):**

- `HitboxPoseSample` struct: Time, Position, BodyYaw, CapsuleCenter, CapsuleHeight, CapsuleRadius, IsCrouching.
- `HistoricalCapsule` struct: Top, Bottom, Radius, Center, Height.
- `NetworkHitboxHistory` class: rolling buffer sized by `rewindDuration` (250 ms default). `Record(time, sample)` maintains ascending order and prunes. `TryGetCapsule(time, out capsule)` interpolates between bracketing samples, rejects times outside the window. `IsTimeInWindow` helper.
- `RaycastCapsule(rayOrigin, rayDirection, capsule, maxDistance, out hitDistance)` — analytical ray-vs-capsule intersection. Solves the 2x2 closest-approach system, clamps the axis parameter to the segment, computes the entry distance from the perpendicular distance and chord. Falls back to sphere intersection for a degenerate axis. No allocations, no live `CharacterController` movement.

**Host-side integration (`NetworkCasPlayer`):**

- `_hostHitboxHistories: Dictionary<ulong clientId, NetworkHitboxHistory>` — one per remote player, sized by `tuning.rewindDuration`.
- `RecordHitboxHistory(clientId, sample)` — called from `HostValidateAndApply` on every accepted motion sample. Records position, body yaw, capsule center/height/radius (crouch reduces height by `crouchSpeedMultiplier`).
- `TryGetHistoricalCapsule(targetClientId, hostTime, out capsule)` — interpolates a target's capsule at the given host time.
- `TryHitHistoricalPlayer(rayOrigin, rayDirection, maxDistance, hostTime, shooterClientId, out hitNetworkObjectId, out hitDistance, out hitPoint)` — tests the ray against every living player's historical capsule (excluding the shooter), returns the closest hit. Resolves environment obstruction against the current host physics world via `Physics.Raycast`; a historical player hit is valid only when closer than the current-time environment obstruction.
- `ClientTickToHostTime(clientTick)` — maps the owner's network tick to host network time using the tick delta and `NetworkConfig.TickRate`.
- `ApplyDamageToHistoricalTarget(networkObjectId, damage, hitPoint, travelDirection)` — looks up the spawned NetworkObject by ID and applies damage to its `IDamageable` exactly once.

**Shot resolution wiring (`HostResolveShot`):**

- Shot tick age validation: rejects shots in the future (with 50 ms tolerance) or older than `tuning.rewindDuration` (250 ms), rather than clamping to the oldest pose.
- Per pellet: first calls `TryHitHistoricalPlayer` at the mapped host time. If a player hit is found, applies damage to the historical target and records the impact. If no player hit, falls back to environment hitscan at current host time.

**Tests:** `NetworkHitboxHistoryTests` (14 tests) — empty history, record/retrieve, pruning, window rejection, interpolation, clear, analytical ray-vs-capsule hits (center/degenerate, cylinder, top/bottom sphere), miss when off-axis, max-distance respect, origin-inside returns zero. All 65 EditMode tests pass.

### 10. [PENDING] Add health, death, spawn selection, and respawn.

### 11. [PENDING] Run latency/loss and eight-player performance tests.

### 12. [PENDING] Add Sessions and Relay through the bootstrap interface.

## Test Plan and Acceptance Criteria

### Automated tests

- Serialization round trips for all network structs and fixed-capacity shotgun results.
- Sequence handling rejects stale, duplicate, future, and excessively old movement/shot commands.
- Movement validation rejects non-finite positions, impossible speed, blocked capsule sweeps, invalid sprint direction, crouch+sprint, and aim/fire while sprinting.
- Interpolation, extrapolation timeout, smooth correction, hard correction, and buffer reset behave deterministically.
- Core-assembly tests compile without referencing `Assembly-CSharp`; KINEMATION adapter tests use prefab behavior or reflection rather than illegal asmdef references.
- Remote instances never activate `PlayerInput`, rendered cameras, `AudioListener`, local recoil, or `CharacterController.Move()`.
- Offline shots resolve locally; network-owner shots never apply local damage; the host applies accepted damage exactly once.
- Predicted and confirmed events with the same shot sequence produce one owner recoil/muzzle/audio event.
- Catalog IDs are unique and all four entries have valid Tactical and ballistics references.
- Equipped weapon, ammo, fire mode, reload, health, and life state initialize correctly for late joiners.
- Semi, burst, and auto cadence/ammunition rules are host-authoritative.
- Police shots consume one shell, generate eight deterministic pellets, never exceed 96 raw damage, and preserve per-shell reload behavior.
- Historical capsule rewind hits a moving target at the submitted valid tick, respects environment obstruction, and rejects commands outside the 250 ms window.
- Death applies once and respawn resets all movement, combat, and presentation state.

### Play Mode scenarios

- Host plus one client: movement, turning, jumping, crouching, forward-only sprinting, hip fire, ADS, recoil, switching, reloading, death, and respawn.
- Confirm the remote body preserves the working CAS lower-body locomotion and Tactical upper-body/weapon alignment without a second controller.
- Confirm all four weapons align muzzle, tracer, impact, ammunition, and damage on host and client.
- Join a client after weapons have been switched and ammunition spent; it must immediately see the correct persistent state.
- Simulate 100-150 ms RTT, jitter, and 2-5% packet loss. Owners remain responsive, proxies remain stable, automatic fire does not duplicate, and rewind preserves moving-target hits within the 250 ms window.
- Run eight players using Multiplayer Play Mode and/or standalone clients and profile CPU, animation, bandwidth, and allocations.

### Completion gates

- CAS remains the only movement/input controller.
- `CasTacticalPlayerBridge` remains presentation-only.
- No client can apply authoritative damage or ammunition changes.
- Each accepted shot applies damage at most once.
- No bones or IK transforms are networked.
- Original non-networked CAS/Tactical prefabs remain functional.
- Compilation, Edit Mode tests, Play Mode tests, and normal multiplayer play complete without errors.

## Assumptions and Deferred Work

- The listen-server host is trusted for the first release.
- Owner-authoritative movement validation catches malformed input, wall crossing, and major divergence but is not competitive-grade anti-cheat.
- Hitscan player targets receive 250 ms of host rewind; world geometry and projectiles are not rewound.
- There are no headshots, armor, damage falloff, teams, or friendly-fire rules in this milestone.
- Matchmaking, parties, host migration, reconnect recovery, spectator mode, dedicated servers, and networked physical projectiles are deferred.
- Existing unrelated OneJS/menu and workspace changes must be preserved.
