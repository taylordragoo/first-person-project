// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    [ScriptableComponentGroup("FPS", "Weapon Collision")]
    public class WeaponCollisionSettings : AnimationModifierSettings
    {
        public KRigElement weaponBone = new KRigElement(-1, CasNames.Bone_IkWeaponBone);
        public BindableProperty<float> pitchInput = new BindableProperty<float>(0f);
        
        [Header("Ready Poses")]
        public KTransform highReadyPose = KTransform.Identity;
        public KTransform lowReadyPose = KTransform.Identity;
        public ESpaceType targetSpace = ESpaceType.ComponentSpace;
        [Min(0f)] public float interpSpeed = 0f;
        
        [Header("Tracing")]
        public LayerMask layerMask;
        public BindableProperty<float> weaponLength = new BindableProperty<float>(1f);
        public float startOffset = 0f;
        public float traceRadius = 0.05f;
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new WeaponCollisionJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            pitchInput.Initialize(characterPrefab);
            weaponLength.Initialize(characterPrefab);
        }
    }
}