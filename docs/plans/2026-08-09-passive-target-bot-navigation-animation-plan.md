# Passive Target Bot Navigation, Animation, and Hit Registration Plan

**Date:** 2026-08-09  
**Status:** Planned  
**Primary scene:** Assets/Scenes/OperationsDemoMultiplayer.unity  
**Bot prefab:** Assets/FPSProject/Multiplayer/Prefabs/PassiveTargetBot.prefab  
**Current bot behavior:** Assets/FPSProject/Multiplayer/Core/Match/PassiveTargetBot.cs

## Outcome

Make current passive multiplayer targets server-authoritative roaming bots:

- Existing Team Deathmatch roster spawn flow.
- Modest walk/jog; random valid destinations; stuck recovery.
- Smooth remote-client replication.
- KINEMATION full-body idle/walk/jog; no A-pose.
- Stable AK-style rifle-ready pose while idle/moving.
- Preserve existing server-owned health, damage, death, scoring, despawn, replacement.

Scope: exploration only. No targeting, aiming, shooting, cover, vaulting, or combat decisions.

## Acceptance Criteria

- TeamDeathmatchManager bots roam without input.
- Host/server alone selects destinations and moves NavMeshAgents.
- Remote clients see smooth movement/facing.
- Bots stay on active map; never enter hidden map.
- Floors, ramps, stairs, doorways traverse without permanent stalls.
- Brief destination pauses, then continued exploration.
- Reject invalid/partial paths before movement.
- Stuck bot retries/new destination within seconds.
- No normal-play A-pose.
- Idle/walk/jog match movement without obvious foot sliding.
- Visible rifle; right grip and left support hand aligned.
- Enemy shots reduce health; same-team shots remain blocked.
- Death awards score, despawns bot, replaces after existing delay.
- Replacements resume navigation/animation.
- No edits under Assets/KINEMATION or Assets/CAS Demo.

## Current Project Findings

### Spawn and lifecycle

- TeamDeathmatchManager owns count, server spawn, team, score, despawn, delayed replacement.
- SpawnBot instantiates PassiveTargetBot.prefab at NetworkSpawnPoint, calls NetworkObject.Spawn.
- PassiveTargetBot forwards server NetworkHealth death to TeamDeathmatchManager.
- Add navigation beside existing flow; do not replace it.

Relevant files:

- Assets/FPSProject/Multiplayer/Core/Match/TeamDeathmatchManager.cs
- Assets/FPSProject/Multiplayer/Core/Match/PassiveTargetBot.cs
- Assets/FPSProject/Multiplayer/Core/Match/MatchSpawnCatalog.cs

### Health and hit registration

- NetworkHealth: 100 health.
- Sole hit volume: root CharacterController capsule:
  - Height: 1.75 meters
  - Radius: 0.30 meters
  - Center Y: 0.87 meters
- No head/torso/limb hitboxes.
- WeaponCombatRuntime finds IDamageable on hit collider/parents; root capsule reaches root NetworkHealth.
- NetworkHealth blocks self/friendly damage. Same-team shots can resemble failed registration.
- Bots lack historical player rewind; use host live-physics fallback.

Relevant files:

- Assets/FPSProject/Multiplayer/Core/Health/NetworkHealth.cs
- Assets/FPSProject/Combat/Runtime/WeaponCombatRuntime.cs
- Assets/Integrations/KINEMATION/Multiplayer/NetworkCasPlayer.cs

### Navigation

- Unity AI Navigation 2.0.13 installed.
- OperationsDemoMultiplayer has no baked NavMesh data, NavMeshSurface, or NavMeshAgent.
- Existing Humanoid profile dimensions differ from bot capsule.
- Dust2 + Office share scene and switch through map visibility; need isolated navigation data/bounds.

Relevant files:

- Packages/manifest.json
- ProjectSettings/NavMeshAreas.asset
- Assets/Scenes/OperationsDemoMultiplayer.unity

### Animation

- PassiveTargetBot has full SKM_Operator skeleton + Animator.
- Animator uses AC_TacFPS_Character: first-person procedural presentation, not full NPC locomotion.
- Root motion disabled: correct for NavMeshAgent motion.
- Matching Operator-style Generic in-place KINEMATION clips:
  - A_Idle_Stand_Loop_IP
  - A_Locomotion_Stand_Walk_F_Loop_IP
  - A_Locomotion_Stand_Jog_F_Loop_IP
