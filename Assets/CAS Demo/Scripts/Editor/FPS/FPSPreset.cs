using CAS_Demo.Scripts.FPS;
using CAS_Demo.Scripts.FPS.Attachments;

using KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers;
using KINEMATION.CharacterAnimationSystem.Examples.Scripts.Editor;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.BoneLookups;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.Setup;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;

using KINEMATION.Shared.KAnimationCore.Runtime.Rig;

using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEditor;
using UnityEngine;

namespace CAS_Demo.Scripts.Editor.FPS
{
    [CustomSetupName("FPS (Beginner-friendly)")]
    public class FPSPreset : BasicCasPreset
    {
        protected SetupRigElementProperty _headBone;
        protected SetupRigElementProperty _weaponBone;
        protected SetupRigElementChainProperty _leftHandFingers;

        struct FPSBoneTransforms
        {
            public Transform head;
            public Transform rightHand;
            public Transform leftHand;

            public Transform rightFoot;
            public Transform leftFoot;
            
            public Transform ikWeaponBone;
            public Transform ikWeaponBoneAim;
            public Transform ikWeaponBoneRight;
            public Transform ikHandLeftRight;
            
            public Transform aimSocket;
        }
        
        public override void Initialize()
        {
            _headBone = ScriptableObject.CreateInstance<SetupRigElementProperty>();
            _weaponBone = ScriptableObject.CreateInstance<SetupRigElementProperty>();
            _leftHandFingers = ScriptableObject.CreateInstance<SetupRigElementChainProperty>();
            
            base.Initialize();
            
            _headBone.Initialize(setupData.skeleton, "Head Bone");
            _weaponBone.Initialize(setupData.skeleton, "Weapon Bone");
            _leftHandFingers.Initialize(setupData.skeleton, "Left Hand Fingers");
        }

        public override void UpdateBoneReferences()
        {
            _headBone.element = new KRigElement(-1);
            _weaponBone.element = new KRigElement(-1);
            _leftHandFingers.chain = new KRigElementChain();
            
            base.UpdateBoneReferences();
            
            var lookupPreset = _layeredBlendingWidget.GetSelectedPreset();
            foreach (var chain in setupData.skeleton.ElementChains)
            {
                if (!BoneLookupUtility.IsNameMatching(chain.chainName, lookupPreset.GetLeftHandFingersLookups()))
                {
                    continue;
                }

                foreach (var element in chain.elementChain) _leftHandFingers.chain.elementChain.Add(element);
            }
        }

        protected override bool FindMatchingBones(CharacterSkeletonBone skeletonBone)
        {
            if (base.FindMatchingBones(skeletonBone)) return true;
            
            var lookupPreset = _layeredBlendingWidget.GetSelectedPreset();
            
            if (BoneLookupUtility.IsNameMatching(skeletonBone.rigElement.name, lookupPreset.GetHeadLookups()))
            {
                _headBone.element = skeletonBone.rigElement;
                return true;
            }

            return false;
        }

        protected override bool ValidateBoneProperties()
        {
            if (!base.ValidateBoneProperties()) return false;

            _headBone.DrawProperty();
            if (_headBone.element.index == -1)
            {
                EditorGUILayout.HelpBox("Select head bone!", MessageType.Warning);
                return false;
            }
            
            _weaponBone.DrawProperty();
            if (_weaponBone.element.index == -1)
            {
                EditorGUILayout.HelpBox("Select weapon bone (if there's one)!", MessageType.Info);
            }
            
            _leftHandFingers.DrawProperty();
            if (_leftHandFingers.chain.elementChain.Count == 0)
            {
                EditorGUILayout.HelpBox("Select left hand fingers!", MessageType.Info);
                return false;
            }
            
            return true;
        }

