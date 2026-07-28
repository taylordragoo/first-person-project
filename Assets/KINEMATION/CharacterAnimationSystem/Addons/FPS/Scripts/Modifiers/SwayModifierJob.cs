// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    public struct VectorSpringState
    {
        public FloatSpringState x;
        public FloatSpringState y;
        public FloatSpringState z;

        public void Reset()
        {
            x.Reset();
            y.Reset();
            z.Reset();
        }
    }
    
    public struct SwayModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private Vector2 _moveInput;
        private Vector2 _deltaLookInput;
        
        // Move sway.
        private Vector3 _moveSwayPositionTarget;
        private Vector3 _moveSwayRotationTarget;
        
        private Vector3 _moveSwayPositionResult;
        private Vector3 _moveSwayRotationResult;
        
        private VectorSpringState _moveSwayPositionSpring;
        private VectorSpringState _moveSwayRotationSpring;

        // Aim sway.
        private Vector2 _aimSwayTarget;
        private Vector3 _aimSwayPositionResult;
        private Vector3 _aimSwayRotationResult;

        private VectorSpringState _aimSwayPositionSpring;
        private VectorSpringState _aimSwayRotationSpring;

        private int _freeAimPropertyIndex;
        private int _moveInputPropertyIndex;
        private int _mouseInputPropertyIndex;
        
        private ModifierJobData _jobData;
        private TransformStreamHandle _weaponBoneHandle;
        private TransformStreamHandle _weaponBoneAdditiveHandle;
        
        private SwayModifierSettings _settings;

        private BindableProperty<Vector2> _lookInputProp;
        private BindableProperty<Vector2> _moveInputProp;
        private BindableProperty<bool> _isAimingProp;

        private BindableProperty<WeaponSway> _moveSwayProp;
        private BindableProperty<WeaponSway> _aimSwayProp;

        private WeaponSway _movementSway;
        private WeaponSway _aimSway;
        private bool _isAiming;

        private float _curveWeight;
        
        private Vector3 VectorSpringInterp(Vector3 current, Vector3 target, in VectorSpring spring, 
            ref VectorSpringState state, float deltaTime)
        {
            target.x = Mathf.Clamp(target.x, -spring.clamp.x, spring.clamp.x);
            current.x = KSpringMath.FloatSpringInterp(current.x, target.x, spring.speed.x, 
                spring.damping.x, spring.stiffness.x, spring.scale.x, ref state.x, deltaTime);
            
            target.y = Mathf.Clamp(target.y, -spring.clamp.y, spring.clamp.y);
            current.y = KSpringMath.FloatSpringInterp(current.y, target.y, spring.speed.y, 
                spring.damping.y, spring.stiffness.y, spring.scale.y, ref state.y, deltaTime);
            
            target.z = Mathf.Clamp(target.z, -spring.clamp.z, spring.clamp.z);
            current.z = KSpringMath.FloatSpringInterp(current.z, target.z, spring.speed.z, 
                spring.damping.z, spring.stiffness.z, spring.scale.z, ref state.z, deltaTime);

            return current;
        }

        private void ProcessMoveSway(AnimationStream stream)
        {
            var rotationTarget = new Vector3()
            {
                x = _moveInput.y,
                y = _moveInput.x,
                z = _moveInput.x
            };

            var positionTarget = new Vector3()
            {
                x = _moveInput.x,
                y = _moveInput.y,
                z = _moveInput.y
            };

            rotationTarget *= _isAiming ? _movementSway.adsScale : 1f;
            positionTarget *= _isAiming ? _movementSway.adsScale : 1f;
            
            float alpha = KMath.ExpDecayAlpha(_movementSway.dampingFactor, stream.deltaTime);

            _moveSwayPositionTarget = Vector3.Lerp(_moveSwayPositionTarget, positionTarget / 100f, alpha);
            _moveSwayRotationTarget = Vector3.Lerp(_moveSwayRotationTarget, rotationTarget, alpha);

            _moveSwayPositionResult = VectorSpringInterp(_moveSwayPositionResult,
                _moveSwayPositionTarget, _movementSway.position, ref _moveSwayPositionSpring, stream.deltaTime);

            _moveSwayRotationResult = VectorSpringInterp(_moveSwayRotationResult,
                _moveSwayRotationTarget, _movementSway.rotation, ref _moveSwayRotationSpring, stream.deltaTime);

            KTransform transform = new KTransform()
            {
                position = _moveSwayPositionResult,
                rotation = Quaternion.Euler(_moveSwayRotationResult).normalized,
                scale = Vector3.one
            };

            KPose pose = new KPose()
            {
                modifyMode = EModifyMode.Add,
                pose = transform,
                space = _movementSway.space
            };

            KAnimationMath.ModifyTransform(stream, _jobData.rootHandle, _weaponBoneHandle, pose,
                _jobData.weight);
        }
        
        private void ProcessAimSway(AnimationStream stream)
        {
            _aimSwayTarget += new Vector2(_deltaLookInput.x, _deltaLookInput.y) * 0.01f;

            float alpha = KMath.ExpDecayAlpha(_aimSway.dampingFactor, stream.deltaTime);
            _aimSwayTarget = Vector2.Lerp(_aimSwayTarget, Vector2.zero, alpha);
            
            Vector3 targetLoc = new Vector3()
            {
                x = _aimSwayTarget.x,
                y = _aimSwayTarget.y,
                z = 0f
            };
            
            Vector3 targetRot = new Vector3()
            {
                x = _aimSwayTarget.y,
                y = _aimSwayTarget.x,
                z = _aimSwayTarget.x
            };
            
            targetRot *= _isAiming ? _aimSway.adsScale : 1f;
            targetLoc *= _isAiming ? _aimSway.adsScale : 1f;

            _aimSwayPositionResult = VectorSpringInterp(_aimSwayPositionResult,
                targetLoc / 100f, _aimSway.position, ref _aimSwayPositionSpring, stream.deltaTime);

            _aimSwayRotationResult = VectorSpringInterp(_aimSwayRotationResult,
                targetRot, _aimSway.rotation, ref _aimSwayRotationSpring, stream.deltaTime);
            
            KTransform aimSwayTransform = new KTransform()
            {
                position = _aimSwayPositionResult,
                rotation = Quaternion.Euler(_aimSwayRotationResult)
            };
            
            KPose pose = new KPose()
            {
                modifyMode = EModifyMode.Add,
                pose = aimSwayTransform,
                space = _aimSway.space
            };
            
            KAnimationMath.ModifyTransform(stream, _jobData.rootHandle, _weaponBoneHandle, pose, 
                _jobData.weight);
        }

        private void ProcessCurveAnimation(AnimationStream stream)
        {
            _curveWeight = KMath.FloatInterp(_curveWeight, _isAiming ? _settings.adsCurveScale : 1f, 
                _settings.adsCurveSmoothing, stream.deltaTime);
            
            KTransform animation = KAnimationMath.GetTransform(stream, _weaponBoneAdditiveHandle, false);
            animation.position = _settings.spaceOffset * animation.position;
            animation.rotation = _settings.spaceOffset * animation.rotation * Quaternion.Inverse(_settings.spaceOffset);

            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _weaponBoneHandle,
                animation.position, _jobData.weight * _curveWeight);

            KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _weaponBoneHandle,
                animation.rotation, _jobData.weight * _curveWeight);
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!KAnimationMath.IsWeightRelevant(_jobData.weight) || !_weaponBoneHandle.IsValid(stream)) return;
            
            ProcessMoveSway(stream);
            ProcessAimSway(stream);
            ProcessCurveAnimation(stream);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = settings as SwayModifierSettings;
            
            _weaponBoneHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.weaponBone);
            _weaponBoneAdditiveHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.weaponAdditiveBone);

            _lookInputProp = _settings.deltaLookInput.CreateProperty(jobData.animator.gameObject);
            _moveInputProp = _settings.moveInput.CreateProperty(jobData.animator.gameObject);
            _isAimingProp = _settings.isAiming.CreateProperty(jobData.animator.gameObject);
            
            _moveSwayProp = _settings.movementSway.CreateProperty(jobData.animator.gameObject);
            _aimSwayProp = _settings.aimingSway.CreateProperty(jobData.animator.gameObject);
            
            _moveSwayPositionSpring.Reset();
            _moveSwayRotationSpring.Reset();

            _aimSwayPositionSpring.Reset();
            _aimSwayRotationSpring.Reset();

            _deltaLookInput = _moveInput = Vector2.zero;
            _curveWeight = 1f;
        }

        public void OnModifierUpdated(AnimationModifierSettings newSettings)
        {
        }

        public AnimationScriptPlayable CreatePlayable(PlayableGraph graph)
        {
            return AnimationScriptPlayable.Create(graph, this);
        }

        public void PreUpdateJobData()
        {
        }

        public void UpdateJobData(AnimationScriptPlayable playable, float weight)
        {
            var job = playable.GetJobData<SwayModifierJob>();

            job._movementSway = _moveSwayProp.GetValue();
            job._aimSway = _aimSwayProp.GetValue();

            job._isAiming = _isAimingProp.GetValue();
            job._deltaLookInput = _lookInputProp.GetValue();
            job._moveInput = _moveInputProp.GetValue();
            
            job._jobData.weight = weight;
            
            playable.SetJobData(job);
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
        }

        public void OnDrawGizmos()
        {
        }

        public void OnSceneGUI()
        {
        }
    }
}