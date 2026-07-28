// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    public struct FPSCopyBonesJob : IAnimationJob, IAnimationModifierJob
    {
        private FPSCopyBonesSettings _settings;
        private ModifierJobData _jobData;

        private TransformStreamHandle _sourceWeaponBoneHandle;
        private TransformStreamHandle _sourceHandRightHandle;
        private TransformStreamHandle _sourceHandLeftHandle;
        
        private TransformStreamHandle _ikWeaponBoneHandle;
        private TransformStreamHandle _ikWeaponBoneAimHandle;
        private TransformStreamHandle _ikHandRightHandle;
        private TransformStreamHandle _ikHandLeftHandle;

#if UNITY_EDITOR
        private Transform _ikWeaponBoneReference;
#endif

        private BindableProperty<KTransform> _weaponOffsetProp;
        private KTransform _weaponOffset;

        private void CopyBone(AnimationStream stream, TransformStreamHandle from, TransformStreamHandle to)
        {
            if (!from.IsValid(stream) || !to.IsValid(stream)) return;
            KAnimationMath.CopyBone(stream, from, to, _jobData.weight);
        }
        
        public void ProcessAnimation(AnimationStream stream)
        {
            if (Mathf.Approximately(_jobData.weight, 0f)) return;
            
            CopyBone(stream, _sourceWeaponBoneHandle, _ikWeaponBoneHandle);
            
            if (_ikWeaponBoneHandle.IsValid(stream))
            {
                // 1. Move First (Offset applied in Source Rotation Frame)
                KAnimationMath.MoveInSpace(stream, _ikWeaponBoneHandle, _ikWeaponBoneHandle,
                    _weaponOffset.position, _jobData.weight);
                
                // 2. Rotate Second (Rotation applied on top)
                KAnimationMath.RotateInSpace(stream, _ikWeaponBoneHandle, _ikWeaponBoneHandle,
                    _weaponOffset.rotation, _jobData.weight);

                if (_ikWeaponBoneAimHandle.IsValid(stream))
                {
                    KAnimationMath.MoveInSpace(stream, _ikWeaponBoneAimHandle, _ikWeaponBoneAimHandle,
                        _weaponOffset.position, _jobData.weight);
                    KAnimationMath.RotateInSpace(stream, _ikWeaponBoneAimHandle, _ikWeaponBoneAimHandle,
                        _weaponOffset.rotation, _jobData.weight);
                }
            }
            
            CopyBone(stream, _sourceHandRightHandle, _ikHandRightHandle);
            CopyBone(stream, _sourceHandLeftHandle, _ikHandLeftHandle);
        }

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void Initialize(ModifierJobData jobData, AnimationModifierSettings settings)
        {
            _settings = settings as FPSCopyBonesSettings;
            _jobData = jobData;

            _sourceWeaponBoneHandle = AnimationModifierUtility.GetHandle(jobData, _settings.sourceWeaponBone);
            _sourceHandRightHandle = AnimationModifierUtility.GetHandle(jobData, _settings.sourceRightHand);
            _sourceHandLeftHandle = AnimationModifierUtility.GetHandle(jobData, _settings.sourceLeftHand);
            
            _ikWeaponBoneHandle = AnimationModifierUtility.GetHandle(jobData, _settings.ikWeaponBone);
            _ikWeaponBoneAimHandle = AnimationModifierUtility.GetHandle(jobData, _settings.ikWeaponBoneAim);
            _ikHandRightHandle = AnimationModifierUtility.GetHandle(jobData, _settings.ikHandRight);
            _ikHandLeftHandle = AnimationModifierUtility.GetHandle(jobData, _settings.ikHandLeft);

            _weaponOffsetProp = _settings.weaponBoneOffset.CreateProperty(jobData.animator.gameObject);
            _weaponOffset = KTransform.Identity;

#if UNITY_EDITOR
            _ikWeaponBoneReference = jobData.skeleton.GetBoneTransform(_settings.ikWeaponBone.name);
#endif
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
#if UNITY_EDITOR
            if (_settings == null || _jobData.animator == null || _ikWeaponBoneReference == null) return;
            if (_weaponOffsetProp == null || _weaponOffsetProp.IsBound) return;
            
            // 1. Get the Current Final World State
            Vector3 finalPos = _ikWeaponBoneReference.position;
            Quaternion finalRot = _ikWeaponBoneReference.rotation;
            
            KTransform currentOffset = _weaponOffset;

            // 2. Reconstruct the "Source" (Pre-Modified) State
            // We "Un-apply" the offset to find where the bone was BEFORE the job modified it.
            
            // Reconstruct Source Rotation:
            // FinalRot = SourceRot * OffsetRot  =>  SourceRot = FinalRot * Inverse(OffsetRot)
            Quaternion sourceRot = finalRot * Quaternion.Inverse(currentOffset.rotation);
            
            // Reconstruct Source Position:
            // FinalPos = SourcePos + (SourceRot * OffsetPos)  =>  SourcePos = FinalPos - (SourceRot * OffsetPos)
            Vector3 sourcePos = finalPos - (sourceRot * currentOffset.position);

            EditorGUI.BeginChangeCheck();
            
            Vector3 newPos = finalPos;
            Quaternion newRot = finalRot;

            if (Tools.current == Tool.Move)
            {
                // Draw handle at current bone position, but aligned to Source Rotation 
                // (This keeps the axes stable like a "Sliding Rail")
                newPos = Handles.PositionHandle(finalPos, finalRot);
            }
            else if (Tools.current == Tool.Rotate)
            {
                newRot = Handles.RotationHandle(finalRot, finalPos);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_settings, "Weapon Bone Offset");

                // 3. Calculate New Offsets based on the reconstructed Source
                
                // New Rotation Offset:
                // NewRot = SourceRot * NewOffsetRot => NewOffsetRot = Inverse(SourceRot) * NewRot
                Quaternion newOffsetRot = Quaternion.Inverse(sourceRot) * newRot;
                newOffsetRot.Normalize();

                // New Position Offset:
                // NewPos = SourcePos + (SourceRot * NewOffsetPos) => NewOffsetPos = Inverse(SourceRot) * (NewPos - SourcePos)
                Vector3 moveDelta = newPos - sourcePos;
                Vector3 newOffsetPos = Quaternion.Inverse(sourceRot) * moveDelta;

                // 4. Apply
                KTransform newOffset = new KTransform
                {
                    position = newOffsetPos,
                    rotation = newOffsetRot,
                    scale = currentOffset.scale
                };
                
                _settings.weaponBoneOffset.SetDefaultValue(newOffset);
                EditorUtility.SetDirty(_settings);
            }
#endif
        }
    }
}