        public override void Apply()
        {
            GameObject character = CreateOrGetCharacterGameObject();
            
            var skeleton = setupData.skeleton;
            FPSBoneTransforms bones = new FPSBoneTransforms();

            bones.head = skeleton.GetBoneTransform(_headBone.element.name);
            
            bones.rightHand = skeleton.GetBoneTransform(_rightHand.element.name);
            bones.leftHand = skeleton.GetBoneTransform(_leftHand.element.name);
            bones.rightFoot = skeleton.GetBoneTransform(_rightFoot.element.name);
            bones.leftFoot = skeleton.GetBoneTransform(_leftFoot.element.name);

            bones.ikWeaponBone = AddOrGetTransform(bones.head, CasNames.Bone_IkWeaponBone);
            bones.ikWeaponBoneAim = AddOrGetTransform(bones.head, CasNames.Bone_IkWeaponBoneAim);
            
            bones.aimSocket = AddOrGetTransform(bones.head, CasNames.Bone_AimSocket);
            AddOrGetTransform(bones.ikWeaponBone, CasNames.Bone_IkHandRight);
            AddOrGetTransform(bones.ikWeaponBone, CasNames.Bone_IkHandLeft);
            bones.ikWeaponBoneRight = AddOrGetTransform(bones.rightHand, CasNames.Bone_IkWeaponBoneRight);
            bones.ikHandLeftRight = AddOrGetTransform(bones.rightHand, CasNames.Bone_IkHandLeft_Right);

            var root = setupData.skeleton.SkeletonBones[0].transform;
            AddOrGetTransform(root, CasNames.Bone_IkFootRight);
            AddOrGetTransform(root, CasNames.Bone_IkFootLeft);
            
            SetupDynamicBones(bones);
            
            SetupCharacterController<FPSExampleController>(character);
            SetupCharacterCamera(character, out var camera);

            camera.firstPersonSocket = bones.aimSocket;
            CreateAndSaveAssets(out var cas, out var pas);

            AddCopyHandsModifier(pas, bones.ikWeaponBoneRight);
            AddCopyFeetModifiers(pas, new KRigElement(-1, bones.rightFoot.name), 
                new KRigElement(-1, bones.leftFoot.name));
            
            AddAttachHandModifier(pas);
            AddOffsetModifiers(pas, camera);
            
            AddAdsModifier(pas);
            AddSwayModifier(pas);
            AddRecoilModifier(character, pas);
            AddIkMotionModifier(pas);
            
            AddAdditiveLean(pas, _spineBoneChain.chain);
            AddLookModifier(pas, _spineBoneChain.chain);
            
            AddStrideModifier(pas);
            AddFootIkModifier(pas);
            AddStepModifier(pas);
            
            AddFullBodyIK(pas, bones);
            
            setupData.skeleton.UpdateSkeleton();
            _layeredBlendingWidget.CreateBoneChains(setupData.layeredBlending);

            var chain = setupData.layeredBlending.layeredBlends[^1].layer.elementChain;
            chain.Add(new KRigElement(-1, bones.ikWeaponBoneRight.name));
            chain.Add(new KRigElement(-1, bones.ikWeaponBoneAim.name));
            chain.Add(new KRigElement(-1, bones.ikHandLeftRight.name));
            
            SaveCharacterPrefabs(character, pas);
        }

        private void SetupDynamicBones(FPSBoneTransforms bones)
        {
            var weaponTarget = setupData.skeleton.GetBoneTransform(_weaponBone.element.name);
            weaponTarget = weaponTarget == null ? bones.rightHand : weaponTarget;
            
            var aim = CharacterAnimationSetup.AddOrGetComponent<DynamicBone>(bones.ikWeaponBoneAim.gameObject);
            aim.target = weaponTarget;
            aim.updateMode = BlendMode.PreserveOverlay;

            var right = CharacterAnimationSetup.AddOrGetComponent<DynamicBone>(bones.ikWeaponBoneRight.gameObject);
            right.target = weaponTarget;
            right.updateMode = BlendMode.Default;
            
            var attach = CharacterAnimationSetup.AddOrGetComponent<DynamicBone>(bones.ikHandLeftRight.gameObject);
            attach.target = bones.leftHand;
            attach.updateMode = BlendMode.Default;
        }

