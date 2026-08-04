using KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Modifiers;
using KINEMATION.CharacterAnimationSystem.Examples.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CAS_Demo.Scripts.FPS
{
    [AddComponentMenu(CasNames.Path_ComponentMenu + "FPS Example Controller")]
    public class FPSExampleController : CharacterExampleController
    {
        public bool IsCrouching => _isCrouching;
        public bool IsSprinting => _movementState == CharacterMovementState.Sprint;

        [Header("IK Motions")]
        [SerializeField] protected IkMotionSettings aimingMotion;
        [SerializeField] protected IkMotionSettings fireModeMotion;
        [SerializeField] protected IkMotionSettings movementMotion;

        protected RecoilAnimation _recoilAnimation;
        protected float _defaultMouseSensitivity;
        
        protected override void Start()
        {
            base.Start();
            _defaultMouseSensitivity = mouseSensitivity;
            _recoilAnimation = GetComponent<RecoilAnimation>();
        }

        protected override Vector2 GetDeltaLookInput()
        {
            Vector2 recoilDelta = Vector2.zero;

            if (_recoilAnimation != null)
            {
                _recoilAnimation.UpdateDeltaInput(_deltaLookInput);
                recoilDelta = _recoilAnimation.GetRecoilDelta();
            }
            
            return base.GetDeltaLookInput() + recoilDelta;
        }

#if UNITY_EDITOR
        public static string GetWeaponItemName => nameof(GetWeaponItem);
#endif
        
        public WeaponProp GetWeaponItem()
        {
            return GetActiveItem() as WeaponProp;
        }
        
        protected override void OnMovementChange(bool isMoving)
        {
            base.OnMovementChange(isMoving);
            if(_proceduralAnimation != null) _proceduralAnimation.UpdateAnimationModifier(movementMotion);
        }

#if ENABLE_INPUT_SYSTEM
        public override void OnUseItem(InputValue value)
        {
            if (IsSprinting)
            {
                GetActiveItem()?.StopUsingItem();
                return;
            }

            if (!isFirstPerson && !_isAiming) return;
            base.OnUseItem(value);
        }

        public override void OnSprint(InputValue value)
        {
            base.OnSprint(value);
            if (!value.isPressed) return;

            GetActiveItem()?.StopUsingItem();
            CancelAimForSprint();
        }

        public virtual void OnChangeFiremode()
        {
            if(_proceduralAnimation != null) _proceduralAnimation.UpdateAnimationModifier(fireModeMotion);
            var weapon = GetWeaponItem();
            if (weapon == null) return;
            
            weapon.ChangeFireMode();
        }

        public virtual void OnAlternativeAim()
        {
            if (!_isAiming) return;
            
            var weapon = GetWeaponItem();
            if(weapon != null) weapon.OnAlternativeAim();
        }
        
        public virtual void OnDigitSelected(InputValue value)
        {
            var weapon = GetWeaponItem();
            if (weapon == null) return;
            
            int groupIndex = (int) value.Get<float>();
            
            weapon.CycleAttachments(groupIndex);
        }
        
        public override void OnAim(InputValue value)
        {
            if (value.isPressed && IsSprinting) return;

            base.OnAim(value);
            _proceduralAnimation.UpdateAnimationModifier(aimingMotion);
            mouseSensitivity = _isAiming ? _defaultMouseSensitivity * 0.7f : _defaultMouseSensitivity;

            if (!isFirstPerson && !_isAiming) GetActiveItem().StopUsingItem();
        }

        private void CancelAimForSprint()
        {
            if (!_isAiming) return;

            _isAiming = false;
            if (_characterCamera != null) _characterCamera.isAiming = false;

            GetActiveItem()?.OnAim(false);
            if (_proceduralAnimation != null) _proceduralAnimation.UpdateAnimationModifier(aimingMotion);
            mouseSensitivity = _defaultMouseSensitivity;
        }
        
        public virtual void OnReload()
        {
            GetWeaponItem().Reload();
        }
#endif
    }
}
