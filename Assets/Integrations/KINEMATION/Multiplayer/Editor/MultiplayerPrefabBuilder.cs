using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer.Editor
{
    /// <summary>
    /// Builds the multiplayer prefab variant from the offline CAS/Tactical player prefab.
    /// Replaces FPSExampleController with NetworkFPSExampleController, adds NetworkObject and
    /// NetworkCasPlayer, and disables owner-only components by default so the prefab starts in
    /// a neutral state and NetworkCasPlayer.OnNetworkSpawn enables them only for IsOwner.
    /// </summary>
    public static class MultiplayerPrefabBuilder
    {
        private const string SourcePrefabPath =
            "Assets/Integrations/KINEMATION/CAS_Player_Example_FPS_Tactical.prefab";

        private const string OutputPrefabPath =
            "Assets/FPSProject/Multiplayer/Prefabs/CAS_Player_Network.prefab";

        private const string TuningAssetPath =
            "Assets/FPSProject/Multiplayer/Core/Resources/MultiplayerTuningSettings.asset";

        [MenuItem("Tools/FPSProject/Build Multiplayer Player Prefab")]
        public static void Build()
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
            {
                Debug.LogError($"Source prefab not found: {SourcePrefabPath}");
                return;
            }

            // Ensure output folder exists.
            string folder = System.IO.Path.GetDirectoryName(OutputPrefabPath);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets/FPSProject/Multiplayer", "Prefabs");
            }

            // Instantiate the source prefab temporarily so we can modify it, then save as a
            // prefab variant. We use SaveAsPrefabAssetAndConnect with InteractionMode.Automated
            // so the result is a true variant of the source.
            var tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            try
            {
                BuildInto(tempInstance);
                PrefabUtility.SaveAsPrefabAssetAndConnect(tempInstance, OutputPrefabPath,
                    InteractionMode.AutomatedAction, out bool savedSuccessfully);
                if (!savedSuccessfully)
                {
                    Debug.LogError($"Failed to save multiplayer prefab to {OutputPrefabPath}");
                    return;
                }

                // Assign the tuning asset to the saved prefab. We do this after saving so the
                // reference is stored on the prefab asset, not just the temp instance. The
                // tuning asset lives under a Resources/ folder so the runtime fallback
                // (Resources.LoadAll) can also find it if the serialized reference is lost.
                AssignTuningAsset();

                Debug.Log($"Built multiplayer prefab variant: {OutputPrefabPath}");
            }
            finally
            {
                if (tempInstance != null && tempInstance.scene.IsValid())
                {
                    Object.DestroyImmediate(tempInstance);
                }
            }
        }

        private static void AssignTuningAsset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPrefabPath);
            if (prefab == null) return;
            var netPlayer = prefab.GetComponent<NetworkCasPlayer>();
            if (netPlayer == null) return;

            var tuning = AssetDatabase.LoadAssetAtPath<FPSProject.Multiplayer.Core.Movement.MultiplayerTuningSettings>(TuningAssetPath);
            if (tuning == null)
            {
                Debug.LogWarning($"Tuning asset not found at {TuningAssetPath}. Create it or the runtime will fall back to Resources.LoadAll.");
                return;
            }

            var so = new SerializedObject(netPlayer);
            so.FindProperty("tuning").objectReferenceValue = tuning;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }

        private static void BuildInto(GameObject root)
        {
            // 1. Replace FPSExampleController with NetworkFPSExampleController.
            //    NetworkFPSExampleController derives from FPSExampleController, so we add the
            //    new component and remove the old one. We must preserve serialized fields
            //    that exist on both (the base class fields are inherited).
            var oldController = root.GetComponent<CAS_Demo.Scripts.FPS.FPSExampleController>();
            if (oldController == null)
            {
                Debug.LogError("Source prefab has no FPSExampleController on its root.");
                return;
            }

            // Copy serialized field values from the old controller before removing it.
            var serializedOld = new SerializedObject(oldController);
            var controllerFields = CaptureSerializedFields(serializedOld);

            Object.DestroyImmediate(oldController, true);
            var newController = root.AddComponent<NetworkFPSExampleController>();
            var serializedNew = new SerializedObject(newController);
            RestoreSerializedFields(serializedNew, controllerFields);
            serializedNew.ApplyModifiedPropertiesWithoutUndo();

            // 2. Add NetworkObject to the root.
            if (root.GetComponent<Unity.Netcode.NetworkObject>() == null)
            {
                root.AddComponent<Unity.Netcode.NetworkObject>();
            }

            // 3. Add NetworkCasPlayer and wire the controller reference.
            var netPlayer = root.GetComponent<NetworkCasPlayer>();
            if (netPlayer == null) netPlayer = root.AddComponent<NetworkCasPlayer>();
            var netPlayerSerialized = new SerializedObject(netPlayer);
            netPlayerSerialized.FindProperty("controller").objectReferenceValue = newController;
            netPlayerSerialized.ApplyModifiedPropertiesWithoutUndo();

            // 4. Disable owner-only components by default. NetworkCasPlayer.OnNetworkSpawn
            //    enables them for IsOwner and leaves them disabled for proxies.
            var playerInput = root.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null) playerInput.enabled = false;

            // The Camera and AudioListener live on the "Camera" child of the root.
            var cameraChild = root.transform.Find("Camera");
            if (cameraChild != null)
            {
                var cam = cameraChild.GetComponent<Camera>();
                if (cam != null) cam.enabled = false;
                var listener = cameraChild.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }

            // The Tactical Presentation child also has a PlayerInput and a nested FPCamera
            // with Camera + AudioListener. Disable those too; the bridge handles Tactical
            // presentation without its own input/camera.
            var tacticalChild = root.transform.Find("Tactical Presentation");
            if (tacticalChild != null)
            {
                var tacticalInput = tacticalChild.GetComponent<UnityEngine.InputSystem.PlayerInput>();
                if (tacticalInput != null) tacticalInput.enabled = false;

                // The vendor scripts have an order-dependent Start path: procedural animation
                // must initialize before the player equips a weapon, while its Animator must not
                // evaluate the job before those weapon settings exist. NetworkCasPlayer stages
                // that initialization deterministically for both owners and proxies.
                var tacPlayer = tacticalChild.GetComponent<KINEMATION.TacticalShooterPack.Scripts.Player.TacticalShooterPlayer>();
                if (tacPlayer != null) tacPlayer.enabled = false;
                var tacAnim = tacticalChild.GetComponent<KINEMATION.TacticalShooterPack.Scripts.Animation.TacticalProceduralAnimation>();
                if (tacAnim != null) tacAnim.enabled = false;

                var fpcam = tacticalChild.Find("SKM_Operator/root/pelvis/spine_01/spine_02/spine_03/spine_04/spine_05/neck_01/neck_02/head/FPCamera");
                if (fpcam != null)
                {
                    var fpCamComponent = fpcam.GetComponent<Camera>();
                    if (fpCamComponent != null) fpCamComponent.enabled = false;
                    var fpListener = fpcam.GetComponent<AudioListener>();
                    if (fpListener != null) fpListener.enabled = false;
                }
            }

            // Disable the RecoilAnimation on the root; NetworkCasPlayer re-enables it for owners.
            var recoil = root.GetComponent<KINEMATION.ProceduralRecoilAnimationSystem.Runtime.RecoilAnimation>();
            if (recoil != null) recoil.enabled = false;
        }

        private static Dictionary<string, object> CaptureSerializedFields(SerializedObject so)
        {
            var map = new Dictionary<string, object>();
            var prop = so.GetIterator();
            prop.NextVisible(true);
            while (prop.NextVisible(false))
            {
                if (prop.depth == 0) CaptureProperty(prop, map);
            }
            return map;
        }

        private static void RestoreSerializedFields(SerializedObject so, Dictionary<string, object> map)
        {
            var prop = so.GetIterator();
            prop.NextVisible(true);
            while (prop.NextVisible(false))
            {
                if (prop.depth == 0 && map.TryGetValue(prop.propertyPath, out object value))
                {
                    RestoreProperty(prop, value);
                }
            }
        }

        private static void CaptureProperty(SerializedProperty prop, Dictionary<string, object> map)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    map[prop.propertyPath] = prop.boolValue;
                    break;
                case SerializedPropertyType.Float:
                    map[prop.propertyPath] = prop.floatValue;
                    break;
                case SerializedPropertyType.Integer:
                    map[prop.propertyPath] = prop.intValue;
                    break;
                case SerializedPropertyType.ObjectReference:
                    map[prop.propertyPath] = prop.objectReferenceValue;
                    break;
                case SerializedPropertyType.Enum:
                    map[prop.propertyPath] = prop.enumValueIndex;
                    break;
                case SerializedPropertyType.String:
                    map[prop.propertyPath] = prop.stringValue;
                    break;
                case SerializedPropertyType.Vector2:
                    map[prop.propertyPath] = prop.vector2Value;
                    break;
                case SerializedPropertyType.Vector3:
                    map[prop.propertyPath] = prop.vector3Value;
                    break;
                case SerializedPropertyType.Vector4:
                    map[prop.propertyPath] = prop.vector4Value;
                    break;
                case SerializedPropertyType.Color:
                    map[prop.propertyPath] = prop.colorValue;
                    break;
                case SerializedPropertyType.LayerMask:
                    map[prop.propertyPath] = prop.intValue;
                    break;
            }
        }

        private static void RestoreProperty(SerializedProperty prop, object value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    prop.boolValue = (bool)value;
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = (float)value;
                    break;
                case SerializedPropertyType.Integer:
                    prop.intValue = (int)value;
                    break;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = (Object)value;
                    break;
                case SerializedPropertyType.Enum:
                    prop.enumValueIndex = (int)value;
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = (string)value;
                    break;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = (Vector2)value;
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = (Vector3)value;
                    break;
                case SerializedPropertyType.Vector4:
                    prop.vector4Value = (Vector4)value;
                    break;
                case SerializedPropertyType.Color:
                    prop.colorValue = (Color)value;
                    break;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = (int)value;
                    break;
            }
        }
    }
}