        private void AddCopyHandsModifier(ProceduralAnimationSettings pas, Transform weaponBoneRight)
        {
            FPSCopyBonesSettings copyBones = ScriptableObject.CreateInstance<FPSCopyBonesSettings>();
            copyBones.name = "FPS Copy Bones";
            
            copyBones.sourceRightHand = _rightHand.element;
            copyBones.sourceLeftHand = _leftHand.element;
            copyBones.sourceWeaponBone =
                weaponBoneRight == null ? _rightHand.element : new KRigElement(-1, weaponBoneRight.name);
            AddModifier(pas, copyBones);
        }

        private void AddAttachHandModifier(ProceduralAnimationSettings pas)
        {
            AttachHandModifierSettings modifier = ScriptableObject.CreateInstance<AttachHandModifierSettings>();
            modifier.name = "Attach Left Hand";
            
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(1f),
                weightModifier = new WeightModifier(0f, 0f, true)
            });
            modifier.weightOverrides[0].weight.Bind(typeof(LayeredBlendingComponent), 
                nameof(setupData.layeredBlending.Mask_Attach_Hand) + ".value");

            string weaponPath = $"{FPSExampleController.GetWeaponItemName}";
            
            modifier.handPose.Bind(typeof(FPSExampleController), 
                $"{weaponPath}.{WeaponProp.GetGripDataName}.{GripAttachmentData.PoseName}");
            modifier.attachTransform.Bind(typeof(FPSExampleController), 
                $"{weaponPath}.{WeaponProp.GetGripDataName}.{GripAttachmentData.IkTargetName}");
            modifier.fingers = _leftHandFingers.chain.GetCopy();
            
