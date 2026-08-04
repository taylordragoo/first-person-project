// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.GameplayFramework.Scripts.Editor
{
    public class GameplayCustomCurvePostprocessor : AssetPostprocessor
    {
        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            var importer = assetImporter as ModelImporter;
            if (importer == null)
            {
                return;
            }

            GameplayCustomCurveUtility.ApplyCurvesFromImporter(clip, importer);
        }
    }
}