- Clips + SKM_Operator import Generic; likely no Humanoid Avatar conversion.
- Skeleton has weapon_r; W_AK105 exists.
- Locomotion clips lack explicit rifle-ready pose; need upper-body overlay.

Relevant assets:

- Assets/KINEMATION/Shared/Character/Animations/Generic/Idles/A_Idle_Stand_Loop_IP.fbx
- Assets/KINEMATION/Shared/Character/Animations/Generic/Walking/A_Locomotion_Stand_Walk_F_Loop_IP.fbx
- Assets/KINEMATION/Shared/Character/Animations/Generic/Jogging/A_Locomotion_Stand_Jog_F_Loop_IP.fbx
- Assets/KINEMATION/TacticalShooterPack/Prefabs/Weapons/W_AK105.prefab
- Assets/KINEMATION/TacticalShooterPack/Meshes/Character/Operator/SKM_Operator.fbx

### Network replication

- PassiveTargetBot has NetworkObject, no NetworkTransform.
- NetworkObject sync covers spawn state, not continuous movement.
- First pass: server-authoritative NetworkTransform.
- No NetworkAnimator needed; each peer derives animation speed from observed root motion.

## Architecture Decisions

1. **Server owns behavior.** Host/server samples destinations, calculates paths, enables NavMeshAgent.
2. **NavMeshAgent owns displacement.** Root motion stays disabled.
3. **One movement motor.** Replace CharacterController with non-trigger CapsuleCollider before adding NavMeshAgent.
4. **NetworkTransform replicates movement.** Clients interpolate server-owned root.
5. **Animation locally derived.** Host uses agent velocity; clients use interpolated transform delta.
6. **Project-owned Animator assets.** Bot controller/masks live outside vendor folders.
7. **Generic KINEMATION locomotion.** Use hierarchy-compatible Generic idle/walk/jog.
8. **Simple rifle-ready overlay.** Project-owned upper-body pose + transform mask; no full CAS/Tactical bridge.
9. **One damage capsule first.** Limb hitboxes wait until whole-body registration is stable.
10. **No bot rewind first pass.** Preserve live-host fallback unless multiplayer evidence shows bad misses.

## Proposed Runtime Components

### PassiveTargetBotNavigator

Suggested path:

Assets/FPSProject/Multiplayer/Core/Match/PassiveTargetBotNavigator.cs

Responsibilities:

- Resolve NetworkObject + NavMeshAgent.
- Navigate only when IsServer.
- Sample near spawn; safely warp onto valid polygon.
- Resolve active map BotExplorationArea.
- Sample random area candidates; enforce minimum distance.
- Require NavMeshPathStatus.PathComplete; avoid immediate repeats.
- Random pause on arrival.
- Recover from invalid path, off-mesh, no progress.
- Stop on death/despawn; replacements start naturally.

No health, scoring, animation, weapon, or presentation logic.

### PassiveTargetBotAnimationDriver

Suggested path:

Assets/FPSProject/Multiplayer/Core/Match/PassiveTargetBotAnimationDriver.cs

Responsibilities:

- Resolve Operator Animator.
- Calculate planar speed: server agent velocity; client root-transform delta.
- Smooth speed before Animator write; optional moving flag.
- Cache parameter hashes.
- Reset idle on disable/death/despawn.

No destination decisions or network-transform mutation.

### BotExplorationArea

Suggested path:

Assets/FPSProject/Multiplayer/Core/Match/BotExplorationArea.cs

Responsibilities:

- Map-local roaming bounds + random world candidates.
- Map identity; inactive with hidden map.
- Clear editor gizmos.

Optional authoring BoxCollider: trigger + Ignore Raycast, preventing weapon-ray/NavMesh geometry interference.

## Navigation Tuning Defaults

Starting values, not final balance:

