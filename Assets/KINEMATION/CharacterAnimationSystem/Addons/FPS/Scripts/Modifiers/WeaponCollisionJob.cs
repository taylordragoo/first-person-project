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
    public struct WeaponCollisionJob : IAnimationJob, IAnimationModifierJob
    {
        private WeaponCollisionSettings _settings;
        private ModifierJobData _jobData;

        private TransformStreamHandle _weaponBoneHandle;
        private KTransform _blockingPose;
        
        private bool _isHit;
        private RaycastHit _hit;
        private Vector3 _direction;
        private Vector3 _start;

        private BindableProperty<float> _pitchInputProp;
        private BindableProperty<float> _weaponLengthProp;

        private float _weaponLength;
        private float _pitchInput; 
        
        public void ProcessAnimation(AnimationStream stream)
        {
            _direction = _weaponBoneHandle.GetRotation(stream) * Vector3.forward;
            _start = _weaponBoneHandle.GetPosition(stream) + _direction * _settings.startOffset;
            
            KTransform target = KTransform.Identity;
            if (_isHit)
            {
                float blockRatio = Mathf.Approximately(_weaponLength, 0f) ? 0f : 1f - _hit.distance / _weaponLength;
                target = KTransform.Lerp(target, _pitchInput > 0f ? _settings.lowReadyPose : _settings.highReadyPose, 
                    blockRatio);
            }
            
            float alpha = KMath.ExpDecayAlpha(_settings.interpSpeed, stream.deltaTime);
            _blockingPose = KTransform.Lerp(_blockingPose, target, alpha);
            
            KPose pose = new KPose()
            {
                modifyMode = EModifyMode.Add,
                pose = _blockingPose,
                space = _settings.targetSpace
            };
            
            KAnimationMath.ModifyTransform(stream, _jobData.rootHandle, _weaponBoneHandle, pose, 
                _jobData.weight);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            _settings = settings as WeaponCollisionSettings;
            
            _weaponBoneHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.weaponBone);
            
            _blockingPose = KTransform.Identity;
            _pitchInputProp = _settings.pitchInput.CreateProperty(jobData.animator.gameObject);
            _weaponLengthProp = _settings.weaponLength.CreateProperty(jobData.animator.gameObject);
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
            var job = playable.GetJobData<WeaponCollisionJob>();
            job._isHit = _isHit;
            job._hit = _hit;

            _start = job._start;
            _direction = job._direction;
            
            job._pitchInput = _pitchInputProp.GetValue();
            job._weaponLength = _weaponLength = _weaponLengthProp.GetValue();
            job._jobData.weight = weight;
            playable.SetJobData(job);
        }

        public void LateUpdate()
        {
            _isHit = Physics.SphereCast(_start, _settings.traceRadius, _direction, out _hit, 
                _weaponLength, _settings.layerMask);
        }

        public void Dispose()
        {
        }

        public void OnDrawGizmos()
        {
            var color = Gizmos.color;
            
            Gizmos.color = Color.red;
            Vector3 target = _start + _direction * _weaponLength;
            Gizmos.DrawLine(_start, target);
            
            if(_isHit) Gizmos.DrawWireSphere(_hit.point, 0.03f);
            
            Gizmos.color = color;
        }

        public void OnSceneGUI()
        {
        }
    }
}