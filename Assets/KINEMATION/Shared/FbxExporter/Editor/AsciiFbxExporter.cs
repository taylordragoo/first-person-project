// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KINEMATION.Shared.FbxExporter.Editor
{
    public static class AsciiFbxExporter
    {
        public const long FbxTicksPerSecond = 46186158000L;

        private const float MinSampleRate = 1f;
        private const float MaxSampleRate = 240f;
        private const float CurveEpsilon = 0.0001f;
        private const float PositionCurveTolerance = 0.0005f;
        private const float RotationCurveTolerance = 0.05f;
        private const float ScaleCurveTolerance = 0.0005f;
        private const int MaxAdaptiveCurveDepth = 8;
        private const string ExporterVersionTag = "v2026-03-27-shared-transform-hierarchy";
        private const string ExporterDisplayName = "KINEMATION ASCII FBX Exporter";
        private const string LegacyExporterDisplayName = "Retarget Pro ASCII FBX Exporter";
        private const int WeightedUserKeyAttrFlag = 50334728;
        private const float DefaultTangentWeight = 1f / 3f;
        private const float TerminalTangentWeight = 0.00010001f;
        private const float FbxTranslationScale = 100f;
        private const float FbxFileUnitScaleFactor = 1f;
        private static readonly string[] AnimatorRootPositionPropertyNames = { "RootT.x", "RootT.y", "RootT.z" };
        private static readonly string[] AnimatorRootRotationPropertyNames = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };

        [Serializable]
        public sealed class ExportOptions
        {
            public GameObject model;
            public AnimationClip clip;
            public string outputAssetPath = "Assets/RetargetedAnimations/AsciiExport.fbx";
            public float sampleRate = 30f;
            public float startTime;
            public float endTime;
            public bool stripRootNode = true;
            public bool includeScaleCurves = true;
            public bool optimizeConstantCurves = true;
        }

        private sealed class ExportRootTrack
        {
            public Vector3 defaultLocalPosition;
            public Vector3 defaultLocalRotation;
            public Vector3 defaultLocalScale;
            public Vector3 lastSampledEuler;
            public bool hasRotationSample;

            public readonly List<float> translationX = new List<float>();
            public readonly List<float> translationY = new List<float>();
            public readonly List<float> translationZ = new List<float>();
            public readonly List<float> rotationX = new List<float>();
            public readonly List<float> rotationY = new List<float>();
            public readonly List<float> rotationZ = new List<float>();
            public readonly List<float> scaleX = new List<float>();
            public readonly List<float> scaleY = new List<float>();
            public readonly List<float> scaleZ = new List<float>();

            public FbxCurveNode translationNode;
            public FbxCurveNode rotationNode;
            public FbxCurveNode scaleNode;
        }

        private sealed class ExportNode
        {
            public int parentIndex;
            public string exportName;
            public string sourcePath;
            public Transform transform;
            public Vector3 defaultLocalPosition;
            public Vector3 defaultLocalRotation;
            public Vector3 defaultLocalScale;
            public Vector3 lastSampledEuler;
            public bool hasRotationSample;

            public AnimationCurve sourcePositionX;
            public AnimationCurve sourcePositionY;
            public AnimationCurve sourcePositionZ;
            public AnimationCurve sourceScaleX;
            public AnimationCurve sourceScaleY;
            public AnimationCurve sourceScaleZ;
            public AnimationCurve sourceEulerX;
            public AnimationCurve sourceEulerY;
            public AnimationCurve sourceEulerZ;

            public readonly List<float> translationX = new List<float>();
            public readonly List<float> translationY = new List<float>();
            public readonly List<float> translationZ = new List<float>();
            public readonly List<float> rotationX = new List<float>();
            public readonly List<float> rotationY = new List<float>();
            public readonly List<float> rotationZ = new List<float>();
            public readonly List<float> scaleX = new List<float>();
            public readonly List<float> scaleY = new List<float>();
            public readonly List<float> scaleZ = new List<float>();

            public long modelId;
            public FbxCurveNode translationNode;
            public FbxCurveNode rotationNode;
            public FbxCurveNode scaleNode;
        }

        private sealed class FbxCurveNode
        {
            public long id;
            public string label;
            public Vector3 defaultValue;
            public FbxCurve xCurve;
            public FbxCurve yCurve;
            public FbxCurve zCurve;
        }

        private sealed class FbxCurve
        {
            public long id;
            public float defaultValue;
            public List<long> keyTimes;
            public List<float> keyValues;
            public List<int> keyAttrFlags;
            public List<int> keyAttrData;
            public List<int> keyAttrRefCount;
        }

        private sealed class ExportDocument
        {
            public string sceneName;
            public long sceneRootModelId;
            public long animationStackId;
            public long animationLayerId;
            public long stopTime;
            public bool includeScaleCurves;
            public ExportRootTrack rootTrack;
            public List<ExportNode> nodes;
        }

        private sealed class RootMotionCurves
        {
            public AnimationCurve[] positionCurves;
            public AnimationCurve[] rotationCurves;

            public bool HasPosition => positionCurves != null && positionCurves.Length == 3;
            public bool HasRotation => rotationCurves != null && rotationCurves.Length == 4;
            public bool HasAny => HasPosition || HasRotation;
        }

        private sealed class ClipCurveLookup
        {
            public readonly Dictionary<string, AnimationCurve> transformCurves =
                new Dictionary<string, AnimationCurve>(StringComparer.Ordinal);
        }

        private sealed class IdGenerator
        {
            private long _nextId = 1000000L;

            public long Next()
            {
                _nextId += 16L;
                return _nextId;
            }
        }

        public static bool Export(ExportOptions options, out string error)
        {
            error = string.Empty;

            if (!ValidateOptions(options, out float normalizedStartTime, out float normalizedEndTime,
                    out float sampleRate, out error))
            {
                return false;
            }

            string outputAssetPath = NormalizeAssetPath(options.outputAssetPath);
            string absoluteOutputPath = GetAbsoluteProjectPath(outputAssetPath);
            string directoryPath = Path.GetDirectoryName(absoluteOutputPath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                error = "Unable to resolve the output directory.";
                return false;
            }

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            GameObject exportInstance = null;

            try
            {
                exportInstance = Object.Instantiate(options.model);
                if (exportInstance == null)
                {
                    error = "Failed to instantiate the export model.";
                    return false;
                }

                exportInstance.hideFlags = HideFlags.HideAndDontSave;
                exportInstance.name = options.model.name;

                RootMotionCurves rootMotionCurves = ExtractRootMotionCurves(options.clip);

                // Imported animation paths must start at the actual skeleton root, not the model container.
                bool stripRootNode = exportInstance.transform.childCount > 0;
                var exportNodes = new List<ExportNode>();
                if (stripRootNode)
                {
                    for (int i = 0; i < exportInstance.transform.childCount; i++)
                    {
                        CollectExportNodes(exportInstance.transform.GetChild(i), exportInstance.transform, -1, exportNodes);
                    }
                }
                else
                {
                    CollectExportNodes(exportInstance.transform, exportInstance.transform, -1, exportNodes);
                }

                ClipCurveLookup clipCurveLookup = BuildClipCurveLookup(options.clip);
                AttachSourceCurves(exportNodes, clipCurveLookup);

                if (stripRootNode)
                {
                    ApplyStrippedRootDefaults(exportInstance.transform, exportNodes, rootMotionCurves, normalizedStartTime);
                }
                else
                {
                    ApplyExportRootDefaults(exportInstance.transform, exportNodes, rootMotionCurves, normalizedStartTime);
                }

                List<float> sampleTimes = BuildSampleTimes(normalizedStartTime, normalizedEndTime, sampleRate,
                    exportNodes, rootMotionCurves);
                if (sampleTimes.Count == 0)
                {
                    sampleTimes.Add(0f);
                }

                if (!CaptureSamples(options.clip, exportInstance, stripRootNode, rootMotionCurves, exportNodes, sampleTimes,
                        normalizedStartTime, out error))
                {
                    return false;
                }

                List<long> fbxSampleTimes = ConvertSampleTimesToFbxTicks(sampleTimes);
                ExportDocument document = BuildDocument(options, null, exportNodes, fbxSampleTimes,
                    sampleTimes[sampleTimes.Count - 1]);

                string asciiContents = BuildAsciiDocument(document);
                File.WriteAllText(absoluteOutputPath, asciiContents, new UTF8Encoding(false));

                AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceSynchronousImport);
                ModelImporter importer = AssetImporter.GetAtPath(outputAssetPath) as ModelImporter;
                if (importer == null)
                {
                    error = "Unity did not create a ModelImporter for the exported FBX.";
                    return false;
                }

                ConfigureImporter(importer);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (exportInstance != null)
                {
                    Object.DestroyImmediate(exportInstance);
                }
            }
        }

        private static ExportDocument BuildDocument(ExportOptions options, ExportRootTrack rootTrack,
            List<ExportNode> exportNodes, List<long> sampleTimes, float relativeDuration)
        {
            var idGenerator = new IdGenerator();
            bool includeScaleCurves = options.includeScaleCurves;

            var document = new ExportDocument
            {
                sceneName = Path.GetFileNameWithoutExtension(options.outputAssetPath),
                sceneRootModelId = idGenerator.Next(),
                animationStackId = idGenerator.Next(),
                animationLayerId = idGenerator.Next(),
                stopTime = SecondsToFbxTicks(relativeDuration),
                includeScaleCurves = includeScaleCurves,
                rootTrack = rootTrack,
                nodes = exportNodes
            };

            if (rootTrack != null)
            {
                rootTrack.translationNode = BuildCurveNode(idGenerator, "T", rootTrack.defaultLocalPosition, sampleTimes,
                    rootTrack.translationX, rootTrack.translationY, rootTrack.translationZ, options.optimizeConstantCurves,
                    FbxTranslationScale);
                rootTrack.rotationNode = BuildCurveNode(idGenerator, "R", rootTrack.defaultLocalRotation, sampleTimes,
                    rootTrack.rotationX, rootTrack.rotationY, rootTrack.rotationZ, options.optimizeConstantCurves);

                if (includeScaleCurves)
                {
                    rootTrack.scaleNode = BuildCurveNode(idGenerator, "S", rootTrack.defaultLocalScale, sampleTimes,
                        rootTrack.scaleX, rootTrack.scaleY, rootTrack.scaleZ, options.optimizeConstantCurves);
                }
            }

            for (int i = 0; i < exportNodes.Count; i++)
            {
                ExportNode node = exportNodes[i];
                node.modelId = idGenerator.Next();

                node.translationNode = BuildCurveNode(idGenerator, "T", node.defaultLocalPosition, sampleTimes,
                    node.translationX, node.translationY, node.translationZ, options.optimizeConstantCurves,
                    FbxTranslationScale);
                node.rotationNode = BuildCurveNode(idGenerator, "R", node.defaultLocalRotation, sampleTimes,
                    node.rotationX, node.rotationY, node.rotationZ, options.optimizeConstantCurves);

                if (includeScaleCurves)
                {
                    node.scaleNode = BuildCurveNode(idGenerator, "S", node.defaultLocalScale, sampleTimes,
                        node.scaleX, node.scaleY, node.scaleZ, options.optimizeConstantCurves);
                }
            }

            return document;
        }

        private static FbxCurveNode BuildCurveNode(IdGenerator idGenerator, string label, Vector3 defaultValue,
            List<long> sampleTimes, List<float> xValues, List<float> yValues, List<float> zValues,
            bool optimizeConstantCurves, float valueScale = 1f)
        {
            return new FbxCurveNode
            {
                id = idGenerator.Next(),
                label = label,
                defaultValue = ScaleVector3(defaultValue, valueScale),
                xCurve = BuildCurve(idGenerator, defaultValue.x, sampleTimes, xValues, optimizeConstantCurves, valueScale),
                yCurve = BuildCurve(idGenerator, defaultValue.y, sampleTimes, yValues, optimizeConstantCurves, valueScale),
                zCurve = BuildCurve(idGenerator, defaultValue.z, sampleTimes, zValues, optimizeConstantCurves, valueScale)
            };
        }

        private static FbxCurve BuildCurve(IdGenerator idGenerator, float defaultValue, List<long> sourceTimes,
            List<float> sourceValues, bool optimizeConstantCurves, float valueScale = 1f)
        {
            var keyTimes = new List<long>(sourceTimes.Count);
            var keyValues = new List<float>(sourceValues.Count);
            var keyAttrFlags = new List<int>();
            var keyAttrData = new List<int>();
            var keyAttrRefCount = new List<int>();

            if (!optimizeConstantCurves || !TryBuildConstantCurve(sourceTimes, sourceValues, keyTimes, keyValues))
            {
                keyTimes.AddRange(sourceTimes);
                for (int i = 0; i < sourceValues.Count; i++)
                {
                    keyValues.Add(ScaleFloat(sourceValues[i], valueScale));
                }
            }
            else
            {
                for (int i = 0; i < keyValues.Count; i++)
                {
                    keyValues[i] = ScaleFloat(keyValues[i], valueScale);
                }
            }

            BuildSampledTangentData(keyValues, keyTimes, keyAttrFlags, keyAttrData, keyAttrRefCount);

            return new FbxCurve
            {
                id = idGenerator.Next(),
                defaultValue = ScaleFloat(defaultValue, valueScale),
                keyTimes = keyTimes,
                keyValues = keyValues,
                keyAttrFlags = keyAttrFlags,
                keyAttrData = keyAttrData,
                keyAttrRefCount = keyAttrRefCount
            };
        }

        private static bool TryBuildConstantCurve(IReadOnlyList<long> sourceTimes, IReadOnlyList<float> sourceValues,
            ICollection<long> keyTimes, ICollection<float> keyValues)
        {
            if (sourceTimes == null || sourceValues == null || sourceTimes.Count == 0 || sourceValues.Count == 0)
            {
                return false;
            }

            float reference = sourceValues[0];
            for (int i = 1; i < sourceValues.Count; i++)
            {
                if (!Mathf.Approximately(reference, sourceValues[i]) &&
                    Mathf.Abs(reference - sourceValues[i]) > CurveEpsilon)
                {
                    return false;
                }
            }

            keyTimes.Add(sourceTimes[0]);
            keyValues.Add(reference);

            if (sourceTimes.Count > 1 && sourceTimes[sourceTimes.Count - 1] != sourceTimes[0])
            {
                keyTimes.Add(sourceTimes[sourceTimes.Count - 1]);
                keyValues.Add(reference);
            }

            return true;
        }

        private static List<float> BuildSampleTimes(float startTime, float endTime, float sampleRate,
            IReadOnlyList<ExportNode> exportNodes, RootMotionCurves rootMotionCurves)
        {
            float duration = Mathf.Max(0f, endTime - startTime);
            if (duration <= Mathf.Epsilon)
            {
                return new List<float> { 0f };
            }

            var absoluteTimes = new List<float>(256);
            AddUniformSampleTimes(startTime, endTime, sampleRate, absoluteTimes);
            AddRootMotionSampleTimes(rootMotionCurves, startTime, endTime, absoluteTimes);
            AddNodeCurveSampleTimes(exportNodes, startTime, endTime, absoluteTimes);

            absoluteTimes.Sort();

            var result = new List<float>(absoluteTimes.Count);
            float lastAbsoluteTime = float.NegativeInfinity;
            for (int i = 0; i < absoluteTimes.Count; i++)
            {
                float absoluteTime = Mathf.Clamp(absoluteTimes[i], startTime, endTime);
                if (result.Count > 0 && Mathf.Abs(absoluteTime - lastAbsoluteTime) <= CurveEpsilon)
                {
                    continue;
                }

                result.Add(Mathf.Max(0f, absoluteTime - startTime));
                lastAbsoluteTime = absoluteTime;
            }

            if (result.Count == 0 || result[0] > CurveEpsilon)
            {
                result.Insert(0, 0f);
            }

            if (Mathf.Abs(result[result.Count - 1] - duration) > CurveEpsilon)
            {
                result.Add(duration);
            }

            return result;
        }

        private static void AddUniformSampleTimes(float startTime, float endTime, float sampleRate,
            ICollection<float> absoluteTimes)
        {
            if (absoluteTimes == null)
            {
                return;
            }

            float duration = Mathf.Max(0f, endTime - startTime);
            if (duration <= Mathf.Epsilon)
            {
                absoluteTimes.Add(startTime);
                return;
            }

            float delta = 1f / Mathf.Max(sampleRate, MinSampleRate);
            float time = startTime;
            while (time < endTime)
            {
                absoluteTimes.Add(time);
                time += delta;
            }

            absoluteTimes.Add(endTime);
        }

        private static void AddRootMotionSampleTimes(RootMotionCurves rootMotionCurves, float startTime, float endTime,
            ICollection<float> absoluteTimes)
        {
            if (rootMotionCurves == null)
            {
                return;
            }

            AddCurveGroupSampleTimes(rootMotionCurves.positionCurves, startTime, endTime, PositionCurveTolerance,
                absoluteTimes);
            AddCurveGroupSampleTimes(rootMotionCurves.rotationCurves, startTime, endTime, RotationCurveTolerance,
                absoluteTimes);
        }

        private static void AddNodeCurveSampleTimes(IReadOnlyList<ExportNode> exportNodes, float startTime, float endTime,
            ICollection<float> absoluteTimes)
        {
            if (exportNodes == null)
            {
                return;
            }

            for (int i = 0; i < exportNodes.Count; i++)
            {
                ExportNode node = exportNodes[i];
                AddCurveGroupSampleTimes(new[] { node.sourcePositionX, node.sourcePositionY, node.sourcePositionZ },
                    startTime, endTime, PositionCurveTolerance, absoluteTimes);
                AddCurveGroupSampleTimes(new[] { node.sourceEulerX, node.sourceEulerY, node.sourceEulerZ },
                    startTime, endTime, RotationCurveTolerance, absoluteTimes);
                AddCurveGroupSampleTimes(new[] { node.sourceScaleX, node.sourceScaleY, node.sourceScaleZ },
                    startTime, endTime, ScaleCurveTolerance, absoluteTimes);
            }
        }

        private static void AddCurveGroupSampleTimes(AnimationCurve[] curves, float startTime, float endTime,
            float tolerance, ICollection<float> absoluteTimes)
        {
            if (curves == null)
            {
                return;
            }

            for (int i = 0; i < curves.Length; i++)
            {
                AddCurveSampleTimes(curves[i], startTime, endTime, tolerance, absoluteTimes);
            }
        }

        private static void AddCurveSampleTimes(AnimationCurve curve, float startTime, float endTime, float tolerance,
            ICollection<float> absoluteTimes)
        {
            if (curve == null || curve.length == 0 || absoluteTimes == null)
            {
                return;
            }

            var anchors = new List<float>(curve.length + 2)
            {
                startTime,
                endTime
            };

            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                float keyTime = keys[i].time;
                if (keyTime < startTime - CurveEpsilon || keyTime > endTime + CurveEpsilon)
                {
                    continue;
                }

                anchors.Add(Mathf.Clamp(keyTime, startTime, endTime));
            }

            anchors.Sort();

            float previousAnchor = float.NaN;
            for (int i = 0; i < anchors.Count; i++)
            {
                float anchor = anchors[i];
                if (i > 0 && Mathf.Abs(anchor - previousAnchor) <= CurveEpsilon)
                {
                    continue;
                }

                absoluteTimes.Add(anchor);
                if (i > 0 && anchor - previousAnchor > CurveEpsilon)
                {
                    AddAdaptiveCurveSampleTimes(curve, previousAnchor, anchor, tolerance, 0, absoluteTimes);
                }

                previousAnchor = anchor;
            }
        }

        private static void AddAdaptiveCurveSampleTimes(AnimationCurve curve, float startTime, float endTime,
            float tolerance, int depth, ICollection<float> absoluteTimes)
        {
            if (curve == null || absoluteTimes == null || depth >= MaxAdaptiveCurveDepth)
            {
                return;
            }

            if (endTime - startTime <= CurveEpsilon ||
                !NeedsAdaptiveSubdivision(curve, startTime, endTime, tolerance))
            {
                return;
            }

            float midTime = (startTime + endTime) * 0.5f;
            absoluteTimes.Add(midTime);
            AddAdaptiveCurveSampleTimes(curve, startTime, midTime, tolerance, depth + 1, absoluteTimes);
            AddAdaptiveCurveSampleTimes(curve, midTime, endTime, tolerance, depth + 1, absoluteTimes);
        }

        private static bool NeedsAdaptiveSubdivision(AnimationCurve curve, float startTime, float endTime,
            float tolerance)
        {
            float startValue = curve.Evaluate(startTime);
            float endValue = curve.Evaluate(endTime);

            const int probeCount = 3;
            for (int i = 1; i <= probeCount; i++)
            {
                float alpha = i / (probeCount + 1f);
                float probeTime = Mathf.Lerp(startTime, endTime, alpha);
                float expectedValue = Mathf.Lerp(startValue, endValue, alpha);
                float actualValue = curve.Evaluate(probeTime);
                if (Mathf.Abs(actualValue - expectedValue) > tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BuildSampledTangentData(IReadOnlyList<float> keyValues, IReadOnlyList<long> keyTimes,
            ICollection<int> keyAttrFlags, ICollection<int> keyAttrData, ICollection<int> keyAttrRefCount)
        {
            int keyCount = keyValues != null ? keyValues.Count : 0;
            for (int i = 0; i < keyCount; i++)
            {
                keyAttrFlags.Add(WeightedUserKeyAttrFlag);

                float rightSlope = 0f;
                if (i < keyCount - 1)
                {
                    float dt = (keyTimes[i + 1] - keyTimes[i]) / (float)FbxTicksPerSecond;
                    if (dt > Mathf.Epsilon)
                    {
                        rightSlope = (keyValues[i + 1] - keyValues[i]) / dt;
                    }
                }

                float nextLeftSlope = i < keyCount - 1 ? rightSlope : 0f;
                keyAttrData.Add(FloatToIntBits(rightSlope));
                keyAttrData.Add(FloatToIntBits(i < keyCount - 1 ? nextLeftSlope : -0f));
                keyAttrData.Add(FloatToIntBits(DefaultTangentWeight));
                keyAttrData.Add(FloatToIntBits(i < keyCount - 1 ? DefaultTangentWeight : TerminalTangentWeight));
                keyAttrRefCount.Add(1);
            }
        }

        private static bool CaptureSamples(AnimationClip clip, GameObject exportInstance, bool stripRootNode,
            RootMotionCurves rootMotionCurves, List<ExportNode> exportNodes, IReadOnlyList<float> sampleTimes, float startTime,
            out string error)
        {
            error = string.Empty;
            int sampleCount = sampleTimes.Count;

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                float relativeTime = sampleTimes[sampleIndex];
                float clipTime = startTime + relativeTime;

                if (EditorUtility.DisplayCancelableProgressBar("ASCII FBX Exporter",
                        $"Sampling {clip.name} ({sampleIndex + 1}/{sampleCount})",
                        sampleCount <= 1 ? 1f : sampleIndex / (float)(sampleCount - 1)))
                {
                    error = "Export cancelled.";
                    return false;
                }

                clip.SampleAnimation(exportInstance, clipTime);
                ResolveRootPose(exportInstance.transform, rootMotionCurves, clipTime, out Vector3 rootPosition,
                    out Quaternion rootRotation, out Vector3 rootScale);

                for (int nodeIndex = 0; nodeIndex < exportNodes.Count; nodeIndex++)
                {
                    ExportNode node = exportNodes[nodeIndex];
                    Transform transform = node.transform;
                    Vector3 unityFallbackPosition = sampleIndex == 0
                        ? node.defaultLocalPosition
                        : ConvertFbxLocalPositionToUnity(node.defaultLocalPosition);

                    Vector3 position;
                    Quaternion rotation;
                    Vector3 scale;

                    if (stripRootNode && node.parentIndex < 0)
                    {
                        ComposeLocalTransform(rootPosition, rootRotation, rootScale, transform.localPosition,
                            transform.localRotation, transform.localScale, out position, out rotation, out scale);
                        position = SanitizeVector3(position, unityFallbackPosition);
                        scale = SanitizeVector3(scale, node.defaultLocalScale);
                    }
                    else if (transform == exportInstance.transform && rootMotionCurves != null && rootMotionCurves.HasAny)
                    {
                        position = SanitizeVector3(rootPosition, unityFallbackPosition);
                        rotation = rootRotation;
                        scale = SanitizeVector3(rootScale, node.defaultLocalScale);
                    }
                    else
                    {
                        position = SanitizeVector3(transform.localPosition, unityFallbackPosition);
                        rotation = transform.localRotation;
                        scale = SanitizeVector3(transform.localScale, node.defaultLocalScale);
                    }

                    position = ConvertUnityLocalPositionToFbx(position);
                    rotation = ConvertUnityLocalRotationToFbx(rotation);

                    Vector3 rawEuler = ToSignedEuler(rotation.eulerAngles);
                    if (sampleIndex == 0)
                    {
                        node.defaultLocalPosition = position;
                        node.defaultLocalRotation = rawEuler;
                        node.defaultLocalScale = scale;
                    }

                    if (node.hasRotationSample)
                    {
                        rawEuler = UnwrapEuler(node.lastSampledEuler, rawEuler);
                    }
                    else
                    {
                        node.hasRotationSample = true;
                    }

                    node.lastSampledEuler = rawEuler;

                    node.translationX.Add(position.x);
                    node.translationY.Add(position.y);
                    node.translationZ.Add(position.z);

                    node.rotationX.Add(rawEuler.x);
                    node.rotationY.Add(rawEuler.y);
                    node.rotationZ.Add(rawEuler.z);

                    node.scaleX.Add(scale.x);
                    node.scaleY.Add(scale.y);
                    node.scaleZ.Add(scale.z);
                }
            }

            return true;
        }

        private static ExportRootTrack CreateRootTrack(Transform rootTransform)
        {
            return new ExportRootTrack
            {
                defaultLocalPosition = rootTransform.localPosition,
                defaultLocalRotation = ToSignedEuler(rootTransform.localRotation.eulerAngles),
                defaultLocalScale = rootTransform.localScale
            };
        }

        private static void CollectExportNodes(Transform transform, Transform clipRoot, int parentIndex,
            ICollection<ExportNode> exportNodes)
        {
            if (transform == null)
            {
                return;
            }

            string exportName = string.IsNullOrEmpty(transform.name) ? "Node" : transform.name;

            var node = new ExportNode
            {
                parentIndex = parentIndex,
                exportName = exportName,
                sourcePath = clipRoot != null ? AnimationUtility.CalculateTransformPath(transform, clipRoot) : string.Empty,
                transform = transform,
                defaultLocalPosition = transform.localPosition,
                defaultLocalRotation = ToSignedEuler(transform.localRotation.eulerAngles),
                defaultLocalScale = transform.localScale
            };

            int nodeIndex = exportNodes.Count;
            exportNodes.Add(node);

            for (int i = 0; i < transform.childCount; i++)
            {
                CollectExportNodes(transform.GetChild(i), clipRoot, nodeIndex, exportNodes);
            }
        }

        private static void ApplyExportRootDefaults(Transform exportRoot, List<ExportNode> exportNodes,
            RootMotionCurves rootMotionCurves, float clipStartTime)
        {
            if (exportRoot == null || exportNodes == null || exportNodes.Count == 0)
            {
                return;
            }

            ExportNode rootNode = exportNodes[0];
            if (rootNode.transform != exportRoot)
            {
                return;
            }

            ResolveRootPose(exportRoot, rootMotionCurves, clipStartTime, out Vector3 position, out _,
                out Vector3 scale);
            rootNode.defaultLocalPosition = SanitizeVector3(position, rootNode.defaultLocalPosition);
            rootNode.defaultLocalScale = SanitizeVector3(scale, rootNode.defaultLocalScale);
        }

        private static void ApplyStrippedRootDefaults(Transform strippedRoot, List<ExportNode> exportNodes,
            RootMotionCurves rootMotionCurves, float clipStartTime)
        {
            if (strippedRoot == null || exportNodes == null)
            {
                return;
            }

            ResolveRootPose(strippedRoot, rootMotionCurves, clipStartTime, out Vector3 rootPosition, out Quaternion rootRotation,
                out Vector3 rootScale);

            for (int i = 0; i < exportNodes.Count; i++)
            {
                ExportNode node = exportNodes[i];
                if (node.parentIndex >= 0)
                {
                    continue;
                }

                ComposeLocalTransform(rootPosition, rootRotation, rootScale,
                    node.transform.localPosition, node.transform.localRotation, node.transform.localScale,
                    out Vector3 position, out Quaternion rotation, out Vector3 scale);

                node.defaultLocalPosition = SanitizeVector3(position, node.defaultLocalPosition);
                node.defaultLocalRotation = ToSignedEuler(rotation.eulerAngles);
                node.defaultLocalScale = SanitizeVector3(scale, node.defaultLocalScale);
            }
        }

        private static RootMotionCurves ExtractRootMotionCurves(AnimationClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            AnimationCurve[] positionCurves = TryGetEditorCurveGroup(clip, AnimatorRootPositionPropertyNames);
            AnimationCurve[] rotationCurves = TryGetEditorCurveGroup(clip, AnimatorRootRotationPropertyNames);
            if (positionCurves == null && rotationCurves == null)
            {
                return null;
            }

            return new RootMotionCurves
            {
                positionCurves = positionCurves,
                rotationCurves = rotationCurves
            };
        }

        private static AnimationCurve[] TryGetEditorCurveGroup(AnimationClip clip, string[] propertyNames)
        {
            if (clip == null || propertyNames == null || propertyNames.Length == 0)
            {
                return null;
            }

            var curves = new AnimationCurve[propertyNames.Length];
            for (int i = 0; i < propertyNames.Length; i++)
            {
                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), propertyNames[i]);
                curves[i] = AnimationUtility.GetEditorCurve(clip, binding);
                if (curves[i] == null)
                {
                    return null;
                }
            }

            return curves;
        }

        private static ClipCurveLookup BuildClipCurveLookup(AnimationClip clip)
        {
            var lookup = new ClipCurveLookup();
            if (clip == null)
            {
                return lookup;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Transform))
                {
                    continue;
                }

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null)
                {
                    continue;
                }

                lookup.transformCurves[MakeCurveLookupKey(binding.path, binding.propertyName)] = curve;
            }

            return lookup;
        }

        private static void AttachSourceCurves(List<ExportNode> exportNodes, ClipCurveLookup clipCurveLookup)
        {
            if (exportNodes == null || clipCurveLookup == null)
            {
                return;
            }

            for (int i = 0; i < exportNodes.Count; i++)
            {
                ExportNode node = exportNodes[i];
                string path = node.sourcePath ?? string.Empty;

                node.sourcePositionX = FindTransformCurve(clipCurveLookup, path, "localPosition.x", "m_LocalPosition.x");
                node.sourcePositionY = FindTransformCurve(clipCurveLookup, path, "localPosition.y", "m_LocalPosition.y");
                node.sourcePositionZ = FindTransformCurve(clipCurveLookup, path, "localPosition.z", "m_LocalPosition.z");

                node.sourceScaleX = FindTransformCurve(clipCurveLookup, path, "localScale.x", "m_LocalScale.x");
                node.sourceScaleY = FindTransformCurve(clipCurveLookup, path, "localScale.y", "m_LocalScale.y");
                node.sourceScaleZ = FindTransformCurve(clipCurveLookup, path, "localScale.z", "m_LocalScale.z");

                node.sourceEulerX = FindTransformCurve(clipCurveLookup, path, "localEulerAnglesRaw.x",
                    "localEulerAnglesBaked.x", "localEulerAngles.x");
                node.sourceEulerY = FindTransformCurve(clipCurveLookup, path, "localEulerAnglesRaw.y",
                    "localEulerAnglesBaked.y", "localEulerAngles.y");
                node.sourceEulerZ = FindTransformCurve(clipCurveLookup, path, "localEulerAnglesRaw.z",
                    "localEulerAnglesBaked.z", "localEulerAngles.z");
            }
        }

        private static AnimationCurve FindTransformCurve(ClipCurveLookup lookup, string path, params string[] propertyNames)
        {
            if (lookup == null || propertyNames == null)
            {
                return null;
            }

            for (int i = 0; i < propertyNames.Length; i++)
            {
                if (lookup.transformCurves.TryGetValue(MakeCurveLookupKey(path, propertyNames[i]), out AnimationCurve curve))
                {
                    return curve;
                }
            }

            return null;
        }

        private static string MakeCurveLookupKey(string path, string propertyName)
        {
            return $"{path ?? string.Empty}|{propertyName ?? string.Empty}";
        }

        private static bool ValidateOptions(ExportOptions options, out float normalizedStartTime,
            out float normalizedEndTime, out float sampleRate, out string error)
        {
            normalizedStartTime = 0f;
            normalizedEndTime = 0f;
            sampleRate = 30f;
            error = string.Empty;

            if (options == null)
            {
                error = "Export options are missing.";
                return false;
            }

            if (options.model == null)
            {
                error = "Assign a model root to export.";
                return false;
            }

            if (options.clip == null)
            {
                error = "Assign an animation clip to export.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.outputAssetPath))
            {
                error = "Select an output path inside Assets.";
                return false;
            }

            string outputAssetPath = NormalizeAssetPath(options.outputAssetPath);
            if (!outputAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(outputAssetPath, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                error = "Output path must be inside the project's Assets folder.";
                return false;
            }

            sampleRate = ResolveSampleRate(options.sampleRate, options.clip);
            NormalizeClipRange(options.clip, options.startTime, options.endTime, out normalizedStartTime,
                out normalizedEndTime);

            return true;
        }

        private static void NormalizeClipRange(AnimationClip clip, float startTime, float endTime,
            out float normalizedStartTime, out float normalizedEndTime)
        {
            float clipLength = clip != null ? Mathf.Max(0f, clip.length) : 0f;
            normalizedStartTime = Mathf.Clamp(startTime, 0f, clipLength);

            float resolvedEndTime = endTime;
            if (clip != null && resolvedEndTime <= 0f)
            {
                resolvedEndTime = clip.length;
            }

            normalizedEndTime = Mathf.Clamp(resolvedEndTime, normalizedStartTime, clipLength);
        }

        private static float ResolveSampleRate(float requestedSampleRate, AnimationClip clip)
        {
            float resolved = requestedSampleRate;
            if (resolved <= 0f && clip != null)
            {
                resolved = clip.frameRate;
            }

            if (resolved <= 0f)
            {
                resolved = 30f;
            }

            return Mathf.Clamp(resolved, MinSampleRate, MaxSampleRate);
        }

        private static List<long> ConvertSampleTimesToFbxTicks(IReadOnlyList<float> sampleTimes)
        {
            var result = new List<long>(sampleTimes.Count);
            for (int i = 0; i < sampleTimes.Count; i++)
            {
                result.Add(SecondsToFbxTicks(sampleTimes[i]));
            }

            return result;
        }

        private static long SecondsToFbxTicks(float seconds)
        {
            return (long)Math.Round(seconds * FbxTicksPerSecond, MidpointRounding.AwayFromZero);
        }

        private static Vector3 SanitizeVector3(Vector3 value, Vector3 fallback)
        {
            value.x = SanitizeFloat(value.x, fallback.x);
            value.y = SanitizeFloat(value.y, fallback.y);
            value.z = SanitizeFloat(value.z, fallback.z);
            return value;
        }

        private static Vector3 ScaleVector3(Vector3 value, float scale)
        {
            return Mathf.Approximately(scale, 1f) ? value : value * scale;
        }

        private static float ScaleFloat(float value, float scale)
        {
            return Mathf.Approximately(scale, 1f) ? value : value * scale;
        }

        private static int FloatToIntBits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        private static void ResolveRootPose(Transform rootTransform, RootMotionCurves rootMotionCurves, float clipTime,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = rootTransform != null ? rootTransform.localPosition : Vector3.zero;
            rotation = rootTransform != null ? rootTransform.localRotation : Quaternion.identity;
            scale = rootTransform != null ? rootTransform.localScale : Vector3.one;

            if (rootMotionCurves == null)
            {
                return;
            }

            if (rootMotionCurves.HasPosition)
            {
                position = new Vector3(
                    rootMotionCurves.positionCurves[0].Evaluate(clipTime),
                    rootMotionCurves.positionCurves[1].Evaluate(clipTime),
                    rootMotionCurves.positionCurves[2].Evaluate(clipTime));
            }

            if (rootMotionCurves.HasRotation)
            {
                rotation = new Quaternion(
                    rootMotionCurves.rotationCurves[0].Evaluate(clipTime),
                    rootMotionCurves.rotationCurves[1].Evaluate(clipTime),
                    rootMotionCurves.rotationCurves[2].Evaluate(clipTime),
                    rootMotionCurves.rotationCurves[3].Evaluate(clipTime));

                if (Quaternion.Dot(rotation, rotation) > CurveEpsilon)
                {
                    rotation.Normalize();
                }
                else
                {
                    rotation = Quaternion.identity;
                }
            }
        }

        private static void ComposeLocalTransform(Vector3 parentPosition, Quaternion parentRotation, Vector3 parentScale,
            Vector3 childPosition, Quaternion childRotation, Vector3 childScale,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(parentPosition, parentRotation, parentScale) *
                               Matrix4x4.TRS(childPosition, childRotation, childScale);

            position = matrix.GetColumn(3);

            Vector3 axisX = new Vector3(matrix.m00, matrix.m10, matrix.m20);
            Vector3 axisY = new Vector3(matrix.m01, matrix.m11, matrix.m21);
            Vector3 axisZ = new Vector3(matrix.m02, matrix.m12, matrix.m22);

            scale = new Vector3(axisX.magnitude, axisY.magnitude, axisZ.magnitude);
            if (Vector3.Dot(Vector3.Cross(axisX, axisY), axisZ) < 0f)
            {
                scale.x = -scale.x;
                axisX = -axisX;
            }

            bool hasXAxis = Mathf.Abs(scale.x) > CurveEpsilon;
            bool hasYAxis = Mathf.Abs(scale.y) > CurveEpsilon;
            bool hasZAxis = Mathf.Abs(scale.z) > CurveEpsilon;

            if (hasXAxis)
            {
                axisX /= scale.x;
            }

            if (hasYAxis)
            {
                axisY /= scale.y;
            }

            if (hasZAxis)
            {
                axisZ /= scale.z;
            }

            if (!hasXAxis || !hasYAxis || !hasZAxis)
            {
                rotation = parentRotation * childRotation;
                return;
            }

            axisX.Normalize();
            axisY.Normalize();
            axisZ.Normalize();
            rotation = Quaternion.LookRotation(axisZ, axisY);
        }

        private static float SanitizeFloat(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return value;
        }

        private static Vector3 ToSignedEuler(Vector3 euler)
        {
            return new Vector3(ToSignedAngle(euler.x), ToSignedAngle(euler.y), ToSignedAngle(euler.z));
        }

        private static Vector3 ConvertUnityLocalPositionToFbx(Vector3 position)
        {
            return new Vector3(-position.x, position.y, position.z);
        }

        private static Vector3 ConvertFbxLocalPositionToUnity(Vector3 position)
        {
            return new Vector3(-position.x, position.y, position.z);
        }

        private static Quaternion ConvertUnityLocalRotationToFbx(Quaternion rotation)
        {
            Quaternion converted = new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
            if (Quaternion.Dot(converted, converted) > CurveEpsilon)
            {
                converted.Normalize();
                return converted;
            }

            return Quaternion.identity;
        }

        private static Vector3 UnwrapEuler(Vector3 previous, Vector3 current)
        {
            current.x = previous.x + Mathf.DeltaAngle(previous.x, current.x);
            current.y = previous.y + Mathf.DeltaAngle(previous.y, current.y);
            current.z = previous.z + Mathf.DeltaAngle(previous.z, current.z);
            return current;
        }

        private static float ToSignedAngle(float angle)
        {
            while (angle > 180f)
            {
                angle -= 360f;
            }

            while (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath.Replace('\\', '/').Trim();
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), NormalizeAssetPath(assetPath)));
        }

        private static string BuildAsciiDocument(ExportDocument document)
        {
            var builder = new StringBuilder(1024 * 1024);
            AppendHeader(builder);
            AppendGlobalSettings(builder, document.stopTime);
            AppendDocuments(builder);
            AppendReferences(builder);
            AppendDefinitions(builder, document);
            AppendObjects(builder, document);
            AppendConnections(builder, document);
            AppendTakes(builder, document);
            return builder.ToString();
        }

        private static void AppendHeader(StringBuilder builder)
        {
            DateTime now = DateTime.Now;

            builder.AppendLine("; FBX 7.7.0 project file");
            builder.AppendLine("; ----------------------------------------------------");
            builder.AppendLine($"; ExporterVersion: {ExporterVersionTag}");
            builder.AppendLine();
            builder.AppendLine("FBXHeaderExtension:  {");
            builder.AppendLine("\tFBXHeaderVersion: 1003");
            builder.AppendLine("\tFBXVersion: 7700");
            builder.AppendLine("\tCreationTimeStamp:  {");
            builder.AppendLine("\t\tVersion: 1000");
            builder.AppendLine($"\t\tYear: {now.Year}");
            builder.AppendLine($"\t\tMonth: {now.Month}");
            builder.AppendLine($"\t\tDay: {now.Day}");
            builder.AppendLine($"\t\tHour: {now.Hour}");
            builder.AppendLine($"\t\tMinute: {now.Minute}");
            builder.AppendLine($"\t\tSecond: {now.Second}");
            builder.AppendLine($"\t\tMillisecond: {now.Millisecond}");
            builder.AppendLine("\t}");
            builder.AppendLine($"\tCreator: \"{ExporterDisplayName} {ExporterVersionTag}\"");
            builder.AppendLine("}");
        }

        private static void AppendGlobalSettings(StringBuilder builder, long stopTime)
        {
            builder.AppendLine("GlobalSettings:  {");
            builder.AppendLine("\tVersion: 1000");
            builder.AppendLine("\tProperties70:  {");
            builder.AppendLine("\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine("\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine("\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
            builder.AppendLine("\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine("\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
            builder.AppendLine("\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine("\t\tP: \"OriginalUpAxis\", \"int\", \"Integer\", \"\",-1");
            builder.AppendLine("\t\tP: \"OriginalUpAxisSign\", \"int\", \"Integer\", \"\",1");
            builder.AppendLine($"\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",{FormatFloat(FbxFileUnitScaleFactor)}");
            builder.AppendLine($"\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",{FormatFloat(FbxFileUnitScaleFactor)}");
            builder.AppendLine("\t\tP: \"AmbientColor\", \"ColorRGB\", \"Color\", \"\",0,0,0");
            builder.AppendLine("\t\tP: \"DefaultCamera\", \"KString\", \"\", \"\", \"Producer Perspective\"");
            builder.AppendLine("\t\tP: \"TimeMode\", \"enum\", \"\", \"\",6");
            builder.AppendLine("\t\tP: \"TimeSpanStart\", \"KTime\", \"Time\", \"\",0");
            builder.AppendLine($"\t\tP: \"TimeSpanStop\", \"KTime\", \"Time\", \"\",{stopTime}");
            builder.AppendLine("\t\tP: \"CustomFrameRate\", \"double\", \"Number\", \"\",-1");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendDocuments(StringBuilder builder)
        {
            builder.AppendLine("Documents:  {");
            builder.AppendLine("\tCount: 1");
            builder.AppendLine("\tDocument: 1, \"Scene\", \"Scene\" {");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine("\t\t\tP: \"SourceObject\", \"object\", \"\", \"\"");
            builder.AppendLine("\t\t\tP: \"ActiveAnimStackName\", \"KString\", \"\", \"\", \"\"");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t\tRootNode: 0");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendReferences(StringBuilder builder)
        {
            builder.AppendLine("References:  {");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendDefinitions(StringBuilder builder, ExportDocument document)
        {
            int modelCount = document.nodes.Count + (document.rootTrack != null ? 1 : 0);
            int rootCurveNodeCount = document.rootTrack != null ? (document.includeScaleCurves ? 3 : 2) : 0;
            int rootCurveCount = document.rootTrack != null ? (document.includeScaleCurves ? 9 : 6) : 0;
            int curveNodeCount = rootCurveNodeCount + document.nodes.Count * (document.includeScaleCurves ? 3 : 2);
            int curveCount = rootCurveCount + document.nodes.Count * (document.includeScaleCurves ? 9 : 6);
            int totalCount = 1 + modelCount + 1 + 1 + curveNodeCount + curveCount;

            builder.AppendLine("Definitions:  {");
            builder.AppendLine("\tVersion: 100");
            builder.AppendLine($"\tCount: {totalCount}");
            builder.AppendLine("\tObjectType: \"GlobalSettings\" {");
            builder.AppendLine("\t\tCount: 1");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"Model\" {");
            builder.AppendLine($"\t\tCount: {modelCount}");
            builder.AppendLine("\t\tPropertyTemplate: \"FbxNode\" {");
            builder.AppendLine("\t\t\tProperties70:  {");
            builder.AppendLine("\t\t\t\tP: \"RotationOrder\", \"enum\", \"\", \"\",4");
            builder.AppendLine("\t\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            builder.AppendLine("\t\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            builder.AppendLine("\t\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
            builder.AppendLine("\t\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",0,0,0");
            builder.AppendLine("\t\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
            builder.AppendLine("\t\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
            builder.AppendLine("\t\t\t\tP: \"Visibility\", \"Visibility\", \"\", \"A\",1");
            builder.AppendLine("\t\t\t}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"AnimationStack\" {");
            builder.AppendLine("\t\tCount: 1");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"AnimationLayer\" {");
            builder.AppendLine("\t\tCount: 1");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"AnimationCurveNode\" {");
            builder.AppendLine($"\t\tCount: {curveNodeCount}");
            builder.AppendLine("\t\tPropertyTemplate: \"FbxAnimCurveNode\" {");
            builder.AppendLine("\t\t\tProperties70:  {");
            builder.AppendLine("\t\t\t\tP: \"d\", \"Compound\", \"\", \"\"");
            builder.AppendLine("\t\t\t}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
            builder.AppendLine("\tObjectType: \"AnimationCurve\" {");
            builder.AppendLine($"\t\tCount: {curveCount}");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendObjects(StringBuilder builder, ExportDocument document)
        {
            builder.AppendLine("Objects:  {");

            if (document.rootTrack != null)
            {
                AppendSceneRootModel(builder, document.sceneRootModelId, document.sceneName, document.rootTrack);
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                AppendModel(builder, document.nodes[i], "Null");
            }

            AppendAnimationStack(builder, document.animationStackId, document.sceneName, document.stopTime);
            AppendAnimationLayer(builder, document.animationLayerId);

            if (document.rootTrack != null)
            {
                AppendCurveNode(builder, document.rootTrack.translationNode);
                AppendCurveNode(builder, document.rootTrack.rotationNode);
                if (document.includeScaleCurves && document.rootTrack.scaleNode != null)
                {
                    AppendCurveNode(builder, document.rootTrack.scaleNode);
                }
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                AppendCurveNode(builder, node.translationNode);
                AppendCurveNode(builder, node.rotationNode);
                if (document.includeScaleCurves && node.scaleNode != null)
                {
                    AppendCurveNode(builder, node.scaleNode);
                }
            }

            if (document.rootTrack != null)
            {
                AppendCurve(builder, document.rootTrack.translationNode.xCurve);
                AppendCurve(builder, document.rootTrack.translationNode.yCurve);
                AppendCurve(builder, document.rootTrack.translationNode.zCurve);
                AppendCurve(builder, document.rootTrack.rotationNode.xCurve);
                AppendCurve(builder, document.rootTrack.rotationNode.yCurve);
                AppendCurve(builder, document.rootTrack.rotationNode.zCurve);

                if (document.includeScaleCurves && document.rootTrack.scaleNode != null)
                {
                    AppendCurve(builder, document.rootTrack.scaleNode.xCurve);
                    AppendCurve(builder, document.rootTrack.scaleNode.yCurve);
                    AppendCurve(builder, document.rootTrack.scaleNode.zCurve);
                }
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                AppendCurve(builder, node.translationNode.xCurve);
                AppendCurve(builder, node.translationNode.yCurve);
                AppendCurve(builder, node.translationNode.zCurve);
                AppendCurve(builder, node.rotationNode.xCurve);
                AppendCurve(builder, node.rotationNode.yCurve);
                AppendCurve(builder, node.rotationNode.zCurve);

                if (document.includeScaleCurves && node.scaleNode != null)
                {
                    AppendCurve(builder, node.scaleNode.xCurve);
                    AppendCurve(builder, node.scaleNode.yCurve);
                    AppendCurve(builder, node.scaleNode.zCurve);
                }
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendSceneRootModel(StringBuilder builder, long modelId, string sceneName,
            ExportRootTrack rootTrack)
        {
            builder.AppendLine($"\tModel: {modelId}, \"Model::{EscapeFbxString(sceneName)}\", \"Null\" {{");
            builder.AppendLine("\t\tVersion: 232");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine("\t\t\tP: \"RotationOrder\", \"enum\", \"\", \"\",4");
            builder.AppendLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            builder.AppendLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            builder.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
            Vector3 translation = rootTrack != null ? rootTrack.defaultLocalPosition : Vector3.zero;
            Vector3 rotation = rootTrack != null ? rootTrack.defaultLocalRotation : Vector3.zero;
            Vector3 scale = rootTrack != null ? rootTrack.defaultLocalScale : Vector3.one;
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A+\",{FormatVector3(ScaleVector3(translation, FbxTranslationScale))}");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A+\",{FormatVector3(rotation)}");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A+\",{FormatVector3(scale)}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t\tShading: Y");
            builder.AppendLine("\t\tCulling: \"CullingOff\"");
            builder.AppendLine("\t}");
        }

        private static void AppendModel(StringBuilder builder, ExportNode node, string modelType)
        {
            builder.AppendLine(
                $"\tModel: {node.modelId}, \"Model::{EscapeFbxString(node.exportName)}\", \"{modelType}\" {{");
            builder.AppendLine("\t\tVersion: 232");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine("\t\t\tP: \"RotationOrder\", \"enum\", \"\", \"\",4");
            builder.AppendLine("\t\t\tP: \"RotationActive\", \"bool\", \"\", \"\",1");
            builder.AppendLine("\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
            builder.AppendLine("\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
            builder.AppendLine("\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A+\",{FormatVector3(ScaleVector3(node.defaultLocalPosition, FbxTranslationScale))}");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A+\",{FormatVector3(node.defaultLocalRotation)}");
            builder.AppendLine(
                $"\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A+\",{FormatVector3(node.defaultLocalScale)}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t\tShading: Y");
            builder.AppendLine("\t\tCulling: \"CullingOff\"");
            builder.AppendLine("\t}");
        }

        private static void AppendAnimationStack(StringBuilder builder, long animationStackId, string sceneName,
            long stopTime)
        {
            builder.AppendLine(
                $"\tAnimationStack: {animationStackId}, \"AnimStack::{EscapeFbxString(sceneName)}\", \"\" {{");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine(
                $"\t\t\tP: \"Description\", \"KString\", \"\", \"\", \"Animation Take: {EscapeFbxString(sceneName)}\"");
            builder.AppendLine("\t\t\tP: \"LocalStart\", \"KTime\", \"Time\", \"\",0");
            builder.AppendLine($"\t\t\tP: \"LocalStop\", \"KTime\", \"Time\", \"\",{stopTime}");
            builder.AppendLine("\t\t\tP: \"ReferenceStart\", \"KTime\", \"Time\", \"\",0");
            builder.AppendLine($"\t\t\tP: \"ReferenceStop\", \"KTime\", \"Time\", \"\",{stopTime}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        private static void AppendAnimationLayer(StringBuilder builder, long animationLayerId)
        {
            builder.AppendLine($"\tAnimationLayer: {animationLayerId}, \"AnimLayer::Animation Base Layer\", \"\" {{");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine("\t\t\tP: \"Weight\", \"Number\", \"\", \"A\",100");
            builder.AppendLine("\t\t\tP: \"Mute\", \"bool\", \"\", \"\",0");
            builder.AppendLine("\t\t\tP: \"Solo\", \"bool\", \"\", \"\",0");
            builder.AppendLine("\t\t\tP: \"Lock\", \"bool\", \"\", \"\",0");
            builder.AppendLine("\t\t\tP: \"BlendMode\", \"enum\", \"\", \"\",0");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        private static void AppendCurveNode(StringBuilder builder, FbxCurveNode curveNode)
        {
            builder.AppendLine(
                $"\tAnimationCurveNode: {curveNode.id}, \"AnimCurveNode::{curveNode.label}\", \"\" {{");
            builder.AppendLine("\t\tProperties70:  {");
            builder.AppendLine($"\t\t\tP: \"d|X\", \"Number\", \"\", \"A\",{FormatFloat(curveNode.defaultValue.x)}");
            builder.AppendLine($"\t\t\tP: \"d|Y\", \"Number\", \"\", \"A\",{FormatFloat(curveNode.defaultValue.y)}");
            builder.AppendLine($"\t\t\tP: \"d|Z\", \"Number\", \"\", \"A\",{FormatFloat(curveNode.defaultValue.z)}");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        private static void AppendCurve(StringBuilder builder, FbxCurve curve)
        {
            int keyCount = curve.keyTimes.Count;
            int keyAttrFlagsCount = curve.keyAttrFlags != null ? curve.keyAttrFlags.Count : 0;
            int keyAttrDataCount = curve.keyAttrData != null ? curve.keyAttrData.Count : 0;
            int keyAttrRefCount = curve.keyAttrRefCount != null ? curve.keyAttrRefCount.Count : 0;
            builder.AppendLine($"\tAnimationCurve: {curve.id}, \"AnimCurve::\", \"\" {{");
            builder.AppendLine($"\t\tDefault: {FormatFloat(curve.defaultValue)}");
            builder.AppendLine("\t\tKeyVer: 4009");

            builder.AppendLine($"\t\tKeyTime: *{keyCount} {{");
            builder.Append("\t\t\ta: ");
            AppendSeparated(builder, curve.keyTimes, FormatLong);
            builder.AppendLine();
            builder.AppendLine("\t\t}");

            builder.AppendLine($"\t\tKeyValueFloat: *{keyCount} {{");
            builder.Append("\t\t\ta: ");
            AppendSeparated(builder, curve.keyValues, FormatFloat);
            builder.AppendLine();
            builder.AppendLine("\t\t}");

            builder.AppendLine($"\t\tKeyAttrFlags: *{keyAttrFlagsCount} {{");
            builder.Append("\t\t\ta: ");
            AppendSeparated(builder, curve.keyAttrFlags, value => value.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("\t\t}");

            builder.AppendLine($"\t\tKeyAttrDataFloat: *{keyAttrDataCount} {{");
            builder.Append("\t\t\ta: ");
            AppendSeparated(builder, curve.keyAttrData, value => value.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("\t\t}");

            builder.AppendLine($"\t\tKeyAttrRefCount: *{keyAttrRefCount} {{");
            builder.Append("\t\t\ta: ");
            AppendSeparated(builder, curve.keyAttrRefCount, value => value.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        private static void AppendConnections(StringBuilder builder, ExportDocument document)
        {
            builder.AppendLine("Connections:  {");
            builder.AppendLine($"\tC: \"OO\",{document.animationLayerId},{document.animationStackId}");

            if (document.rootTrack != null)
            {
                builder.AppendLine($"\tC: \"OO\",{document.sceneRootModelId},0");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.translationNode.id},{document.sceneRootModelId},\"Lcl Translation\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.rotationNode.id},{document.sceneRootModelId},\"Lcl Rotation\"");
                builder.AppendLine($"\tC: \"OO\",{document.rootTrack.translationNode.id},{document.animationLayerId}");
                builder.AppendLine($"\tC: \"OO\",{document.rootTrack.rotationNode.id},{document.animationLayerId}");

                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.translationNode.xCurve.id},{document.rootTrack.translationNode.id},\"d|X\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.translationNode.yCurve.id},{document.rootTrack.translationNode.id},\"d|Y\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.translationNode.zCurve.id},{document.rootTrack.translationNode.id},\"d|Z\"");

                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.rotationNode.xCurve.id},{document.rootTrack.rotationNode.id},\"d|X\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.rotationNode.yCurve.id},{document.rootTrack.rotationNode.id},\"d|Y\"");
                builder.AppendLine($"\tC: \"OP\",{document.rootTrack.rotationNode.zCurve.id},{document.rootTrack.rotationNode.id},\"d|Z\"");

                if (document.includeScaleCurves && document.rootTrack.scaleNode != null)
                {
                    builder.AppendLine($"\tC: \"OP\",{document.rootTrack.scaleNode.id},{document.sceneRootModelId},\"Lcl Scaling\"");
                    builder.AppendLine($"\tC: \"OO\",{document.rootTrack.scaleNode.id},{document.animationLayerId}");
                    builder.AppendLine($"\tC: \"OP\",{document.rootTrack.scaleNode.xCurve.id},{document.rootTrack.scaleNode.id},\"d|X\"");
                    builder.AppendLine($"\tC: \"OP\",{document.rootTrack.scaleNode.yCurve.id},{document.rootTrack.scaleNode.id},\"d|Y\"");
                    builder.AppendLine($"\tC: \"OP\",{document.rootTrack.scaleNode.zCurve.id},{document.rootTrack.scaleNode.id},\"d|Z\"");
                }
            }

            for (int i = 0; i < document.nodes.Count; i++)
            {
                ExportNode node = document.nodes[i];
                long parentModelId = node.parentIndex >= 0
                    ? document.nodes[node.parentIndex].modelId
                    : (document.rootTrack != null ? document.sceneRootModelId : 0);

                builder.AppendLine($"\tC: \"OO\",{node.modelId},{parentModelId}");

                builder.AppendLine($"\tC: \"OP\",{node.translationNode.id},{node.modelId},\"Lcl Translation\"");
                builder.AppendLine($"\tC: \"OP\",{node.rotationNode.id},{node.modelId},\"Lcl Rotation\"");
                builder.AppendLine($"\tC: \"OO\",{node.translationNode.id},{document.animationLayerId}");
                builder.AppendLine($"\tC: \"OO\",{node.rotationNode.id},{document.animationLayerId}");

                builder.AppendLine($"\tC: \"OP\",{node.translationNode.xCurve.id},{node.translationNode.id},\"d|X\"");
                builder.AppendLine($"\tC: \"OP\",{node.translationNode.yCurve.id},{node.translationNode.id},\"d|Y\"");
                builder.AppendLine($"\tC: \"OP\",{node.translationNode.zCurve.id},{node.translationNode.id},\"d|Z\"");

                builder.AppendLine($"\tC: \"OP\",{node.rotationNode.xCurve.id},{node.rotationNode.id},\"d|X\"");
                builder.AppendLine($"\tC: \"OP\",{node.rotationNode.yCurve.id},{node.rotationNode.id},\"d|Y\"");
                builder.AppendLine($"\tC: \"OP\",{node.rotationNode.zCurve.id},{node.rotationNode.id},\"d|Z\"");

                if (document.includeScaleCurves && node.scaleNode != null)
                {
                    builder.AppendLine($"\tC: \"OP\",{node.scaleNode.id},{node.modelId},\"Lcl Scaling\"");
                    builder.AppendLine($"\tC: \"OO\",{node.scaleNode.id},{document.animationLayerId}");
                    builder.AppendLine($"\tC: \"OP\",{node.scaleNode.xCurve.id},{node.scaleNode.id},\"d|X\"");
                    builder.AppendLine($"\tC: \"OP\",{node.scaleNode.yCurve.id},{node.scaleNode.id},\"d|Y\"");
                    builder.AppendLine($"\tC: \"OP\",{node.scaleNode.zCurve.id},{node.scaleNode.id},\"d|Z\"");
                }
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        private static void AppendTakes(StringBuilder builder, ExportDocument document)
        {
            builder.AppendLine("Takes:  {");
            builder.AppendLine("\tCurrent: \"\"");
            builder.AppendLine($"\tTake: \"{EscapeFbxString(document.sceneName)}\" {{");
            builder.AppendLine($"\t\tFileName: \"{EscapeFbxString(document.sceneName)}.tak\"");
            builder.AppendLine($"\t\tLocalTime: 0,{document.stopTime}");
            builder.AppendLine($"\t\tReferenceTime: 0,{document.stopTime}");
            builder.AppendLine($"\t\tComments: \"Animation Take: {EscapeFbxString(document.sceneName)}\"");
            builder.AppendLine("\t}");
            builder.AppendLine("}");
        }

        private static void AppendSeparated<T>(StringBuilder builder, IReadOnlyList<T> values, Func<T, string> formatter)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(formatter(values[i]));
            }
        }

        private static string EscapeFbxString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"{FormatFloat(value.x)},{FormatFloat(value.y)},{FormatFloat(value.z)}";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatLong(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool IsAsciiFbxExportAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string absolutePath = GetAbsoluteProjectPath(assetPath);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                using var reader = new StreamReader(absolutePath);
                for (int i = 0; i < 24 && !reader.EndOfStream; i++)
                {
                    string line = reader.ReadLine();
                    if (line != null && (line.IndexOf(ExporterDisplayName, StringComparison.Ordinal) >= 0 || line.IndexOf(LegacyExporterDisplayName, StringComparison.Ordinal) >= 0))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static void ConfigureImporter(ModelImporter importer)
        {
            if (importer == null)
            {
                return;
            }

            if (ApplyImporterSettings(importer))
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        public static bool ApplyImporterSettings(ModelImporter importer)
        {
            if (importer == null)
            {
                return false;
            }

            bool changed = false;

            if (!importer.importAnimation)
            {
                importer.importAnimation = true;
                changed = true;
            }

            if (!importer.useFileUnits)
            {
                importer.useFileUnits = true;
                changed = true;
            }

            if (!importer.useFileScale)
            {
                importer.useFileScale = true;
                changed = true;
            }

            if (!Mathf.Approximately(importer.globalScale, 1f))
            {
                importer.globalScale = 1f;
                changed = true;
            }

            if (!importer.preserveHierarchy)
            {
                importer.preserveHierarchy = true;
                changed = true;
            }

            if (importer.bakeAxisConversion)
            {
                importer.bakeAxisConversion = false;
                changed = true;
            }

            if (importer.resampleCurves)
            {
                importer.resampleCurves = false;
                changed = true;
            }

            if (importer.animationCompression != ModelImporterAnimationCompression.Off)
            {
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                changed = true;
            }

            return changed;
        }
    }
}