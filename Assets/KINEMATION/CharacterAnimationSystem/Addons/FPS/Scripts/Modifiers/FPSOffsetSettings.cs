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
    [ScriptableComponentGroup("FPS", "FPS Offset")]
    public class FPSOffsetSettings : AnimationModifierSettings
    {
        [Header("Bones")]
        public KRigElement ikWeaponBone = new KRigElement(-1, CasNames.Bone_IkWeaponBone);
        public KRigElement ikWeaponBoneAim = new KRigElement(-1, CasNames.Bone_IkWeaponBoneAim);
        public KRigElement ikHandRight = new KRigElement(-1, CasNames.Bone_IkHandRight);
        public KRigElement ikHandLeft = new KRigElement(-1, CasNames.Bone_IkHandLeft);
        
        [Header("Offsets")]
        public BindableProperty<KTransform> weaponBoneOffset = new (KTransform.Identity);
        public BindableProperty<KTransform> rightHandOffset = new (KTransform.Identity);
        public BindableProperty<KTransform> leftHandOffset = new (KTransform.Identity);
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new FPSOffsetJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            
            weaponBoneOffset.Initialize(characterPrefab);
            rightHandOffset.Initialize(characterPrefab);
            leftHandOffset.Initialize(characterPrefab);
        }
    }
}