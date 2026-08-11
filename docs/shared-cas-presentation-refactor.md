# Shared CAS Presentation Refactor — Revised

## Summary

Use the supplied corrections as binding design constraints.

One stateful evaluator will own presentation gait smoothing, animator writes, transition history, and procedural reactions. Motors continue deciding movement. Existing network serialization—including replicated raw `Gait`—stays unchanged during this refactor.

No foot-offset correction. No destination data. No presentation-driven root movement for bots.

## Core contracts

Add project-owned types under `Assets/Integrations/KINEMATION`.

### `LocomotionPresentationInput`

Contains:

- `MoveAxes`
- `GaitSource`: `ResolvedRawGait` or `ObservedPlanarSpeed`
- `RawGait`
- `ObservedPlanarSpeed`
- `IsMoving`
- `IsGrounded`
- `IsInAir`
- `IsCrouching`
- `IsSprinting`
- `IsAiming`
- `IsAlive`
- `IsFirstPerson`
- `ViewWeight`
- `AimingWeight`

Precedence is fixed:

1. `MoveAxes` alone controls animation direction.
2. `GaitSource` selects exactly one gait input:
   - Owners/remotes use resolved replicated/local `RawGait`.
   - Bots use `ObservedPlanarSpeed`.
3. Semantic flags alone control stance, aim, air, moving, and transition edges.
4. Velocity never overrides supplied direction or semantic flags.
5. Root position, root rotation, motor settings, destination, and path state are forbidden from this contract.

### `LocomotionPresentationEvaluator`

A normal per-character instance—not static, not shared.

API:

- `Evaluate(input, settings, deltaTime) → output`
- `Reset()`

State owned per instance:

- Smoothed Animator gait.
- Smoothed Animator movement axes.
- Previous semantic flags for edge detection.
- First-sample/bootstrap status.

It never accesses `Time.deltaTime` internally. Caller supplies time.

### `LocomotionPresentationOutput`

Returns:

- Raw presentation gait.
- Smoothed Animator gait.
- Smoothed Animator movement axes.
- Animator booleans/weights.
- Movement, crouch, jump, landing, aim, and sprint edge results.

The controller applies output to CAS Animator/procedural components.

## Implementation sequence

### 1. Golden-test current local player

Before routing anything:

- Characterize current raw gait thresholds.
- Capture `animGaitSmoothing` results over multiple explicit delta times.
- Capture `MoveX`/`MoveY` smoothing.
- Capture third-person, non-aiming, orient-to-movement forward remapping.
- Capture idle→moving, moving→idle, crouch, jump, landing, aim, and sprint edges.
- Capture local Animator parameter order after one and several frames.
- Verify the Tactical bridge receives raw controller `Gait`, not smoothed Animator gait.

These become the non-regression baseline.

### 2. Add evaluator without changing consumers

Implement evaluator entirely under project integration code.

Rules:

- `ResolvedRawGait`: accept supplied raw gait unchanged, then apply evaluator-owned smoothing.
- `ObservedPlanarSpeed`: calculate presentation gait using existing walk/jog/sprint speed thresholds, without touching motor state.
- Gait calculation cannot modify `_activeGait`, acceleration, deceleration, rotation speed, movement state, or velocity.
- Apply the existing third-person direction remap only to `MoveAxes`.
- Bootstrap first input as history without generating fake transitions.
- Return real transition edges once.
- Dead input forces neutral locomotion output and suppresses new procedural reactions.
- `Reset()` clears all smoothing/history deterministically.

Run pure evaluator tests before integration.

### 3. Add minimal CAS vendor hooks

Keep changes to `Assets/KINEMATION/CharacterAnimationSystem/Examples/Scripts/CharacterExampleController.cs` surgical:

- Add a protected virtual final-presentation hook.
- Default implementation retains existing CAS transition and Animator behavior.
- Add a protected opt-in indicating that a subclass externally handles locomotion presentation.
- When opted in, suppress only direct presentation side effects that would otherwise duplicate shared handling:
  - Jump Animator trigger.
  - Crouch/uncrouch step modifier and Animator write.
  - Final movement-edge/Animator parameter block.
- Do not change trace grounding, gait resolution, CharacterController movement, jump physics, crouch collider geometry, rotation, camera, input, or weapon behavior.
- Add `IsSurfaceGrounded => _isGrounded`.
- Keep existing `IsGrounded => !_isInAir` unchanged.
- Do not migrate existing consumers silently.

Add the equivalent minimal aim-presentation opt-in around the direct FPS aiming modifier so the network subclass can centralize that reaction without double-firing.

Offline/non-network CAS controllers retain default behavior.

### 4. Route local owning player

Update `Assets/Integrations/KINEMATION/Multiplayer/NetworkFPSExampleController.cs`:

- Keep `LocalOwner` calling `base.Update()`.
- Opt this subclass into external/shared presentation.
- After the original motor has resolved state, build input using:
  - `_moveInput` for direction.
  - `_gait` with `ResolvedRawGait`.
  - Current CAS semantic flags.
  - Existing camera/view/aim weights.
- Evaluate using explicit `Time.deltaTime`.
- Write the returned Animator output.
- Fire returned procedural edges:
  - `OnMovementChange`
  - crouch/uncrouch step modifier
  - jump trigger
  - aim modifier
  - visual landing state
- Leave landing momentum in the CharacterController motor path.
- Continue exposing raw `_gait` to `CasTacticalPlayerBridge`.

