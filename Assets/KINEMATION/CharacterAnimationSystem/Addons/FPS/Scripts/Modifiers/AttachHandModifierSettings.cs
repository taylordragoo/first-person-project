// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    [ScriptableComponentGroup("FPS", "Attach Hand Modifier")]
    public class AttachHandModifierSettings : AnimationModifierSettings
    {
        public KRigElement defaultHandBone = new KRigElement(-1, CasNames.Bone_IkHandLeft_Right);
        public KRigElement ikHandBone = new KRigElement(-1, CasNames.Bone_IkHandLeft);
        public KRigElement ikWeaponBone = new KRigElement(-1, CasNames.Bone_IkWeaponBone);
        
        [CustomElementChainDrawer(true, false)]
        public KRigElementChain fingers = new KRigElementChain();
        public BindableProperty<AnimationClip> handPose = new (null);
        public BindableProperty<Transform> attachTransform = new(null);
        
        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new AttachHandModifierJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            attachTransform.Initialize(characterPrefab);
            handPose.Initialize(characterPrefab);
        }
    }
}