            AddModifier(pas, modifier);
        }

        private void AddOffsetModifiers(ProceduralAnimationSettings pas, CharacterCamera camera)
        {
            FPSOffsetSettings modifier = ScriptableObject.CreateInstance<FPSOffsetSettings>();
            modifier.name = "FP Offsets";

            string weaponSettingsPath = $"{FPSExampleController.GetWeaponItemName}.{WeaponProp.WeaponSettingsName}";
            
            modifier.weaponBoneOffset.Bind(typeof(FPSExampleController), 
                $"{weaponSettingsPath}.{WeaponSettings.WeaponPoseName}");
            modifier.rightHandOffset.Bind(typeof(FPSExampleController), 
                $"{weaponSettingsPath}.{WeaponSettings.RightHandOffsetName}");
            modifier.leftHandOffset.Bind(typeof(FPSExampleController), 
                $"{weaponSettingsPath}.{WeaponSettings.LeftHandOffsetName}");
            
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(1f),
                weightModifier = new WeightModifier(0f, 0f, true)
            });
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(1f),
                weightModifier = new WeightModifier(1f, 2f, true)
            });
            
            modifier.weightOverrides[0].weight.Bind(typeof(CharacterCamera), nameof(camera.ViewWeight));
            modifier.weightOverrides[1].weight.Bind(typeof(FPSExampleController), nameof(_controller.Gait));
            
            AddModifier(pas, modifier);
            
            modifier = ScriptableObject.CreateInstance<FPSOffsetSettings>();
            modifier.name = "TP Offset";
            modifier.weaponBoneOffset.Bind(typeof(FPSExampleController), 
                $"{weaponSettingsPath}.{WeaponSettings.WeaponPoseThirdPersonName}");
            
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(1f),
                weightModifier = new WeightModifier(0f, 0f, false)
            });
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(1f),
                weightModifier = new WeightModifier(0f, 0f, false)
            });
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(1f),
                weightModifier = new WeightModifier(1f, 2f, true)
            });
            modifier.weightOverrides[0].weight.Bind(typeof(CharacterCamera), nameof(camera.ViewWeight));
            modifier.weightOverrides[1].weight.Bind(typeof(FPSExampleController), 
                nameof(_controller.AimingWeight));
            modifier.weightOverrides[2].weight.Bind(typeof(FPSExampleController), 
                nameof(_controller.Gait));
            
            AddModifier(pas, modifier);
            
            modifier = ScriptableObject.CreateInstance<FPSOffsetSettings>();
            modifier.name = "TP Rest Offset";
            modifier.weaponBoneOffset.Bind(typeof(FPSExampleController), 
                $"{weaponSettingsPath}.{WeaponSettings.WeaponPoseThirdPersonRestName}");
            
            modifier.weightOverrides.Add(new WeightOverride()
            {
                weight = new BindableProperty<float>(1f),
                weightModifier = new WeightModifier(0f, 0f, true)
            });
            modifier.weightOverrides[0].weight.Bind(typeof(FPSExampleController), 
                nameof(_controller.AimingWeight));
            AddModifier(pas, modifier);
        }

        private void AddAdsModifier(ProceduralAnimationSettings pas)
        {
            var modifier = ScriptableObject.CreateInstance<AdsModifierSettings>();
            modifier.name = "Ads Modifier";

            string weaponPath = $"{FPSExampleController.GetWeaponItemName}";
            
            modifier.aimPoint.Bind(typeof(FPSExampleController), 
                $"{weaponPath}.{WeaponProp.GetScopeDataName}.{ScopeAttachmentData.AimPointName}");
            
            modifier.isAiming.Bind(typeof(FPSExampleController), nameof(_controller.IsAiming));
            AddModifier(pas, modifier);
        }

        private void AddRecoilModifier(GameObject character, ProceduralAnimationSettings pas)
        {
            var recoilAnimation = CharacterAnimationSetup.AddOrGetComponent<RecoilAnimation>(character);
            var modifier = ScriptableObject.CreateInstance<ModifyBoneSettings>();
            modifier.name = "Procedural Recoil";
            modifier.boneToModify.name = CasNames.Bone_IkWeaponBone;
            modifier.rotation.Bind(recoilAnimation, nameof(recoilAnimation.OutRot));
            modifier.position.Bind(recoilAnimation, nameof(recoilAnimation.OutLoc));
            modifier.rotationMode = modifier.positionMode = EModifyMode.Add;
            modifier.positionSpace = modifier.rotationSpace = ESpaceType.ComponentSpace;
            AddModifier(pas, modifier);
        }

        private void AddIkMotionModifier(ProceduralAnimationSettings pas)
        {
            IkMotionSettings modifier = ScriptableObject.CreateInstance<IkMotionSettings>();
            modifier.name = "IK Motion";
            AddModifier(pas, modifier);
        }

        private void AddSwayModifier(ProceduralAnimationSettings s)
        {
            var modifier = ScriptableObject.CreateInstance<SwayModifierSettings>();
            modifier.name = "Sway Modifier";
            modifier.deltaLookInput.Bind(_controller, nameof(_controller.DeltaLookInput));
            modifier.moveInput.Bind(_controller, nameof(_controller.MoveInput));

            string weaponSettingsPath = $"{FPSExampleController.GetWeaponItemName}.{WeaponProp.WeaponSettingsName}";
            modifier.aimingSway.Bind(typeof(FPSExampleController), 
                $"{weaponSettingsPath}.{WeaponSettings.AimingSwayName}");
            modifier.movementSway.Bind(typeof(FPSExampleController), 
                $"{weaponSettingsPath}.{WeaponSettings.MovementSway}");
            
            AddModifier(s, modifier);
        }

        private void AddFullBodyIK(ProceduralAnimationSettings pas, FPSBoneTransforms bones)
        {
            var modifier = ScriptableObject.CreateInstance<FullBodyIkSettings>();
            modifier.name = "Full Body IK";

            modifier.rightHandIk.tip = _rightHand.element;
            modifier.rightHandIk.target.name = CasNames.Bone_IkHandRight;
            modifier.rightHandIk.poleVector = -Vector3.forward;

            modifier.leftHandIk.tip = _leftHand.element;
            modifier.leftHandIk.target.name = CasNames.Bone_IkHandLeft;
            modifier.leftHandIk.poleVector = -Vector3.forward;

            modifier.rightFootIk.tip = _rightFoot.element;
            modifier.rightFootIk.target.name = CasNames.Bone_IkFootRight;

            modifier.leftFootIk.tip = _leftFoot.element;
            modifier.leftFootIk.target.name = CasNames.Bone_IkFootLeft;

            AddModifier(pas, modifier);
        }
    }
}