Verify golden tests remain numerically equivalent.

### 5. Route remote humans

Keep current network schema and raw replicated `Gait`.

Refactor the proxy path:

- Continue receiving interpolated position, velocity, movement axes, raw gait, aim, stance, air, moving, and alive flags.
- Build shared input using:
  - Replicated/interpolated `MoveX`/`MoveY` for direction.
  - Replicated/interpolated `Gait` with `ResolvedRawGait`.
  - Replicated semantic flags for edges.
- Remove direct assignment of received gait to Animator gait.
- Remove proxy-only Animator smoothing/writes and duplicate transition logic.
- Replace `_proxyWas*` fields with evaluator-owned history.
- Keep proxy camera pitch/yaw updates for the Tactical bridge.
- Keep the current human-proxy root wrapper behavior; it remains the sole root writer because CharacterController is disabled.
- `ResetProxyState()` delegates to evaluator `Reset()`.

No serialization, host validation, RPC, or interpolation-buffer field removal occurs in this phase.

### 6. Route bots

Reduce `Assets/Integrations/KINEMATION/Multiplayer/BotCasPresentationAdapter.cs` to source observation and lifecycle work.

It supplies:

- Server velocity from `NavMeshAgent.velocity`.
- Client velocity from filtered NetworkTransform displacement.
- Local movement axes derived from observed velocity relative to body yaw.
- Observed planar speed with `ObservedPlanarSpeed`.
- `IsMoving` from the adapter's filtered observed-motion threshold.
- Alive state from `NetworkHealth`.
- Current truthful standing/not-aiming/not-sprinting semantics.
- Physical grounded/air state from a downward world-ground probe based on the bot capsule and ground mask.

Grounding rules:

- `NavMeshAgent.isOnNavMesh` only confirms navigation attachment.
- It is never used alone as physical grounded state.
- The world probe supplies bot `IsGrounded`/`IsInAir`.
- Off-mesh traversal can therefore become airborne presentation when physically unsupported.
- Existing `IsGrounded` semantics elsewhere remain untouched.

Remove:

- Bot walk/jog/sprint constants.
- `CalculatePlayerGait`.
- Bot-owned gait smoothing.
- Construction of generic proxy `SampledState` solely to reach animation code.
- All destination/path data from presentation.

Retain:

- Velocity observation filtering.
- Tactical staged initialization.
- Input/camera suppression.
- Lifecycle cleanup.
- Root-free presentation call.

Root ownership remains:

- Server bot: NavMeshAgent.
- Client bot: NetworkTransform.
- Evaluator: never.
- CharacterController: disabled.

### 7. Lifecycle hardening

Reset the evaluator on:

- Initial owner/proxy/bot setup.
- Simulation-mode changes.
- Despawn.
- Disable.
- Death.
- Respawn/recycle.
- Hard correction.

After reset:

- First sample seeds smoothing/history.
- No false movement, crouch, jump, landing, or aim reaction fires.
- Subsequent real edges fire once.

## Test plan

### Pure evaluator tests

- Raw-gait mode preserves supplied raw gait.
- Speed mode matches current walk/jog/sprint presentation curve.
- Both modes use identical smoothing afterward.
- Explicit delta-time produces deterministic results.
- Direction precedence ignores velocity when axes are supplied.
- Gait-source precedence uses only the selected source.
- Semantic flags alone drive edges.
- First input produces no synthetic edges.
- Reset and respawn behavior.
- State never leaks between evaluator instances.

### Controller tests

- Local golden outputs remain unchanged.
- Remote raw gait is smoothed identically instead of assigned directly.
- Equivalent local and remote input histories produce equivalent Animator histories.
- Bot speed matching a supplied player gait produces equivalent smoothed Animator gait.
- Tactical bridge still reads raw gait.
- Procedural reactions fire once, without local duplication.
- `IsGrounded` retains existing meaning.
- `IsSurfaceGrounded` reports raw trace state separately.

### Root/navigation tests

- Human proxy wrapper may move its root.
- Bot presentation call cannot change root position or rotation.
- CharacterController remains disabled for proxies/bots.
- NavMeshAgent remains attached and moving during continuous presentation updates.
- World-ground probing—not `isOnNavMesh` alone—controls bot air flags.

### Full verification

- Focused EditMode evaluator/controller tests.
- Existing prefab presentation contract.
- Full EditMode suite.
- Multiplayer PlayMode spawn/lifecycle suite.
- Live multiplayer bot scene.

Live acceptance:

- Local owner, remote human, and bot show matching gait histories under equivalent inputs.
- Feet retain the same stance width through equivalent jog cycles.
- Bots continue navigating without detachment or root snapping.
- No fake spawn transitions, duplicate modifiers, or console errors.

## Deferred follow-up

Removing replicated `Gait` is explicitly out of scope.

After parity is proven, a separate change may compare:

- Owner-resolved replicated gait.
- Gait reconstructed from interpolated proxy velocity.

Only remove replicated gait if tests show equivalent visuals, transitions, Tactical output, and network behavior.

## Assumptions

- Current bots do not intentionally crouch, aim, sprint, or jump.
- Bot neutral semantic flags remain explicit until AI owns those states.
- Existing network compatibility is preserved.
- Vendor edits remain limited to opt-in hooks and the new explicit surface-grounded accessor.
- Existing user changes remain untouched.
