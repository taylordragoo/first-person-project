// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using KINEMATION.Shared.KAnimationCore.Editor;
using UnityEditor;
using UnityEngine;

namespace KINEMATION.Shared.GameplayFramework.Scripts.Editor
{
    [Serializable]
    public struct CustomCurveData
    {
        public AnimationCurve curve;
        public string relativePath;
        public string propertyName;
        public string targetTypeName;

        public CustomCurveData(string relativePath, string propertyName, string targetTypeName, AnimationCurve curve)
        {
            this.relativePath = relativePath;
            this.propertyName = propertyName;
            this.targetTypeName = targetTypeName;
            this.curve = curve;
        }
    }

    public static class GameplayCustomCurveUtility
    {
        public const string CustomCurvePrefix = "CustomCurve";
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
                if (!TryParseCurveQuery(property, out string clipName, out CustomCurveData curveData))
                {
                    continue;
                }

                if (!string.Equals(clipName, clip.name)) continue;
                if (!IsValidCurveData(curveData, true)) continue;
                
                Type targetType = Type.GetType(curveData.targetTypeName);
                if (targetType == null) continue;

                clip.SetCurve(curveData.relativePath, targetType, curveData.propertyName, curveData.curve);
                appliedAny = true;
            }

            return appliedAny;
        }

        public static bool TrySavingToFBX(AnimationClip clip, List<CustomCurveData> customCurves)
        {
            if (!TryGetModelImporter(clip, out ModelImporter importer))
            {
                return false;
            }

            WriteCurvesToImporter(clip, importer, customCurves);
            importer.SaveAndReimport();
            return true;
        }

        public static void WriteCurvesToImporter(AnimationClip clip, ModelImporter importer,
            List<CustomCurveData> customCurves)
        {
            if (clip == null || importer == null || customCurves == null)
            {
                return;
            }

            List<CustomCurveData> validCustomCurves = customCurves
                .Where(curveData => IsValidCurveData(curveData, true))
                .ToList();

            List<string> extraUserProperties = (importer.extraUserProperties)
                .Where(property =>
                {
                    return !validCustomCurves.Any(curveData => IsCurveQueryForClip(property, clip.name, curveData));
                })
                .ToList();
            
            foreach (CustomCurveData curveData in validCustomCurves)
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

        private static bool TryGetModelImporter(AnimationClip clip, out ModelImporter importer)
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

        private static bool IsCurveQueryForClip(string property, string clipName, CustomCurveData curveData)
        {
            return TryParseCurveQuery(property, out string serializedClipName, out CustomCurveData serializedCurveData)
                   && serializedClipName.Equals(clipName)
                   && serializedCurveData.relativePath.Equals(curveData.relativePath)
                   && serializedCurveData.propertyName.Equals(curveData.propertyName);
        }

        private static string CreateCurveQuery(string clipName, CustomCurveData curveData)
        {
            return $"{CustomCurvePrefix}{QuerySeparator}{clipName}{QuerySeparator}{JsonUtility.ToJson(curveData)}";
        }

        private static bool IsValidCurveData(CustomCurveData curveData, bool requireCurve)
        {
            if (requireCurve && curveData.curve == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(curveData.propertyName) &&
                   !string.IsNullOrEmpty(curveData.targetTypeName);
        }

        private static bool TryParseCurveQuery(string property, out string clipName,
            out CustomCurveData curveData)
        {
            clipName = string.Empty;
            curveData = default;

            if (string.IsNullOrEmpty(property) || !property.StartsWith(PrefixWithSeparator, StringComparison.Ordinal))
            {
                return false;
            }

            string[] query = property.Split(QuerySeparator);
            if (query.Length != 3 || string.IsNullOrEmpty(query[1]) || string.IsNullOrEmpty(query[2]))
            {
                return false;
            }

            clipName = query[1];
            curveData = JsonUtility.FromJson<CustomCurveData>(query[2]);

            return true;
        }
    }
}
