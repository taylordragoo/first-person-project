using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEngine;

namespace CAS_Demo.Scripts.FPS
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS/Weapon Events")]
    public class WeaponPropEvents : MonoBehaviour
    {
        protected WeaponProp _weaponProp;

        private void Start()
        {
            _weaponProp = transform.parent.GetComponent<WeaponProp>();
        }

        public void PlaySoundByEvent(int index)
        {
            _weaponProp.PlaySoundByEvent(index);
        }
    }
}