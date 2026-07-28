// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    public struct FPSOffsetJob : IAnimationJob, IAnimationModifierJob
    {
        private FPSOffsetSettings _settings;
        private ModifierJobData _jobData;

        private TransformStreamHandle _ikWeaponBoneHandle;
        private TransformStreamHandle _ikWeaponBoneAimHandle;
        private TransformStreamHandle _ikHandRightHandle;
        private TransformStreamHandle _ikHandLeftHandle;

        private BindableProperty<KTransform> _weaponOffsetProp;
        private BindableProperty<KTransform> _rightHandOffsetProp;
        private BindableProperty<KTransform> _leftHandOffsetProp;

        private KTransform _weaponOffset;
        private KTransform _rightHandOffset;
        private KTransform _leftHandOffset;
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (Mathf.Approximately(_jobData.weight, 0f)) return;

            bool ikWeaponBoneIsValid = _ikWeaponBoneHandle.IsValid(stream);

            if (ikWeaponBoneIsValid)
            {
                KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _ikWeaponBoneHandle, _weaponOffset.position, 
                    _jobData.weight);
                KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _ikWeaponBoneHandle, _weaponOffset.rotation,
                    _jobData.weight);

                if (_ikWeaponBoneAimHandle.IsValid(stream))
                {
                    KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _ikWeaponBoneAimHandle, 
                        _weaponOffset.position, _jobData.weight);
                    KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _ikWeaponBoneAimHandle, 
                        _weaponOffset.rotation, _jobData.weight);
                }
            }
            
            if (_ikHandRightHandle.IsValid(stream) && ikWeaponBoneIsValid)
            {
                KAnimationMath.MoveInSpace(stream, _ikWeaponBoneHandle, _ikHandRightHandle, 
                    _rightHandOffset.position, _jobData.weight);
                KAnimationMath.RotateInSpace(stream, _ikWeaponBoneHandle, _ikHandRightHandle,
                    _rightHandOffset.rotation, _jobData.weight);
            }
            
            if (_ikHandLeftHandle.IsValid(stream) && ikWeaponBoneIsValid)
            {
                KAnimationMath.MoveInSpace(stream, _ikWeaponBoneHandle, _ikHandLeftHandle, 
                    _leftHandOffset.position, _jobData.weight);
                KAnimationMath.RotateInSpace(stream, _ikWeaponBoneHandle, _ikHandLeftHandle, 
                    _leftHandOffset.rotation, _jobData.weight);
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _settings = settings as FPSOffsetSettings;
            _jobData = jobData;

            _ikWeaponBoneHandle = AnimationModifierUtility.GetHandle(jobData, _settings.ikWeaponBone);
            _ikWeaponBoneAimHandle = AnimationModifierUtility.GetHandle(jobData, _settings.ikWeaponBoneAim);
            _ikHandRightHandle = AnimationModifierUtility.GetHandle(jobData, _settings.ikHandRight);
            _ikHandLeftHandle = AnimationModifierUtility.GetHandle(jobData, _settings.ikHandLeft);

            _weaponOffsetProp = _settings.weaponBoneOffset.CreateProperty(jobData.animator.gameObject);
            _rightHandOffsetProp = _settings.rightHandOffset.CreateProperty(jobData.animator.gameObject);
            _leftHandOffsetProp = _settings.leftHandOffset.CreateProperty(jobData.animator.gameObject);
        }

        public void OnModifierUpdated(AnimationModifierSettings newSettings)
        {
            Initialize(_jobData, newSettings);
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
            _jobData.weight = weight;

            _weaponOffset = _weaponOffsetProp.GetValue();
            _rightHandOffset = _rightHandOffsetProp.GetValue();
            _leftHandOffset = _leftHandOffsetProp.GetValue();
            
            playable.SetJobData(this);
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