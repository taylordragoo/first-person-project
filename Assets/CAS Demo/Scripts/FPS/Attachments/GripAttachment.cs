using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEngine;

namespace CAS_Demo.Scripts.FPS.Attachments
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS/Grip Attachment")]
    public class GripAttachment : WeaponAttachment
    {
        public GripAttachmentData GripData => attachmentData;
        
        [SerializeField] protected GripAttachmentData attachmentData;
    }
}