// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.KAnimationCore.Editor.CurveImporter
{
    [Serializable]
    public struct CustomEditorCurveData
    {
        public AnimationCurve curve;
        public string relativePath;
        public string propertyName;
        public string targetTypeName;

        public CustomEditorCurveData(string relativePath, string propertyName, string targetTypeName, 
            AnimationCurve curve)
        {
            this.relativePath = relativePath;
            this.propertyName = propertyName;
            this.targetTypeName = targetTypeName;
            this.curve = curve;
        }
    }
    
    public class CurveProcessorUtility
    {
        public const string CustomCurvePrefix = "Curve";
        private const string QuerySeparator = "~";
        private static readonly string PrefixWithSeparator = $"{CustomCurvePrefix}{QuerySeparator}";
        
        public static bool ApplyCurvesFromImporter(AnimationClip clip, ModelImporter importer)
        {
            if (clip == null || importer == null)
            {
                return false;
            }

            if (!importer.importAnimatedCustomProperties) return false;
            
            bool appliedAny = false;
            foreach (string property in importer.extraUserProperties)
            {
                if (!TryParseCurveQuery(property, out string clipName, out CustomEditorCurveData curveData))
                {
                    continue;
                }

                if (!string.Equals(clipName, clip.name)) continue;
                if (!IsValidCurveData(curveData, true)) continue;
                
                Type targetType = Type.GetType(curveData.targetTypeName);
                if (targetType == null) continue;

                curveData.relativePath =
                    string.IsNullOrEmpty(curveData.relativePath) ? string.Empty : curveData.relativePath;

                clip.SetCurve(curveData.relativePath, targetType, curveData.propertyName, curveData.curve);
                appliedAny = true;
            }

            return appliedAny;
        }
        
        public static void WriteCurvesToImporter(AnimationClip clip, ModelImporter importer,
            List<CustomEditorCurveData> customCurves)
        {
            if (clip == null || importer == null || customCurves == null)
            {
                return;
            }

            List<CustomEditorCurveData> validCustomCurves = customCurves
                .Where(curveData => IsValidCurveData(curveData, true))
                .ToList();

            List<string> extraUserProperties = (importer.extraUserProperties)
                .Where(property =>
                {
                    return !validCustomCurves.Any(curveData => IsCurveQueryForClip(property, clip.name, curveData));
                })
                .ToList();
            
            foreach (CustomEditorCurveData curveData in validCustomCurves)
            {
                extraUserProperties.Add(CreateCurveQuery(clip.name, curveData));
            }
            
            string[] newProperties = extraUserProperties.ToArray();

            if (!importer.extraUserProperties.SequenceEqual(newProperties))
            {
                importer.extraUserProperties = newProperties;
            }

            if (!importer.importAnimatedCustomProperties) importer.importAnimatedCustomProperties = true;
        }
        
        public static bool TrySavingToFBX(AnimationClip clip, List<CustomEditorCurveData> customCurves)
        {
            if (!TryGetModelImporter(clip, out ModelImporter importer))
            {
                return false;
            }

            WriteCurvesToImporter(clip, importer, customCurves);
            return true;
        }

        public static bool TryGetModelImporter(AnimationClip clip, out ModelImporter importer)
        {
            importer = null;

            if (clip == null || !KEditorUtility.IsSubAsset(clip))
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(clip);
            importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;

            return importer != null;
        }
        
        private static bool TryParseCurveQuery(string property, out string clipName,
            out CustomEditorCurveData curveData)
        {
            clipName = string.Empty;
            curveData = default;

            if (string.IsNullOrEmpty(property) || !property.Contains(PrefixWithSeparator, StringComparison.Ordinal))
            {
                return false;
            }

            string[] query = property.Split(QuerySeparator);
            if (query.Length != 3 || string.IsNullOrEmpty(query[1]) || string.IsNullOrEmpty(query[2]))
            {
                return false;
            }

            clipName = query[1];
            curveData = JsonUtility.FromJson<CustomEditorCurveData>(query[2]);

            return true;
        }
        
        private static bool IsCurveQueryForClip(string property, string clipName, CustomEditorCurveData curveData)
        {
            if (!TryParseCurveQuery(property, out string serializedClipName,
                    out CustomEditorCurveData serializedCurveData))
            {
                return false;
            }

            if (!string.Equals(serializedClipName, clipName))
            {
                return false;
            }

            if (!string.Equals(serializedCurveData.relativePath, curveData.relativePath))
            {
                return false;
            }
            
            if (!string.Equals(serializedCurveData.propertyName, curveData.propertyName))
            {
                return false;
            }

            return true;
        }
        
        private static string CreateCurveQuery(string clipName, CustomEditorCurveData curveData)
        {
            return $"{CustomCurvePrefix}{QuerySeparator}{clipName}{QuerySeparator}{JsonUtility.ToJson(curveData)}";
        }
        
        private static bool IsValidCurveData(CustomEditorCurveData curveData, bool requireCurve)
        {
            if (requireCurve && curveData.curve == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(curveData.propertyName) && !string.IsNullOrEmpty(curveData.targetTypeName);
        }
    }
}