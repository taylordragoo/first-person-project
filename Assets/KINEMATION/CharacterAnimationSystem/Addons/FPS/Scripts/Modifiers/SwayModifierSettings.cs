// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using KINEMATION.Shared.PropertyBindings.Runtime;
using KINEMATION.Shared.ScriptableWidget.Runtime;
using UnityEngine;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers
{
    [Serializable]
    public struct VectorSpring
    {
        public Vector3 damping;
        public Vector3 stiffness;
        public Vector3 speed;
        public Vector3 scale;
        public Vector3 clamp;

        public static VectorSpring identity = new VectorSpring()
        {
            damping = Vector3.zero,
            stiffness = Vector3.zero,
            speed = Vector3.zero,
            scale = Vector3.zero,
            clamp = Vector3.zero
        };
    }

    [Serializable]
    public struct WeaponSway
    {
        public VectorSpring position;
        public VectorSpring rotation;
        public float dampingFactor;
        public ESpaceType space;
        [Range(0f, 1f)] public float adsScale;

        public static WeaponSway identity = new WeaponSway()
        {
            position = VectorSpring.identity,
            rotation = VectorSpring.identity,
            dampingFactor = 0f,
            space = ESpaceType.ComponentSpace
        };

        public static WeaponSway shooterAimPreset = new WeaponSway()
        {
            position = new VectorSpring()
            {
                damping = new Vector3(0.4f, 0.4f, 0.4f),
                stiffness = new Vector3(0.4f, 0.4f, 0.8f),
                speed = new Vector3(7f, 7f, 7f),
                scale = new Vector3(1f, 1f, 1f),
                clamp = new Vector3(1f, 1f, 1f)
            },
            rotation = new VectorSpring()
            {
                damping = new Vector3(0.4f, 0.4f, 0.3f),
                stiffness = new Vector3(0.8f, 0.8f, 0.8f),
                speed = new Vector3(15f, 20f, 15f),
                scale = new Vector3(-2f, 2f, -2f),
                clamp = new Vector3(1f, 1f, 1f)
            },
            dampingFactor = 8f,
            space = ESpaceType.ComponentSpace
        };
        
        public static WeaponSway shooterMovePreset = new WeaponSway()
        {
            position = new VectorSpring()
            {
                damping = new Vector3(0.4f, 0.4f, 0.4f),
                stiffness = new Vector3(0.8f, 0.8f, 0.8f),
                speed = new Vector3(7f, 7f, 7f),
                scale = new Vector3(1f, 0f, 1f),
            },
            rotation = new VectorSpring()
            {
                damping = new Vector3(0.4f, 0.4f, 0.4f),
                stiffness = new Vector3(0.8f, 0.8f, 0.8f),
                speed = new Vector3(12f, 12f, 12f),
                scale = new Vector3(2f, 2f, -2f),
            },
            dampingFactor = 8f,
            space = ESpaceType.ComponentSpace
        };
    }
    
    [ScriptableComponentGroup("FPS", "Sway Modifier")]
    public class SwayModifierSettings : AnimationModifierSettings
    {
        [Header("Bones")]
        public KRigElement weaponBone = new KRigElement(-1, CasNames.Bone_IkWeaponBone);
        public KRigElement weaponAdditiveBone = new KRigElement(-1, CasNames.Bone_WeaponBoneAdditive);
        
        [Header("Inputs")]
        public BindableProperty<Vector2> deltaLookInput = new (Vector2.zero);
        public BindableProperty<Vector2> moveInput = new (Vector2.zero);
        public BindableProperty<bool> isAiming = new BindableProperty<bool>(false);

        [Header("Spring Sway")]
        public BindableProperty<WeaponSway> aimingSway = new (WeaponSway.shooterAimPreset);
        public BindableProperty<WeaponSway> movementSway = new (WeaponSway.shooterMovePreset);
        
        [Header("Curve Animation")]
        public Quaternion spaceOffset = Quaternion.identity;
        [Range(0f, 1f)] public float adsCurveScale = 1f;
        [Min(0f)] public float adsCurveSmoothing = 10f;

        public override IAnimationModifierJob CreateAnimationJob()
        {
            return new SwayModifierJob();
        }

        public override void InitializeOnLoad()
        {
            base.InitializeOnLoad();
            isAiming.Initialize(characterPrefab);
            
            deltaLookInput.Initialize(characterPrefab);
            moveInput.Initialize(characterPrefab);
            
            aimingSway.Initialize(characterPrefab);
            movementSway.Initialize(characterPrefab);
        }
    }
}