| Setting | Initial value |
|---|---:|
| Agent radius | 0.30-0.35 m |
| Agent height | 1.75 m |
| Agent base offset | Tune against feet |
| Agent speed | 2.4 m/s |
| Agent acceleration | 6-8 m/s squared |
| Agent angular speed | 240-360 degrees/s |
| Agent stopping distance | 0.4-0.6 m |
| Minimum destination distance | 8 m |
| Maximum destination radius | 25-30 m |
| Arrival pause | 0.5-2.0 s |
| Candidate attempts | 8-12 |
| Stuck timeout | 2-3 s |
| Minimum progress before reset | 0.2-0.4 m |

## Implementation Sequence

Finish each stage before next. Update checkboxes + Progress Log as work lands.

### 0. Prove assumptions with two small spikes

- [ ] **0A - Damage baseline:** Host test enemy-team + friendly-team shots; record health.
- [ ] **0B - Raycast mask check:** Confirm all active ballistics masks hit bot layer.
- [ ] **0C - Generic rig spike:** Preview Generic idle/walk/jog on SKM_Operator.
- [ ] **0D - Bone binding check:** Verify pelvis, legs, spine, hands, weapon_r; no missing Generic paths.
- [ ] **0E - Record decision:** If binding fails, choose exact-rig fallback before production controller.

Gate:

- Enemy damage works or concrete failure identified.
- Friendly damage confirmed intentionally blocked.
- Three locomotion clips bind sufficiently.

### 1. Author map navigation data

- [ ] **1A - Create bot agent type:** Match bot capsule.
- [ ] **1B - Dust2 surface:** NavMeshSurface under Dust2 root; configure collection/geometry.
- [ ] **1C - Office surface:** NavMeshSurface under Office root; matching settings.
- [ ] **1D - Exploration areas:** One BotExplorationArea per map root.
- [ ] **1E - Bake both maps:** Non-empty NavMeshData for both.
- [ ] **1F - Visual QA:** Inspect stairs, ramps, doors, roofs, ledges, spawns, disconnected islands.
- [ ] **1G - Links:** Add only QA-proven NavMeshLinks.
- [ ] **1H - Map isolation:** Disabling root disables its navigation data + area.

Gate:

- Every spawn near correct NavMesh.
- Both maps have useful connected walkable regions.
- Active bots cannot select inactive map.

### 2. Prepare the network bot prefab

- [ ] **2A - Replace motor collider:** CharacterController -> equivalent non-trigger CapsuleCollider.
- [ ] **2B - Preserve damage path:** Root collider still resolves root NetworkHealth.
- [ ] **2C - Add NavMeshAgent:** Dedicated bot type + initial tuning.
- [ ] **2D - Add NetworkTransform:** Server authority, interpolation, practical thresholds.
- [ ] **2E - Add navigator:** Attach PassiveTargetBotNavigator.
- [ ] **2F - Add animation driver:** Attach PassiveTargetBotAnimationDriver.
- [ ] **2G - Prefab registration:** Preserve NetworkManager registration + compatible NetworkBehaviour order.
- [ ] **2H - Spawn smoke test:** Expected bots spawn without NGO errors.

Gate:

- Bot spawns; capsule raycastable; host/client layouts match.

### 3. Implement server-authoritative exploration

- [ ] **3A - Server gating:** Agent + destination loop only server-side.
- [ ] **3B - Spawn sampling:** Sample/warp onto active NavMesh.
- [ ] **3C - Candidate selection:** Random candidates inside active area.
- [ ] **3D - Path validation:** Reject invalid, partial, short, repeated paths.
- [ ] **3E - Arrival behavior:** Random pause, then new destination.
- [ ] **3F - Stuck recovery:** Detect no progress; reset/resample; log only repeated actionable failures.
- [ ] **3G - Lifecycle cleanup:** Stop when dead/despawning/not network-spawned.
- [ ] **3H - Replacement behavior:** Replacement explores without manager special cases.

Gate:

- Ten-minute roam without permanent stall.
- Clients never call SetDestination or move bot roots.

### 4. Replicate and smooth movement

- [ ] **4A - Root replication:** Server NetworkTransform publishes position/rotation.
- [ ] **4B - Client interpolation:** Smooth at project 30 Hz tick.
- [ ] **4C - Threshold tuning:** Reduce updates without stepping.
- [ ] **4D - Correction test:** Latency/jitter causes no excessive oscillation/snapping.
- [ ] **4E - Authority test:** Client proxy movement loses to server state.

Gate:

