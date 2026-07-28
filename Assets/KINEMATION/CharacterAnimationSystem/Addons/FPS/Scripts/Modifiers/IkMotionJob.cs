// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    public struct IkMotionJob : IAnimationJob, IAnimationModifierJob
    {
        private IkMotionSettings _settings;
        private ModifierJobData _jobData;
        
        private KTransform _result;
        private KTransform _cachedResult;
        
        private bool _isPlaying;
        private float _playback;
        
        // Animation Job
        private TransformStreamHandle _boneToAnimate;
        private KTransform _animation;
        private float _length;
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!_boneToAnimate.IsValid(stream) || !_isPlaying || !KAnimationMath.IsWeightRelevant(_jobData.weight))
            {
                return;
            }
            
            if (_isPlaying)
            {
                float blendAlpha = 1f;
                if (!Mathf.Approximately(_settings.blendTime, 0f))
                {
                    blendAlpha = Mathf.Clamp01(_playback / _settings.blendTime);
                }

                Vector3 value = _settings.rotationCurves.GetValue(_playback);
                value.x *= _settings.rotationScale.x;
                value.y *= _settings.rotationScale.y;
                value.z *= _settings.rotationScale.z;

                _animation.rotation = Quaternion.Euler(value);
                
                value = _settings.translationCurves.GetValue(_playback);
                value.x *= _settings.translationScale.x;
                value.y *= _settings.translationScale.y;
                value.z *= _settings.translationScale.z;
                
                _animation.position = value;
            
                // Blend between the cache and current value.
                _result.rotation = Quaternion.Slerp(_cachedResult.rotation, _animation.rotation, blendAlpha);
                _result.position = Vector3.Lerp(_cachedResult.position, _animation.position, blendAlpha);
            }
            
            KAnimationMath.MoveInSpace(stream, _jobData.rootHandle, _boneToAnimate, _result.position, 
                _jobData.weight);
            KAnimationMath.RotateInSpace(stream, _jobData.rootHandle, _boneToAnimate, _result.rotation,
                _jobData.weight);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _jobData = jobData;
            
            _settings = settings as IkMotionSettings;
            _boneToAnimate = AnimationModifierUtility.GetHandle(in jobData, _settings.boneToAnimate);
            _result = KTransform.Identity;

            OnModifierUpdated(_settings);
        }

        public void OnModifierUpdated(AnimationModifierSettings newSettings)
        {
            _settings = newSettings as IkMotionSettings;
            
            _isPlaying = true;
            _cachedResult = _result;
            _playback = 0f;

            _length = Mathf.Max(_settings.rotationCurves.GetCurveLength(), 
                _settings.translationCurves.GetCurveLength());
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
            var job = playable.GetJobData<IkMotionJob>();
            _playback = Mathf.Clamp(_playback + Time.deltaTime * _settings.playRate, 0f, _length);
            
            if (Mathf.Approximately(_playback, 1f) && _settings.autoBlendOut) _isPlaying = false;
            
            _jobData.weight = weight;
            _animation = job._animation;
            _result = job._result;
            
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