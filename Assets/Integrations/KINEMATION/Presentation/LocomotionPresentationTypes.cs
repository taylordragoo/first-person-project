using System;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Presentation
{
    /// <summary>
    /// Selects the one source used to resolve presentation gait for a sample.
    /// </summary>
    public enum GaitSource
    {
        ResolvedRawGait,
        ObservedPlanarSpeed
    }

    // Named aliases keep call sites readable without introducing a second, incompatible enum.
    public static class LocomotionPresentationGaitSource
    {
        public const GaitSource ResolvedRawGait = GaitSource.ResolvedRawGait;
        public const GaitSource ObservedPlanarSpeed = GaitSource.ObservedPlanarSpeed;
    }

    [Serializable]
    public struct LocomotionPresentationInput
    {
        public Vector2 MoveAxes;
        public GaitSource GaitSource;
        public float RawGait;
        public float ObservedPlanarSpeed;
        public bool IsMoving;
        public bool IsGrounded;
        public bool IsInAir;
        public bool IsCrouching;
        public bool IsSprinting;
        public bool IsAiming;
        public bool IsAlive;
        public bool IsFirstPerson;
        public float ViewWeight;
        public float AimingWeight;

        public Vector2 MoveInput { get => MoveAxes; set => MoveAxes = value; }
        public float PlanarSpeed { get => ObservedPlanarSpeed; set => ObservedPlanarSpeed = value; }
    }

    [Serializable]
    public struct LocomotionPresentationSettings
    {
        public const float DefaultWalkSpeed = 1.5f;
        public const float DefaultJogSpeed = 3f;
        public const float DefaultSprintSpeed = 5f;
        public const float DefaultAnimGaitSmoothing = 12f;
        public const float DefaultAnimatorMoveInterpSpeed = 7f;

        public float WalkSpeed;
        public float JogSpeed;
        public float SprintSpeed;
        public float AnimGaitSmoothing;
        public float AnimatorMoveInterpSpeed;
        public bool OrientRotationToMovement;

        public float GaitSmoothing
        {
            get => AnimGaitSmoothing;
            set => AnimGaitSmoothing = value;
        }

        public float MoveSmoothing
        {
            get => AnimatorMoveInterpSpeed;
            set => AnimatorMoveInterpSpeed = value;
        }

        public static LocomotionPresentationSettings Default => new LocomotionPresentationSettings
        {
            WalkSpeed = DefaultWalkSpeed,
            JogSpeed = DefaultJogSpeed,
            SprintSpeed = DefaultSprintSpeed,
            AnimGaitSmoothing = DefaultAnimGaitSmoothing,
            AnimatorMoveInterpSpeed = DefaultAnimatorMoveInterpSpeed,
            OrientRotationToMovement = true
        };

    }

    [Serializable]
    public struct LocomotionPresentationOutput
    {
        public float RawPresentationGait;
        public float SmoothedAnimatorGait;
        public Vector2 SmoothedAnimatorMoveAxes;

        public bool AnimatorIsFirstPerson;
        public bool AnimatorIsInAir;
        public bool AnimatorIsGrounded;
        public bool AnimatorIsMoving;
        public bool AnimatorIsCrouching;
        public bool AnimatorIsSprinting;
        public float AnimatorViewWeight;
        public float AnimatorAimingWeight;

        public bool MovementStarted;
        public bool MovementStopped;
        public bool CrouchStarted;
        public bool CrouchStopped;
        public bool Jumped;
        public bool Landed;
        public bool AimStarted;
        public bool AimStopped;
        public bool SprintStarted;
        public bool SprintStopped;

        public float RawGait { get => RawPresentationGait; set => RawPresentationGait = value; }
        public float AnimatorGait { get => SmoothedAnimatorGait; set => SmoothedAnimatorGait = value; }
        public Vector2 AnimatorMoveAxes
        {
            get => SmoothedAnimatorMoveAxes;
            set => SmoothedAnimatorMoveAxes = value;
        }

        public bool AnimatorCrouch
        {
            get => AnimatorIsCrouching;
            set => AnimatorIsCrouching = value;
        }

        public bool IsMoving
        {
            get => AnimatorIsMoving;
            set => AnimatorIsMoving = value;
        }

        public bool IsInAir
        {
            get => AnimatorIsInAir;
            set => AnimatorIsInAir = value;
        }

        public bool IsGrounded
        {
            get => AnimatorIsGrounded;
            set => AnimatorIsGrounded = value;
        }

        public float ViewWeight
        {
            get => AnimatorViewWeight;
            set => AnimatorViewWeight = value;
        }

        public float AimingWeight
        {
            get => AnimatorAimingWeight;
            set => AnimatorAimingWeight = value;
        }

        public bool MovementChanged => MovementStarted || MovementStopped;
        public bool CrouchChanged => CrouchStarted || CrouchStopped;
        public bool AimChanged => AimStarted || AimStopped;
        public bool SprintChanged => SprintStarted || SprintStopped;
    }
}
