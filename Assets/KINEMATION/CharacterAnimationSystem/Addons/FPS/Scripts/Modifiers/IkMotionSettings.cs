// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    [ScriptableComponentGroup("FPS", "IK Motion")]
    [CreateAssetMenu(fileName = "NewIkMotion", menuName = CasNames.Path_Addons + "FPS/IK Motion")]
    public class IkMotionSettings : AnimationModifierSettings
    {
        public KRigElement boneToAnimate = new KRigElement(-1, CasNames.Bone_IkWeaponBone);

        public VectorCurve rotationCurves = new VectorCurve(new Keyframe[]
        {
            new Keyframe(0f, 0f),
            new Keyframe(1f, 0f)
        });
        
        public VectorCurve translationCurves = new VectorCurve(new Keyframe[]
        {
            new Keyframe(0f, 0f),
            new Keyframe(1f, 0f)
        });

        public Vector3 rotationScale = Vector3.one;
        public Vector3 translationScale = Vector3.one;
        
        [Min(0f)] public float blendTime = 0f;
        [Min(0f)] public float playRate = 1f;
        public bool autoBlendOut = true;

        public float GetLength()
        {
            float baseLength = Mathf.Max(rotationCurves.GetCurveLength(), translationCurves.GetCurveLength());
            return baseLength / (Mathf.Approximately(playRate, 0f) ? 1f : playRate);
        }

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new IkMotionJob();
        }
    }
}