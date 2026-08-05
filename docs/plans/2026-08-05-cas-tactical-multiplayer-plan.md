# CAS + Tactical Character Multiplayer Plan

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

1. Install NGO/Transport/testing packages and add the core assembly without changing gameplay.
2. Add direct host/client bootstrap and spawn a minimal network object.
3. Build the multiplayer prefab variant and prove owner-only input/camera initialization.
4. Implement owner movement submission, host validation, remote interpolation, and corrections.
5. Implement the remote CAS presentation driver and verify the existing CAS/Tactical bridge remains visually correct.
6. Add stable weapon catalog IDs, persistent equipped/ammo/fire-mode state, and ID-based presentation.
7. Add the offline/network/server shot router and prove damage can execute only on the host.
8. Add prediction deduplication, deterministic spread, and all four weapon behaviors including Police buckshot.
9. Add lag-compensated historical capsule hit testing.
10. Add health, death, spawn selection, and respawn.
11. Run latency/loss and eight-player performance tests.
12. Add Sessions and Relay through the bootstrap interface.

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
