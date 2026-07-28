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
    public struct AdsModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private AdsModifierSettings _settings;
        private ModifierJobData _jobData;
        
        private TransformStreamHandle _aimTargetHandle;
        private TransformStreamHandle _hipTargetHandle;
        private TransformStreamHandle _weaponBoneHandle;
        
        private Vector3 _targetDefaultPose;
        private BindableProperty<bool> _isAiming;
        private BindableProperty<Transform> _aimPointTransform;
        private float _aimingWeight;
        private float _aimPointPlayback;

        private Transform _weaponBone;
        private KTransform _prevAimPoint;
        private KTransform _aimPoint;
        private Transform _cachedAimPoint;
        
        private Vector3 GetEuler(Quaternion rotation)
        {
            Vector3 result = rotation.eulerAngles;

            result.x = KMath.NormalizeEulerAngle(result.x);
            result.y = KMath.NormalizeEulerAngle(result.y);
            result.z = KMath.NormalizeEulerAngle(result.z);

            return result;
        }
        
        private KTransform GetLocalAimPoint(Transform aimPoint)
        {
            KTransform result = KTransform.Identity;
            if (aimPoint != null)
            {
                result.rotation = Quaternion.Inverse(_weaponBone.rotation) * aimPoint.rotation;
                result.position = -_weaponBone.InverseTransformPoint(aimPoint.position);
            }
            
            return result;
        }
        
        private KTransform GetComponentAdsPose(AnimationStream stream, TransformStreamHandle bone)
        {
            KTransform rootTransform = KAnimationMath.GetTransform(stream, _jobData.rootHandle);
            KTransform aimTarget = KAnimationMath.GetTransform(stream, _aimTargetHandle);
            KTransform weaponBone = KAnimationMath.GetTransform(stream, bone);

            aimTarget = rootTransform.GetRelativeTransform(aimTarget, false);
            weaponBone = rootTransform.GetRelativeTransform(weaponBone, false);
            
            KTransform result = new KTransform()
            {
                position = aimTarget.position - weaponBone.position,
                rotation = Quaternion.Inverse(weaponBone.rotation)
            };
            
            return result;
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            float weight = KCurves.Ease(0f, 1f, _aimingWeight, _settings.aimingEaseMode) * _jobData.weight;
            if (!KAnimationMath.IsWeightRelevant(weight))
            {
                return;
            }
            
            if (_settings.cameraBlend > 0f && _aimTargetHandle.IsValid(stream))
            {
                _aimTargetHandle.SetLocalPosition(stream, _targetDefaultPose);
            }
            
            KTransform pose = GetComponentAdsPose(stream, _weaponBoneHandle);
            KTransform additivePose = GetComponentAdsPose(stream, _hipTargetHandle);

            pose.position.x = Mathf.Lerp(pose.position.x, additivePose.position.x, _settings.positionBlend.x);
            pose.position.y = Mathf.Lerp(pose.position.y, additivePose.position.y, _settings.positionBlend.y);
            pose.position.z = Mathf.Lerp(pose.position.z, additivePose.position.z, _settings.positionBlend.z);

            pose.position += _aimPoint.rotation * _aimPoint.position;
            
            Vector3 absQ = GetEuler(pose.rotation);
            Vector3 addQ = GetEuler(additivePose.rotation);

            absQ.x = Mathf.Lerp(absQ.x, addQ.x, _settings.rotationBlend.x);
            absQ.y = Mathf.Lerp(absQ.y, addQ.y, _settings.rotationBlend.y);
            absQ.z = Mathf.Lerp(absQ.z, addQ.z, _settings.rotationBlend.z);

            pose.rotation = Quaternion.Euler(absQ);
            pose.rotation *= _aimPoint.rotation;
            
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _weaponBoneHandle, pose.position, 
                weight * (1f - _settings.cameraBlend));
            KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _weaponBoneHandle, pose.rotation, 
                weight);
            
            if (_settings.cameraBlend > 0f && _aimTargetHandle.IsValid(stream))
            {
                KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _aimTargetHandle, -pose.position, 
                    weight * _settings.cameraBlend);
            }
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = settings as AdsModifierSettings;

            _aimPointTransform = _settings.aimPoint.GetCopy();
            _aimPointTransform.UpdateContext(jobData.animator.gameObject);

            _isAiming = _settings.isAiming.CreateProperty(jobData.animator.gameObject);

            _weaponBone = jobData.skeleton.GetBoneTransform(_settings.weaponBone);
            Transform aimTargetBone = jobData.skeleton.GetBoneTransform(_settings.aimTargetBone);
            _cachedAimPoint = _aimPointTransform.GetValue();
            
            _weaponBoneHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.weaponBone);
            _aimTargetHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.aimTargetBone);
            _hipTargetHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.hipTargetBone);
            
            _targetDefaultPose = aimTargetBone.localPosition;
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
            _jobData.weight = weight;

            bool isAiming = _isAiming.GetValue();

            _aimingWeight += _settings.aimingSpeed * (isAiming ? Time.deltaTime : -Time.deltaTime);
            _aimingWeight = Mathf.Clamp01(_aimingWeight);
            
            _aimPointPlayback = Mathf.Clamp01(_aimPointPlayback + Time.deltaTime * _settings.aimPointSpeed);

            var activeAimPoint = _aimPointTransform.GetValue();
            if (activeAimPoint != _cachedAimPoint)
            {
                _aimPointPlayback = 0f;
                _prevAimPoint = _aimPoint;
                _cachedAimPoint = activeAimPoint;
            }
            
            KTransform aimPoint = GetLocalAimPoint(activeAimPoint);
            _aimPoint = KTransform.EaseLerp(_prevAimPoint, aimPoint, _aimPointPlayback, 
                _settings.aimPointEaseMode);
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