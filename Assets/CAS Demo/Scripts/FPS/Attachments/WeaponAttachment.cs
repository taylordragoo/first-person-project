using System;
using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEngine;

namespace CAS_Demo.Scripts.FPS.Attachments
{
    [Serializable]
    public struct GripAttachmentData
    {
        public AnimationClip pose;
        public Transform ikTarget;
        
#if UNITY_EDITOR
        public static readonly string PoseName = nameof(pose);
        public static readonly string IkTargetName = nameof(ikTarget);
#endif
    }

    [Serializable]
    public struct ScopeAttachmentData
    {
        public Transform aimPoint;
        [Min(0f)] public float aimFov;
        
#if UNITY_EDITOR
        public static readonly string AimPointName = nameof(aimPoint);
#endif
    }
    
    [Serializable]
    public struct WeaponAttachmentGroup<T> where T : WeaponAttachment
    {
        public List<T> attachments;
        private int _activeAttachmentIndex;

        public void CycleAttachments(bool forward)
        {
            if (attachments.Count == 0) return;
            int previousAttachmentIndex = _activeAttachmentIndex;
            
            _activeAttachmentIndex += forward ? 1 : -1;
            if (_activeAttachmentIndex < 0) _activeAttachmentIndex = attachments.Count - 1;
            if (_activeAttachmentIndex > attachments.Count - 1) _activeAttachmentIndex = 0;
            
            attachments[previousAttachmentIndex].DisableAttachment();
            attachments[_activeAttachmentIndex].EnableAttachment();
        }
        
        public T GetActiveAttachment()
        {
            if (attachments.Count == 0) return null;
            return attachments[_activeAttachmentIndex];
        }
    }
    
    [AddComponentMenu(CasNames.Path_Addons + "FPS/Weapon Attachment")]
    public class WeaponAttachment : MonoBehaviour
    {
        public virtual void EnableAttachment()
        {
            gameObject.SetActive(true);
        }

        public virtual void DisableAttachment()
        {
            gameObject.SetActive(false);
        }
    }
}