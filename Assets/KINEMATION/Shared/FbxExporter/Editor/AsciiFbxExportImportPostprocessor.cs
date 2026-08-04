// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEditor;

namespace KINEMATION.Shared.FbxExporter.Editor
{
    internal sealed class AsciiFbxExportImportPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer))
            {
                return;
            }

            if (!AsciiFbxExporter.IsAsciiFbxExportAsset(importer.assetPath))
            {
                return;
            }

            AsciiFbxExporter.ApplyImporterSettings(importer);
        }
    }
}