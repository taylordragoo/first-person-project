# Integrate Cowsins-Inspired Projectiles and Decals

## Summary

Adapt the useful parts of Cowsins’ shooting and impact design into the existing CAS demo weapon stack without importing Cowsins’ player, input, inventory, or controller dependencies. The CAS pistol, SMG, and sniper will use hitscan. A pooled physical-projectile path will also be implemented and tested so a future weapon can enable it through data rather than a second combat system.

This pass intentionally targets `CAS_Player_Example_FPS` and its `WeaponProp` weapons. The fortress-based `OperationsDemo` scene currently runs `TAC_Player_Example_FPS` and keeps the CAS player inactive; integrating the active Tactical Shooter weapon stack is outside this pass. Manual CAS validation will temporarily switch the active player in the scene and will not save that activation change.

The implementation improves on Cowsins in four areas:

- Route hitscan and physical projectiles through one damage, surface, impact, decal, and tracer pipeline.
- Pool projectiles as well as tracers, transient impacts, and decals.
- Use a default surface effect plus optional component-based overrides instead of requiring project layers named Ground, Metal, Wood, and so on.
- Keep the combat core independent of CAS so it can be covered by dedicated Unity test assemblies and reused by another weapon stack later.

## Project and Assembly Layout

- Add runtime code under `Assets/FPSProject/Combat/Runtime` with an `FPSProject.Combat.Runtime.asmdef`.
- Keep the runtime assembly independent of CAS and KINEMATION types. It accepts a generic shot request and Unity objects such as a camera, muzzle transform, owner root, and effect prefabs.
- Add `Assets/FPSProject/Combat/Tests/EditMode/FPSProject.Combat.EditModeTests.asmdef` and `Assets/FPSProject/Combat/Tests/PlayMode/FPSProject.Combat.PlayModeTests.asmdef`, both referencing the runtime assembly and Unity Test Framework.
- Keep the small integration changes to CAS `WeaponSettings` and `WeaponProp` in their existing predefined assembly. That assembly can consume the auto-referenced combat runtime without making the runtime or its tests depend on the CAS demo scripts.

## Runtime Data and Public Interfaces

- `WeaponShotType`: `Hitscan` or `Projectile`.
- `ImpactSurfaceType`: `Default`, `Metal`, `Wood`, `Grass`, `Mud`, or `Flesh`.
- `WeaponBallisticsSettings`: a serializable value on CAS `WeaponSettings` containing:
  - Combat-enabled flag and shot type.
  - Damage, maximum range, hit mask, and trigger-query policy. Default trigger behavior is `Ignore` and is explicit rather than inherited from global physics settings.
  - Spread in degrees, defined as the maximum half-angle from the camera center ray; zero disables spread.
  - Tracer prefab, travel speed, and lifetime.
  - Projectile prefab, speed, sweep radius, gravity enabled/multiplier, and lifetime.
  - Shared `ImpactEffectLibrary` reference.
- `WeaponShotRequest`: immutable per-shot input containing a snapshot of the ballistics values, firing owner root, weapon object, muzzle position/rotation, and camera aim origin/direction. Pooled projectiles retain this snapshot even if the player changes weapons.
- `DamageInfo`: damage amount, hit point, hit normal, travel direction, instigating owner, and source weapon.
- `IDamageable.ApplyDamage(in DamageInfo)`: reusable damage contract without imposing a health implementation. Resolve the first implementation on the hit collider, then its closest parent, and invoke it at most once per contact.
- `ImpactSurface`: optional component selecting a surface type. Resolve the collider first and then the closest parent; if none exists, use `Default`.
- `ImpactEffectLibrary`: ScriptableObject containing a default decal/transient-impact pair and optional pairs for metal, wood, grass, mud, and flesh. A missing override falls back to the default pair, and either prefab in a pair may be null.
- `WeaponCombatRuntime.SubmitShot(in WeaponShotRequest)`: the only shot entry point used by the CAS bridge.

## CAS Integration

- Extend CAS `WeaponSettings` with `WeaponBallisticsSettings ballistics`. Existing assets remain inert until `combatEnabled` is checked and configured.
- Add a serialized `Transform muzzlePoint` to `WeaponProp`; the transform reference belongs on the weapon prefab rather than the ScriptableObject settings asset.
- Add and assign a `MuzzlePoint` child at the visible barrel exit of `CAS_W_Pistol`, `CAS_W_SMG`, and `CAS_W_SniperRifle`.
- Add `WeaponCombatRuntime` to `CAS_Player_Example_FPS` and assign/cache its child Unity `Camera` as the aim camera. Fail validation with a clear warning when the camera is missing.
- In `WeaponProp.Awake()`, cache the player-owned runtime from the weapon’s root/parents. A missing runtime or disabled combat settings must not interrupt existing sound, recoil, animation, or fire-mode behavior.
- In `WeaponProp.Fire()`, submit exactly one request after the existing sound, recoil, camera shake, and fire animation calls, but before burst decrement and the semi/burst early return. This ordering ensures the pistol, sniper, and final burst round all create a combat shot while preserving the current cadence.
- Do not add a second fire-rate gate inside `WeaponCombatRuntime`; `WeaponProp` remains authoritative for semi, burst, and automatic cadence.

