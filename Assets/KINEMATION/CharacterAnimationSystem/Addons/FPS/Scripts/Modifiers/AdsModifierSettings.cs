// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    [Serializable]
    public struct AdsBlend
    {
        [Range(0f, 1f)] public float x;
        [Range(0f, 1f)] public float y;
        [Range(0f, 1f)] public float z;
    }
    
    [ScriptableComponentGroup("FPS", "Ads Modifier")]
    public class AdsModifierSettings : AnimationModifierSettings
    {
        [Header("Inputs")]
        public BindableProperty<Transform> aimPoint = new (null);
        public BindableProperty<bool> isAiming = new(false);
        
        [Header("Bones")]
        [Tooltip("Main weapon/ik bone.")]
        public KRigElement weaponBone = new KRigElement(-1, CasNames.Bone_IkWeaponBone);
        [Tooltip("Dynamic bone that has the default idle/hip weapon pose.")]
        public KRigElement hipTargetBone = new KRigElement(-1, CasNames.Bone_IkWeaponBoneAim);
        [Tooltip("Aim target bone (e.g. camera socket).")]
        public KRigElement aimTargetBone = new KRigElement(-1, CasNames.Bone_AimSocket);
        
        [Header("ADS Blends")]
        [Tooltip("Blends between absolute and additive translation aiming.")]
        public AdsBlend positionBlend;
        [Tooltip("Blends between absolute and additive rotation aiming.")]
        public AdsBlend rotationBlend;
        
        [Min(0.01f)] public float aimingSpeed = 1f;
        public EaseMode aimingEaseMode;
        
        [Min(0.01f)] public float aimPointSpeed = 1f;
        public EaseMode aimPointEaseMode;
        
        [Tooltip("If 0, weapon will be aligned with the camera. If 1, camera will be moved instead.")]
        [Range(0f, 1f)] public float cameraBlend = 0f;

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new AdsModifierJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            aimPoint.Initialize(characterPrefab);
            isAiming.Initialize(characterPrefab);
        }
    }
}