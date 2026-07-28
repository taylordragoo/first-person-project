// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.PropertyBindings.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    public struct CachedAttachmentPose
    {
        public List<KTransform> fingerPoses;
    }
    
    public struct AttachHandModifierJob : IAnimationJob, IAnimationModifierJob
    {
        private AttachHandModifierSettings _settings;
        private ModifierJobData _jobData;
        
        private TransformStreamHandle _ikHandBoneHandle;
        private TransformStreamHandle _defaultHandBoneHandle;
        private TransformStreamHandle _ikWeaponBoneHandle;

        private BindableProperty<AnimationClip> _handPoseProp;
        private BindableProperty<Transform> _attachTargetProp;

        private Transform _weaponTransform;
        
        private AnimationClip _prevHandPose;
        
        private NativeArray<AnimationStreamBoneTransform> _fingerPoses;
        private List<Transform> _fingers;

        private static Dictionary<AnimationClip, CachedAttachmentPose> _cachedAttachmentPoses;
        private CachedAttachmentPose _attachmentPose;
        private KTransform _handAttachmentPose;
        private bool _useDefaultHandPose;

        private void SampleHandPose(AnimationClip newPose)
        {
            if (newPose == null) return;
            
            // Check if this animation clip was already sampled.
            if (_cachedAttachmentPoses.TryGetValue(newPose, out _attachmentPose))
            {
                for (int i = 0; i < _fingerPoses.Length; i++)
                {
                    var boneLocalPose = _fingerPoses[i];
                    boneLocalPose.transform = _attachmentPose.fingerPoses[i];
                    _fingerPoses[i] = boneLocalPose;
                }

                return;
            }

            // Sample the clip and compute hand poses.
            newPose.SampleAnimation(_jobData.animator.gameObject, 0f);
            
            _attachmentPose.fingerPoses = new List<KTransform>();
            
            for (int i = 0; i < _fingerPoses.Length; i++)
            {
                _attachmentPose.fingerPoses.Add(new KTransform(_fingers[i], false));
                
                var boneLocalPose = _fingerPoses[i];
                boneLocalPose.transform = _attachmentPose.fingerPoses[i];
                _fingerPoses[i] = boneLocalPose;
            }

            // Add the new pose to the pool.
            _cachedAttachmentPoses.TryAdd(newPose, _attachmentPose);
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (!_ikHandBoneHandle.IsValid(stream) || !KAnimationMath.IsWeightRelevant(_jobData.weight))
            {
                return;
            }

            foreach (var bonePose in _fingerPoses)
            {
                if (!bonePose.handle.IsValid(stream)) continue;

                KTransform basePose = KAnimationMath.GetTransform(stream, bonePose.handle, false);
                basePose = KTransform.Lerp(basePose, bonePose.transform, _jobData.weight);
                bonePose.handle.SetLocalRotation(stream, basePose.rotation);
                bonePose.handle.SetLocalPosition(stream, basePose.position);
            }
            
            KTransform targetTransform;

            if (_useDefaultHandPose)
            {
                targetTransform = KAnimationMath.GetTransform(stream, _defaultHandBoneHandle);
            }
            else
            {
                KTransform weaponTransform = KAnimationMath.GetTransform(stream, _ikWeaponBoneHandle);
                targetTransform = weaponTransform.GetWorldTransform(_handAttachmentPose, false);
            }

            KTransform baseTransform = KAnimationMath.GetTransform(stream, _ikHandBoneHandle);
            targetTransform = KTransform.Lerp(baseTransform, targetTransform, _jobData.weight);
            
            _ikHandBoneHandle.SetPosition(stream, targetTransform.position);
            _ikHandBoneHandle.SetRotation(stream, targetTransform.rotation);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _settings = settings as AttachHandModifierSettings;
            _jobData = jobData;
            
            _ikHandBoneHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.ikHandBone);
            _defaultHandBoneHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.defaultHandBone);
            _ikWeaponBoneHandle = AnimationModifierUtility.GetHandle(in jobData, _settings.ikWeaponBone);

            _handPoseProp = _settings.handPose.CreateProperty(jobData.animator.gameObject);
            _attachTargetProp = _settings.attachTransform.CreateProperty(jobData.animator.gameObject);

            _weaponTransform = jobData.skeleton.GetBoneTransform(_settings.ikWeaponBone);

            int count = _settings.fingers.elementChain.Count;
            _fingerPoses = new NativeArray<AnimationStreamBoneTransform>(count, Allocator.Persistent);
            _fingers = new List<Transform>();
            
            for (int i = 0; i < count; i++)
            {
                var element = _settings.fingers.elementChain[i];

                _fingerPoses[i] = new AnimationStreamBoneTransform()
                {
                    handle = AnimationModifierUtility.GetHandle(in jobData, element),
                    transform = KTransform.Identity,
                };
                
                _fingers.Add(jobData.skeleton.GetBoneTransform(element));
            }

            if (_cachedAttachmentPoses == null) _cachedAttachmentPoses = new();

            _handAttachmentPose = KTransform.Identity;
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

            Transform transform = _attachTargetProp.GetValue();
            _useDefaultHandPose = transform == null;

            if (_useDefaultHandPose)
            {
                _handAttachmentPose = KTransform.Identity;
            }
            else
            {
                _handAttachmentPose = new KTransform(transform);
                _handAttachmentPose = new KTransform(_weaponTransform).GetRelativeTransform(_handAttachmentPose,
                    false);
            }

            // If clip has changed, try sampling the pose.
            AnimationClip activeHandPose = _handPoseProp.GetValue();
            if (activeHandPose != _prevHandPose)
            {
                SampleHandPose(activeHandPose);
                _prevHandPose = activeHandPose;
            }
            
            playable.SetJobData(this);
        }

        public void LateUpdate()
        {
        }

        public void Dispose()
        {
            if (_fingerPoses.IsCreated) _fingerPoses.Dispose();
        }

        public void OnDrawGizmos()
        {
        }

        public void OnSceneGUI()
        {
        }
    }
}