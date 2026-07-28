using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEngine;

namespace CAS_Demo.Scripts.FPS.Attachments
{
    [AddComponentMenu(CasNames.Path_Addons + "FPS/Scope Attachment")]
    public class ScopeAttachment : WeaponAttachment
    {
        [SerializeField] protected ScopeAttachmentData scopeData;
        [SerializeField] protected List<ScopeAttachmentData> secondaryScopes = new List<ScopeAttachmentData>();
        protected int _activeScope = 0;

        public virtual void CycleScopes()
        {
            if (secondaryScopes.Count == 0) return;

            _activeScope++;
            _activeScope = _activeScope > secondaryScopes.Count ? 0 : _activeScope;
        }

        public virtual ScopeAttachmentData GetScopeData()
        {
            if (_activeScope == 0) return scopeData;
            return secondaryScopes[_activeScope - 1];
        }
    }
}