- Host/clients see same route/facing.
- No client NavMeshAgent fights interpolation.

### 5. Build the bot locomotion controller

- [ ] **5A - Create controller:** Create Assets/FPSProject/Multiplayer/Animations/AC_PassiveTargetBot.controller.
- [ ] **5B - Create parameter:** Smoothed planar Speed float.
- [ ] **5C - Base blend tree:** Generic idle/forward walk/forward jog.
- [ ] **5D - Disable root motion:** Animator root motion off.
- [ ] **5E - Assign controller:** Replace AC_TacFPS_Character only on PassiveTargetBot.prefab.
- [ ] **5F - Drive speed:** Server agent velocity; clients transform-derived velocity.
- [ ] **5G - Foot-speed tuning:** Tune thresholds/clip multipliers to agent speed.
- [ ] **5H - Lifecycle reset:** Safe idle before death/despawn removal.

Gate:

- No A-pose; transitions match root motion; no vendor controller edits.

### 6. Add rifle-ready presentation

- [ ] **6A - Mount rifle:** Visual-only W_AK105 under weapon_r.
- [ ] **6B - Strip behavior:** Disable/remove unnecessary gameplay, input, recoil, fire, casing, muzzle behavior.
- [ ] **6C - Tune socket:** Right-hand grip local position/rotation.
- [ ] **6D - Create pose:** Project-owned Generic upper-body rifle-ready pose.
- [ ] **6E - Create mask:** Generic transform mask for spine/shoulders/arms/hands; legs free.
- [ ] **6F - Add Animator layer:** Ready-pose overlay above locomotion.
- [ ] **6G - Left-hand alignment:** Key/constrain support hand to fore-end.
- [ ] **6H - Motion QA:** Rifle stable in idle/walk/jog/turns.

Gate:

- Both hands hold rifle; locomotion readable.
- No Tactical player input/camera/fire/full CasTacticalPlayerBridge.

### 7. Harden hit registration

- [ ] **7A - Enemy damage:** Server health reduction replicates.
- [ ] **7B - Friendly rule:** Same-team hits ignored.
- [ ] **7C - Moving host shot:** Hit moving bot from host.
- [ ] **7D - Moving remote shot:** Hit moving bot remotely under normal latency.
- [ ] **7E - Death flow:** Score/despawn/delayed replacement/navigation exactly once.
- [ ] **7F - Diagnose misses:** Separate timing, mask, collider, authority failures with evidence.
- [ ] **7G - Optional rewind decision:** Add bot rewind only if live-host fallback fails.
- [ ] **7H - Optional regional hitboxes:** Separate milestone; explicit multipliers/overlap rules.

Gate:

- Reliable whole-body hits; no duplicate damage; server-owned death/replacement.

### 8. Automated and manual verification

- [ ] **8A - Prefab validation:** Tests/editor checks for NetworkObject, NetworkHealth, CapsuleCollider, NavMeshAgent, NetworkTransform, navigator, animation driver.
- [ ] **8B - Authority test:** Server agents enabled; client agents disabled.
- [ ] **8C - Destination logic tests:** Extract/test pure candidate rejection where practical.
- [ ] **8D - Health tests:** Enemy damage, friendly rejection, death, replacement.
- [ ] **8E - Dust2 soak:** Ten minutes.
- [ ] **8F - Office soak:** Ten minutes.
- [ ] **8G - Host/client test:** Smooth motion + hits, one host/one remote.
- [ ] **8H - Full roster test:** Inspect CPU, animation, bandwidth, allocations.
- [ ] **8I - Console check:** No new errors, recurring nav warnings, NGO order errors, missing bindings.

## Test Matrix

| Scenario | Expected result |
|---|---|
| Host, Dust2 | Bots spawn on NavMesh; explore Dust2 only |
| Host, Office | Bots spawn on NavMesh; explore Office only |
| Host plus one client | Matching smooth movement |
| Client proxy inspection | Agent + destination selection disabled |
| Enemy shot | Server health decreases + replicates |
| Friendly shot | Health unchanged |
| Moving bot, host shot | Root-capsule hit resolves |
| Moving bot, remote shot | Acceptable normal-latency hit |
| Bot death | Score once; despawn |
| Replacement | New bot spawns + explores |
| Invalid destination | Rejected before movement |
| Stuck bot | Path reset + new destination |
| Hidden map | No bot/destination enters |
| Idle | Rifle-ready; no A-pose |
| Walk/jog | Correct blend; stable rifle |
| Full bot roster | Acceptable CPU/bandwidth/animation cost |

