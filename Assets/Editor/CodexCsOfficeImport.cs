using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class CodexCsOfficeImport
{
    private const string SourceRoot = "/Users/tdragoo/Downloads/cs-office-with-real-light";
    private const string ModelAssetPath = "Assets/Maps/cs_office/cs_office.fbx";
    private const string TextureAssetFolder = "Assets/Maps/cs_office/Textures";
    private const string MaterialAssetFolder = "Assets/Maps/cs_office/Materials";

    [MenuItem("Tools/Codex/Import CS Office")]
    private static void ImportFromMenu()
    {
        Import();
    }

    private static void Import()
    {
        try
        {
            string sourceModel = Path.Combine(SourceRoot, "source/1.fbx");
            string sourceTextures = Path.Combine(SourceRoot, "textures");

            if (!File.Exists(sourceModel) || !Directory.Exists(sourceTextures))
            {
                Debug.LogError("[CodexCsOfficeImport] Source FBX or texture folder is unavailable.");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string modelDiskPath = Path.Combine(projectRoot, ModelAssetPath);
            string textureDiskFolder = Path.Combine(projectRoot, TextureAssetFolder);
            string materialDiskFolder = Path.Combine(projectRoot, MaterialAssetFolder);

            Directory.CreateDirectory(Path.GetDirectoryName(modelDiskPath));
            Directory.CreateDirectory(textureDiskFolder);
            Directory.CreateDirectory(materialDiskFolder);

            if (!File.Exists(modelDiskPath))
            {
                File.Copy(sourceModel, modelDiskPath);
            }

            foreach (string sourceTexture in Directory.GetFiles(sourceTextures, "*", SearchOption.TopDirectoryOnly))
            {
                string extension = Path.GetExtension(sourceTexture).ToLowerInvariant();
                if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".tga")
                {
                    continue;
                }

                string destination = Path.Combine(textureDiskFolder, Path.GetFileName(sourceTexture));
                if (!File.Exists(destination))
                {
                    File.Copy(sourceTexture, destination);
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ModelImporter importer = AssetImporter.GetAtPath(ModelAssetPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[CodexCsOfficeImport] Unity did not create a model importer for the FBX.");
                return;
            }

            importer.globalScale = 0.0076655145f;
            importer.useFileScale = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();

            int remappedMaterials = 0;
            int texturedMaterials = 0;
            StringBuilder details = new StringBuilder();

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelAssetPath))
            {
                Material sourceMaterial = asset as Material;
                if (sourceMaterial == null)
                {
                    continue;
                }

                string materialPath = MaterialAssetFolder + "/" + sourceMaterial.name + ".mat";
                Material externalMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (externalMaterial == null)
                {
                    externalMaterial = new Material(sourceMaterial) { name = sourceMaterial.name };
                    AssetDatabase.CreateAsset(externalMaterial, materialPath);
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    TextureAssetFolder + "/" + sourceMaterial.name + ".jpeg");
                if (texture == null)
                {
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        TextureAssetFolder + "/" + sourceMaterial.name + ".jpg");
                }

                if (texture != null)
                {
                    externalMaterial.mainTexture = texture;
                    texturedMaterials++;
                }

                EditorUtility.SetDirty(externalMaterial);
                AssetImporter.SourceAssetIdentifier identifier = new AssetImporter.SourceAssetIdentifier
                {
                    type = typeof(Material),
                    name = sourceMaterial.name
                };
                importer.AddRemap(identifier, externalMaterial);
                remappedMaterials++;
                details.AppendLine(sourceMaterial.name + (texture == null ? " (no texture)" : " -> " + texture.name));
            }

            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelAssetPath);
            Selection.activeObject = model;
            EditorUtility.FocusProjectWindow();
            EditorGUIUtility.PingObject(model);

            Debug.Log(
                "[CodexCsOfficeImport] COMPLETE\n" +
                "Model: " + ModelAssetPath + "\n" +
                "Materials: " + remappedMaterials + "\n" +
                "Textured materials: " + texturedMaterials + "\n" +
                details);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