## Shared Aim and Collision Pipeline

- Resolve one camera aim ray per shot. Apply spread at the camera stage, then query to the configured maximum range.
- If the camera query hits, use its hit point as the desired destination. On a miss, use `cameraOrigin + spreadDirection * range`.
- From the muzzle, aim toward that destination and clamp the muzzle query/trajectory to the configured maximum range. The muzzle result is authoritative so nearby cover can block a farther camera target.
- Use this same camera-to-muzzle resolution for both hitscan and physical projectiles. Hitscan resolves immediately; a projectile starts at the muzzle with velocity along the resolved muzzle direction and then follows its simulated trajectory.
- Spawn a tracer from the muzzle to the authoritative hitscan endpoint on both hits and misses. Projectile visuals may use their own trail; they still use the shared impact pipeline on contact.
- Implement reusable non-allocating ray, sphere, and overlap query buffers. Scan all returned results for the nearest accepted collider rather than taking one hit and discarding it afterward.
- Reject any collider whose transform is the firing owner root or one of its descendants, including the viewmodel hierarchy. If a query buffer fills, expand it and retry so an owner hit cannot hide valid geometry behind it.
- Apply the configured layer mask and trigger policy consistently to camera, muzzle, projectile sweep, and initial-overlap queries.

## Physical Projectile Simulation

- Implement a pooled `WeaponCombatProjectile` updated during fixed simulation steps.
- At spawn, perform an overlap check at the projectile sphere. Treat the nearest accepted overlap as an immediate contact so a muzzle already intersecting cover cannot launch through it; use the opposite travel direction as a fallback normal when the overlap cannot provide a stable normal.
- Each physics tick, integrate velocity, including `Physics.gravity * gravityMultiplier` when enabled, and sphere-sweep the entire displacement from the current position to the proposed next position.
- Resolve only the nearest accepted contact, apply damage once, spawn the same surface impact/decal pair used by hitscan, and return the projectile to its pool.
- Return a projectile without impact when either its lifetime expires or its accumulated travel reaches maximum range.
- Reset velocity, lifetime, distance traveled, owner/weapon references, renderer/trail state, and contact guards whenever a projectile returns to or leaves the pool.

## Impacts, Decals, Tracers, and Pooling

- Route every accepted hitscan or projectile contact through one impact method that constructs `DamageInfo`, resolves `IDamageable`, resolves `ImpactSurfaceType`, and spawns the selected effects.
- Place decals a small configurable distance above the surface, orient their forward axis to the hit normal, and apply randomized rotation around that normal.
- Keep pooled decals owned by the combat runtime. For a moving hit transform, store the target-relative pose and update it while active instead of making the decal lifecycle-owned by the target. Return the decal if the target is destroyed. This prevents moving targets or non-uniform parent scale from destroying or distorting pooled instances.
- Transient impact effects use the hit pose, restart their particle systems when rented, and return after their configured duration.
- Use prefab-keyed pools with lazy creation and automatic expansion. The following capacities are per prefab key and are explicitly not aggregate totals:
  - 32 decals.
  - 16 transient impact effects.
  - 16 tracers.
  - 16 projectiles.
- Default lifetimes are 20 seconds for decals and five seconds for physical projectiles. Effect- and tracer-specific lifetimes may be shorter.
- Every pooled type has an explicit reset contract: cancel pending returns, clear target-follow data, restore local scale, reset transforms and active state, clear `TrailRenderer` history before emission, stop/clear particle systems, reset rigidbodies, and discard dead externally-destroyed entries.

## VFX Asset Migration

- Copy only the licensed Cowsins bullet-hole PNGs, their prefabs, and all corresponding `.meta` files into `Assets/FPSProject/VFX/Decals`. Remove unused components from the copied prefab roots after import.
- Copy the Cowsins `Trail.prefab`, `Trail.prefab.meta`, `Trail.mat`, and `Trail.mat.meta` together into `Assets/FPSProject/VFX/Tracers`; the prefab is not self-contained and references that material by GUID.
- Verify the migrated trail material after Unity upgrades it from the source project to this project’s URP version. If it renders pink, opaque, or otherwise incorrectly, recreate it with a current URP transparent particle/unlit shader and update the copied prefab reference.
- Rebuild a small URP-compatible dust/spark transient impact prefab in this project because several source impact prefabs are empty or contain unresolved material/mesh references.
- Do not copy Cowsins scripts, player/controller prefabs, inventory assets, input assets, layers, tags, or unrelated VFX.

