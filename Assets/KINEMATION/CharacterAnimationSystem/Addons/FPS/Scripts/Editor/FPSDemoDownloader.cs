// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Scripts.Editor.Tools;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;

namespace KINEMATION.CharacterAnimationSystem.Addons.FPS.Scripts.Editor
{
    public class FPSDemoDownloader : CasDemoDownloaderTool
    {
        protected override string GetPackageUrl()
        {
            return "https://github.com/kinemation/demoes/releases/download/cas/CAS_Demo_FPS_Addon.unitypackage";
        }

        protected override string GetPackageFileName()
        {
            return "CasFPSAddonDemo";
        }

        public override string GetToolDescription()
        {
            return "Weapons, animations and example prefabs for FPS Addon (CAS).";
        }

        public override string GetToolName()
        {
            return "FPS Addon";
        }

        protected override List<ContentLicense> GetContentLicenses()
        {
            return null;
        }
    }
}