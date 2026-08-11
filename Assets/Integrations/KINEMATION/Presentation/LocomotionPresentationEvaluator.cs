using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Presentation
{
    /// <summary>
    /// Stateful, per-character presentation evaluator. It owns only presentation smoothing and
    /// semantic edge history; movement, rotation, and root transforms remain motor-owned.
    /// </summary>
    public sealed class LocomotionPresentationEvaluator
    {
        private bool _hasSample;
        private bool _previousMoving;
        private bool _previousGrounded;
        private bool _previousInAir;
        private bool _previousCrouching;
        private bool _previousSprinting;
        private bool _previousAiming;
        private float _smoothedAnimatorGait;
        private Vector2 _smoothedAnimatorMoveAxes;

        public LocomotionPresentationOutput Evaluate(
            LocomotionPresentationInput input,
            LocomotionPresentationSettings settings,
            float deltaTime)
        {
            deltaTime = Mathf.Max(0f, deltaTime);

            // A dead character is always neutral and cannot emit a new reaction. Treating this
            // as a bootstrap also ensures a later respawn cannot inherit stale edge history.
            if (!input.IsAlive)
            {
                _hasSample = false;
                _smoothedAnimatorGait = 0f;
                _smoothedAnimatorMoveAxes = Vector2.zero;
                return BuildOutput(input, 0f, Vector2.zero,
                    false, false,
                    false, false,
                    false, false,
                    false, false,
                    false, false);
            }

            float rawGait = ResolveRawGait(input, settings);
            Vector2 targetMoveAxes = ResolveAnimatorMoveAxes(input, settings);
            bool firstSample = !_hasSample;
            bool movementStarted = false;
            bool movementStopped = false;
            bool crouchStarted = false;
            bool crouchStopped = false;
            bool jumped = false;
            bool landed = false;
            bool aimStarted = false;
            bool aimStopped = false;
            bool sprintStarted = false;
            bool sprintStopped = false;

            if (firstSample)
            {
                _smoothedAnimatorGait = rawGait;
                _smoothedAnimatorMoveAxes = targetMoveAxes;
            }
            else
            {
                _smoothedAnimatorGait = FloatInterp(
                    _smoothedAnimatorGait, rawGait, settings.AnimGaitSmoothing, deltaTime);
                float moveAlpha = ExpDecayAlpha(settings.AnimatorMoveInterpSpeed, deltaTime);
                _smoothedAnimatorMoveAxes = Vector2.Lerp(
                    _smoothedAnimatorMoveAxes, targetMoveAxes, moveAlpha);

                movementStarted = input.IsMoving && !_previousMoving;
                movementStopped = !input.IsMoving && _previousMoving;
                crouchStarted = input.IsCrouching && !_previousCrouching;
                crouchStopped = !input.IsCrouching && _previousCrouching;
                jumped = input.IsInAir && !_previousInAir;
                landed = input.IsGrounded && !_previousGrounded && _previousInAir;
                aimStarted = input.IsAiming && !_previousAiming;
                aimStopped = !input.IsAiming && _previousAiming;
                sprintStarted = input.IsSprinting && !_previousSprinting;
                sprintStopped = !input.IsSprinting && _previousSprinting;
            }

            LocomotionPresentationOutput output = BuildOutput(
                input,
                rawGait,
                _smoothedAnimatorMoveAxes,
                movementStarted,
                movementStopped,
                crouchStarted,
                crouchStopped,
                jumped,
                landed,
                aimStarted,
                aimStopped,
                sprintStarted,
                sprintStopped);
            output.SmoothedAnimatorGait = _smoothedAnimatorGait;

            _previousMoving = input.IsMoving;
            _previousGrounded = input.IsGrounded;
            _previousInAir = input.IsInAir;
            _previousCrouching = input.IsCrouching;
            _previousSprinting = input.IsSprinting;
            _previousAiming = input.IsAiming;
            _hasSample = true;
            return output;
        }

        /// <summary>Clears smoothing and semantic history so the next sample bootstraps.</summary>
        public void Reset()
        {
            _hasSample = false;
            _previousMoving = false;
            _previousGrounded = false;
            _previousInAir = false;
            _previousCrouching = false;
            _previousSprinting = false;
            _previousAiming = false;
            _smoothedAnimatorGait = 0f;
            _smoothedAnimatorMoveAxes = Vector2.zero;
        }

        private static float ResolveRawGait(
            LocomotionPresentationInput input,
            LocomotionPresentationSettings settings)
        {
            if (input.GaitSource == GaitSource.ResolvedRawGait)
                return input.RawGait;

            if (!input.IsMoving) return 0f;

            float walkSpeed = Mathf.Max(0.01f, settings.WalkSpeed);
            float jogSpeed = Mathf.Max(walkSpeed + 0.01f, settings.JogSpeed);
            float sprintSpeed = Mathf.Max(jogSpeed + 0.01f, settings.SprintSpeed);
            float speed = Mathf.Max(0f, input.ObservedPlanarSpeed);

            if (speed <= walkSpeed) return 1f;
            if (speed <= jogSpeed)
                return 1f + Mathf.InverseLerp(walkSpeed, jogSpeed, speed);

            return 2f + Mathf.InverseLerp(jogSpeed, sprintSpeed, Mathf.Min(speed, sprintSpeed));
        }

        private static Vector2 ResolveAnimatorMoveAxes(
            LocomotionPresentationInput input,
            LocomotionPresentationSettings settings)
        {
            Vector2 moveAxes = input.MoveAxes;
            if (!input.IsFirstPerson && !input.IsAiming && settings.OrientRotationToMovement)
            {
                moveAxes.x = 0f;
                moveAxes.y = moveAxes.normalized.magnitude;
            }

            return moveAxes;
        }

        private static LocomotionPresentationOutput BuildOutput(
            LocomotionPresentationInput input,
            float rawGait,
            Vector2 smoothedMoveAxes,
            bool movementStarted,
            bool movementStopped,
            bool crouchStarted,
            bool crouchStopped,
            bool jumped,
            bool landed,
            bool aimStarted,
            bool aimStopped,
            bool sprintStarted,
            bool sprintStopped)
        {
            bool alive = input.IsAlive;
            return new LocomotionPresentationOutput
            {
                RawPresentationGait = rawGait,
                SmoothedAnimatorMoveAxes = smoothedMoveAxes,
                AnimatorIsFirstPerson = alive && input.IsFirstPerson,
                AnimatorIsInAir = alive && input.IsInAir,
                AnimatorIsGrounded = alive && input.IsGrounded,
                AnimatorIsMoving = alive && input.IsMoving,
                AnimatorIsCrouching = alive && input.IsCrouching,
                AnimatorIsSprinting = alive && input.IsSprinting,
                AnimatorViewWeight = alive ? input.ViewWeight : 0f,
                AnimatorAimingWeight = alive ? input.AimingWeight : 0f,
                MovementStarted = movementStarted,
                MovementStopped = movementStopped,
                CrouchStarted = crouchStarted,
                CrouchStopped = crouchStopped,
                Jumped = jumped,
                Landed = landed,
                AimStarted = aimStarted,
                AimStopped = aimStopped,
                SprintStarted = sprintStarted,
                SprintStopped = sprintStopped
            };
        }

        private static float ExpDecayAlpha(float speed, float deltaTime)
        {
            return 1f - Mathf.Exp(-speed * deltaTime);
        }

        private static float FloatInterp(float from, float to, float speed, float deltaTime)
        {
            return speed > 0f ? Mathf.Lerp(from, to, ExpDecayAlpha(speed, deltaTime)) : to;
        }
    }
}