## Asset Configuration

- Create one shared `ImpactEffectLibrary` using the rebuilt default transient impact and copied default decal, plus the available metal, wood, grass, mud, and flesh decal overrides. Overrides without a suitable transient effect reuse the default transient effect.
- Configure the three existing CAS weapon settings as hitscan with provisional values:
  - Pistol: 25 damage, 75 m range, 0.5° spread.
  - SMG: 12 damage, 60 m range, 1.25° spread.
  - Sniper: 100 damage, 250 m range, no spread.
- Use the shared impact library and tracer prefab for all three weapons. Keep projectile-only fields valid but unused while `shotType` is `Hitscan`.
- Add editor validation for enabled settings with missing muzzle, runtime, camera, tracer, impact library, or projectile prefab. Validation should report configuration problems without breaking the existing CAS firing presentation.

## Test Plan

### Edit Mode

- Default surface resolution, explicit override resolution, child-collider/parent-surface precedence, and missing-override fallback.
- Damage payload contents, closest-parent `IDamageable` resolution, and exactly-once dispatch.
- Owner/viewmodel rejection where an ignored collider appears before a valid target.
- Layer-mask and trigger-policy consistency.
- Nearest-hit selection and query-buffer expansion when the initial non-allocating buffer fills.
- Prefab-keyed pool reuse and reset behavior for transforms, followers, timers, projectiles, trails, particles, and externally destroyed entries.

### Play Mode

- Hitscan applies damage once and creates the correct decal and transient impact.
- A muzzle obstruction wins over a farther camera target.
- A miss creates a tracer from the muzzle to the range-clamped endpoint.
- Repeated `SubmitShot` calls each create exactly one combat shot and are not suppressed by a second runtime fire-rate gate. CAS-specific semi, burst, and automatic ordering remains a manual integration check because the CAS demo scripts stay in the predefined assembly rather than the combat test assembly.
- Physical projectiles use the camera-to-muzzle aim solution, hit thin colliders without tunneling, handle an initial muzzle overlap, obey gravity, ignore their owner, stop at maximum range, expire by lifetime, and create the same damage and impacts as hitscan.
- Decals follow moving colliders, survive non-uniform scale, return cleanly when their target is destroyed, and show no stale trail/particle/decal state when reused.

### Manual Fortress Validation

- Open the built fortress environment at `Assets/Scenes/OperationsDemo.unity`.
- Temporarily disable `TAC_Player_Example_FPS` and enable `CAS_Player_Example_FPS` for the validation session. Do not save those activation overrides; Tactical Shooter remains the default scene player and is outside this pass.
- Fire the pistol, SMG, and sniper against box and mesh colliders. Exercise semi-auto, the SMG’s burst through its final round, and automatic fire; confirm one combat result per presentation cycle along with reticle alignment, muzzle-obstruction behavior, recoil/audio/animation preservation, tracers on hits and misses, persistent decals, and transient particles.
- Exercise moving geometry where available and inspect close-range shots around doorways and walls.
- Run Edit Mode and Play Mode tests, wait for script compilation/domain reload to finish, and finish with no new Unity console errors or warnings attributable to this feature.

## Acceptance Criteria

- All three CAS weapons produce correctly aligned hitscan damage, tracers, impacts, and decals without changing existing presentation or fire cadence.
- Hitscan and projectile contacts share the same damage and surface-effect code path.
- Owner/viewmodel colliders never consume a shot or hide a valid hit behind them.
- A nearby muzzle obstruction always takes precedence over the camera target.
- Projectiles cannot tunnel through the thin-collider test and cannot escape through an initial overlap.
- Reused pooled objects contain no stale parent, motion, trail, particle, timer, damage, or owner state.
- The dedicated Edit Mode and Play Mode test assemblies pass, and manual CAS validation completes with a clean Unity console.

## Assumptions and Non-Goals

- The project is licensed to reuse the Cowsins assets available in the supplied `fps-prototype` repository.
- This pass targets the CAS demo weapon stack only. It does not modify `TAC_Player_Example_FPS`, `TacticalShooterWeapon`, or the default player selection in `OperationsDemo`.
- This pass adds and tests the physical-projectile runtime but does not add a launcher or another projectile weapon to normal gameplay.
- Surface-specific fortress classification is optional; all existing geometry works immediately through the default impact pair.
- No penetration, ricochet, explosive damage, critical-hit tags, networking/prediction, ammo system, or save-data migration is included.
- Existing uncommitted project/package settings are preserved, and build settings or unrelated imported assets are not rewritten.
