// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.IO;
using System.Linq;
using KINEMATION.Shared.KAnimationCore.Editor.Tools;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.FbxExporter.Editor
{
    public class FbxAnimationExportTool : IEditorTool
    {
        private GameObject _model;
        private AnimationClip _clip;
        private AnimationClip _previousClip;
        private string _outputAssetDirectory = "Assets/";
        private string _lastExportPath;
        private string _statusMessage = "Select a model and animation clip.";
        private MessageType _statusType = MessageType.Info;

        private float _startTime;
        private float _endTime;
        private float _sampleRate = 30f;
        private bool _includeScaleCurves = true;
        private bool _optimizeConstantCurves = true;

        public void Init()
        {
        }

        public void Render()
        {
            DrawSelectionFields();
            DrawExportOptions();
            DrawOutputDirectory();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_statusMessage, _statusType);

            using (new EditorGUI.DisabledScope(!CanExport()))
            {
                if (GUILayout.Button("Export FBX Animation", GUILayout.Height(28f)))
                {
                    Export();
                }
            }

            DrawLastExportInfo();
        }

        public string GetToolCategory() => "Animation";
        public string GetToolName() => "FBX Exporter";
        public string GetDocsURL() => string.Empty;
        public string GetToolDescription() => "Exports an Animation Clip to a ASCII FBX asset.";

        private void DrawSelectionFields()
        {
            _model = (GameObject)EditorGUILayout.ObjectField("Model", _model, typeof(GameObject), true);
            _clip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", _clip, typeof(AnimationClip), false);

            if (_clip == _previousClip)
            {
                return;
            }

            _previousClip = _clip;
            if (_clip == null)
            {
                return;
            }

            _startTime = 0f;
            _endTime = _clip.length;
            _sampleRate = _clip.frameRate > 0f ? _clip.frameRate : 30f;
        }

        private void DrawExportOptions()
        {
            float clipLength = _clip != null ? Mathf.Max(0f, _clip.length) : 0f;
            _startTime = EditorGUILayout.FloatField("Start Time", _startTime);
            _endTime = EditorGUILayout.FloatField("End Time", _endTime);

            _startTime = Mathf.Max(_startTime, 0f);
            _endTime = Mathf.Max(_endTime, 0f);
            
            _sampleRate = EditorGUILayout.FloatField("Base Sample Rate", _sampleRate);
            _includeScaleCurves = EditorGUILayout.Toggle("Include Scale Curves", _includeScaleCurves);
            _optimizeConstantCurves = EditorGUILayout.Toggle("Optimize Constant Curves", _optimizeConstantCurves);

            if (_clip == null)
            {
                return;
            }

            float clampedStart = Mathf.Clamp(_startTime, 0f, clipLength);
            float clampedEnd = Mathf.Clamp(_endTime <= 0f ? clipLength : _endTime, clampedStart, clipLength);
            float resolvedRate = Mathf.Clamp(_sampleRate <= 0f ? _clip.frameRate : _sampleRate, 1f, 240f);
            float duration = Mathf.Max(0f, clampedEnd - clampedStart);
            int sampleCount = duration <= Mathf.Epsilon ? 1 : Mathf.CeilToInt(duration * resolvedRate) + 1;

            EditorGUILayout.LabelField("Minimum Samples", sampleCount.ToString());
            EditorGUILayout.LabelField("Sampling Note", "Extra samples are inserted at source curve keys when needed.");
        }

        private void DrawOutputDirectory()
        {
            EditorGUILayout.BeginHorizontal();
            _outputAssetDirectory = EditorGUILayout.TextField("Output Asset Folder", _outputAssetDirectory);
            if (GUILayout.Button("Choose", GUILayout.Width(72f)))
            {
                string initialDirectory = GetAbsoluteProjectPath(NormalizeAssetDirectory(_outputAssetDirectory));
                if (!Directory.Exists(initialDirectory))
                {
                    initialDirectory = Application.dataPath;
                }

                string selectedPath = EditorUtility.OpenFolderPanel("Choose FBX Export Folder", initialDirectory,
                    string.Empty);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    string selectedAssetPath = FileUtil.GetProjectRelativePath(selectedPath);
                    if (string.IsNullOrEmpty(selectedAssetPath) ||
                        (!selectedAssetPath.StartsWith("Assets/") && selectedAssetPath != "Assets"))
                    {
                        _statusMessage = "Output directory must be inside the project's Assets folder.";
                        _statusType = MessageType.Error;
                    }
                    else
                    {
                        _outputAssetDirectory = NormalizeAssetDirectory(selectedAssetPath);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLastExportInfo()
        {
            if (string.IsNullOrEmpty(_lastExportPath))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Export", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_lastExportPath, EditorStyles.wordWrappedLabel);

            Object exportedAsset = AssetDatabase.LoadMainAssetAtPath(_lastExportPath);
            int importedClipCount = AssetDatabase.LoadAllAssetsAtPath(_lastExportPath).OfType<AnimationClip>().Count();
            EditorGUILayout.LabelField("Imported Clips", importedClipCount.ToString());

            using (new EditorGUI.DisabledScope(exportedAsset == null))
            {
                if (GUILayout.Button("Ping Exported Asset"))
                {
                    Selection.activeObject = exportedAsset;
                    EditorGUIUtility.PingObject(exportedAsset);
                }
            }
        }

        private bool CanExport()
        {
            return _model != null && _clip != null && !string.IsNullOrWhiteSpace(_outputAssetDirectory);
        }

        private void Export()
        {
            string outputAssetPath = GetOutputAssetPath();
            var options = new AsciiFbxExporter.ExportOptions
            {
                model = _model,
                clip = _clip,
                outputAssetPath = outputAssetPath,
                startTime = _startTime,
                endTime = _endTime,
                sampleRate = _sampleRate,
                stripRootNode = true,
                includeScaleCurves = _includeScaleCurves,
                optimizeConstantCurves = _optimizeConstantCurves
            };

            if (AsciiFbxExporter.Export(options, out string error))
            {
                _lastExportPath = outputAssetPath;
                _statusMessage = $"Exported FBX to {_lastExportPath}";
                _statusType = MessageType.Info;
                return;
            }

            _statusMessage = error;
            _statusType = MessageType.Error;
        }

        private string GetOutputAssetPath()
        {
            string outputAssetPath = $"{NormalizeAssetDirectory(_outputAssetDirectory)}/{GetOutputFileName()}";
            return AssetDatabase.GenerateUniqueAssetPath(outputAssetPath);
        }

        private string GetOutputFileName()
        {
            string fileName = _clip != null ? _clip.name : (_model != null ? _model.name : "FbxExport");
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidCharacter.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(fileName) ? "FbxExport.fbx" : $"{fileName}.fbx";
        }

        private static string NormalizeAssetDirectory(string assetDirectory)
        {
            return (assetDirectory ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        }
    }
}
