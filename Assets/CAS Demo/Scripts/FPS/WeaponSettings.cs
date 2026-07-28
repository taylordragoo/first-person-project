using System;
using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Attributes;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

namespace CAS_Demo.Scripts.FPS
{
    [Serializable]
    public struct CharacterWeaponAnimation
    {
        public AnimationAsset character;
        public AnimationAsset weapon;
    }
    
    [CreateAssetMenu(fileName = "NewWeaponSettings", menuName = CasNames.Path_Addons + "FPS/Weapon Settings")]
    public class WeaponSettings : ScriptableObject
    {
        [Tab("General")] [Header("Firing")] [Min(0f)]
        public float fireRate = 0;

        public RecoilAnimData recoilData;
        public CharacterCameraShake recoilShake;
        public bool supportsBurst = false;
        public bool supportsAuto = false;
        public int burstRounds = 0;

        [Header("Sounds")] public List<AudioClip> fireSounds;
        public List<AudioClip> animEventSounds;
        
        [Tab("Animation")]
        
        [Header("Offsets")]
        public KTransform weaponPose = KTransform.Identity;
        public KTransform weaponPoseThirdPerson = KTransform.Identity;
        public KTransform weaponPoseThirdPersonRest = KTransform.Identity;
        public KTransform rightHandOffset = KTransform.Identity;
        public KTransform leftHandOffset = KTransform.Identity;
        
        [Header("Sway")]
        public WeaponSway aimingSway = WeaponSway.shooterAimPreset;
        public WeaponSway movementSway = WeaponSway.shooterMovePreset;

        [Header("Animations")] public CharacterWeaponAnimation reload;
        public CharacterWeaponAnimation fire;
        public CharacterWeaponAnimation equip;
        public CharacterWeaponAnimation unEquip;
        public IkMotionSettings equipMotion;
        public IkMotionSettings unEquipMotion;
        
#if UNITY_EDITOR
        public static readonly string WeaponPoseName = nameof(weaponPose);
        public static readonly string WeaponPoseThirdPersonName = nameof(weaponPoseThirdPerson);
        public static readonly string WeaponPoseThirdPersonRestName = nameof(weaponPoseThirdPersonRest);
        public static readonly string RightHandOffsetName = nameof(rightHandOffset);
        public static readonly string LeftHandOffsetName = nameof(leftHandOffset);
        
        public static readonly string AimingSwayName = nameof(aimingSway);
        public static readonly string MovementSway = nameof(movementSway);
#endif
    }
}