## Expected File Changes

### New project-owned files

- Assets/FPSProject/Multiplayer/Core/Match/PassiveTargetBotNavigator.cs
- Assets/FPSProject/Multiplayer/Core/Match/PassiveTargetBotAnimationDriver.cs
- Assets/FPSProject/Multiplayer/Core/Match/BotExplorationArea.cs
- Assets/FPSProject/Multiplayer/Animations/AC_PassiveTargetBot.controller
- Assets/FPSProject/Multiplayer/Animations/AM_PassiveTargetBotUpperBody.mask
- Assets/FPSProject/Multiplayer/Animations/A_PassiveTargetBot_RifleReady.anim
- Relevant EditMode and PlayMode tests under Assets/FPSProject/Multiplayer/Core/Tests

### Existing project-owned files or assets likely modified

- Assets/FPSProject/Multiplayer/Prefabs/PassiveTargetBot.prefab
- Assets/Scenes/OperationsDemoMultiplayer.unity
- ProjectSettings/NavMeshAreas.asset
- Assets/FPSProject/Multiplayer/Core/Match/TeamDeathmatchManager.cs only if spawn-time NavMesh placement cannot remain self-contained in the navigator

### Third-party files that must remain unchanged

- Assets/KINEMATION
- Assets/CAS Demo

Vendor clips/prefabs may be referenced or nested; never edit source contents.

## Risks and Mitigations

### Generic animation paths do not perfectly match Operator

Run rig spike before production controller. Avoid Humanoid conversion unless exact Generic binding fails.

### NavMeshAgent and physical collider conflict

Replace CharacterController with passive non-trigger CapsuleCollider; NavMeshAgent solely owns displacement.

### Inactive map navigation remains loaded

Put each NavMeshSurface + BotExplorationArea under map root controlled by ApplyMapVisibility; explicitly test isolation.

### Remote bot shots miss because bots lack rewind

Verify masks, collider, teams, authority first. Add pose history only when measured misses justify cost.

### Rifle prop is attached but hands do not grip it

Author upper-body ready pose; socket attachment alone insufficient.

### Foot sliding

Tune agent speed, blend thresholds, clip playback from measured stride; root motion stays disabled.

### Excessive network traffic

Tune NetworkTransform thresholds/interpolation; profile maximum roster before more replicated animation state.

## Deferred Work

- Detection/targeting; aiming/firing/reload/ammo/recoil.
- Cover/tactics; squads; hearing/vision/alert states.
- Vault/climb/jump/ladder/complex off-mesh travel.
- Strafe/backward locomotion; turn-in-place.
- Limb hitboxes/headshots/armor/multipliers.
- Full bot lag compensation.
- Ragdolls/death animations.

## Completion Gates

Complete only when:

- Both maps: valid isolated NavMesh data.
- Server-authoritative roaming; smooth remote movement.
- No A-pose; rifle-ready idle/walk/jog.
- Enemy hits work; friendly rejection intact.
- Death/score/despawn/replacement/resumed roaming work.
- Tests + soaks have no new errors.
- Third-party KINEMATION/CAS sources untouched.

## Progress Log

Add dated entries as stages complete.

| Date | Stage | Result | Evidence or remaining risk |
|---|---|---|---|
| 2026-08-09 | Planning | Cavecrew investigation and live scene inspection completed | Implementation not started |

## Decision Log

| Decision | Status | Reason |
|---|---|---|
| Server-authoritative NavMeshAgent | Accepted | Matches server-owned match/bot lifecycle |
| NetworkTransform for bot movement | Accepted | Simplest reliable continuous NGO replication |
| Generic KINEMATION locomotion | Pending rig spike | Import mode + hierarchy evidence strong |
| Project-owned upper-body rifle pose | Accepted | Avoid full player input/camera bridge in bots |
| Root capsule only | Accepted for first pass | Sufficient for basic reliable damage |
| Bot rewind | Deferred pending evidence | Host physics fallback may suffice |
