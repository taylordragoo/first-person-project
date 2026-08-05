using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Weapons;
using KINEMATION.TacticalShooterPack.Scripts.Player;
using UnityEditor;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Multiplayer.Editor
{
    /// <summary>
    /// Builds Step 6 of the multiplayer plan: replaces the vendor TacticalShooterPlayer with
    /// NetworkTacticalShooterPlayer, adds NetworkWeaponState, wires the NetworkWeaponCatalog,
    /// and swaps the Tactical Presentation's weaponPrefabs array to use the networked weapon
    /// prefab variants that implement INetworkTacticalWeaponPresentation.
    /// </summary>
    public static class MultiplayerWeaponStateBuilder
    {
        private const string PlayerPrefabPath =
            "Assets/FPSProject/Multiplayer/Prefabs/CAS_Player_Network.prefab";

        private const string CatalogPath =
            "Assets/FPSProject/Multiplayer/Core/Resources/NetworkWeaponCatalog.asset";

        private const string VendorTacticalCharacterPath =
            "Assets/KINEMATION/TacticalShooterPack/Prefabs/TacticalShooterCharacter.prefab";

        // Networked weapon prefab variants, in the same order as the vendor TacticalShooterCharacter
        // weaponPrefabs array. The catalog maps weapon IDs to these indices. We swap every entry
        // that has a networked variant; entries without one keep the vendor prefab so the array
        // indices stay stable.
        private static readonly string[] NetworkedWeaponPaths =
        {
            "Assets/FPSProject/Multiplayer/Prefabs/Weapons/W_TR15_Network.prefab",
            "Assets/FPSProject/Multiplayer/Prefabs/Weapons/W_WK-11_Viper_Network.prefab",
            "Assets/FPSProject/Multiplayer/Prefabs/Weapons/W_Herrington_11-87_Police_Network.prefab",
            "Assets/FPSProject/Multiplayer/Prefabs/Weapons/W_Mk14EBR_Network.prefab"
        };

        // Vendor weapon prefab paths matched to networked variants above. We match by the vendor
        // prefab asset referenced in the TacticalShooterCharacter weaponPrefabs array.
        private static readonly string[] VendorWeaponPaths =
        {
            "Assets/KINEMATION/TacticalShooterPack/Prefabs/Weapons/W_TR15.prefab",
            "Assets/KINEMATION/TacticalShooterPack/Prefabs/Weapons/W_WK-11_Viper.prefab",
            "Assets/KINEMATION/TacticalShooterPack/Prefabs/Weapons/W_Herrington_11-87_Police.prefab",
            "Assets/KINEMATION/TacticalShooterPack/Prefabs/Weapons/W_Mk14EBR.prefab"
        };

        [MenuItem("Tools/FPSProject/Build Multiplayer Weapon State (Step 6)")]
        public static void Build()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Multiplayer player prefab not found: {PlayerPrefabPath}");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<NetworkWeaponCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"NetworkWeaponCatalog not found: {CatalogPath}");
                return;
            }

            var tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                BuildInto(tempInstance, catalog);
                PrefabUtility.SaveAsPrefabAssetAndConnect(tempInstance, PlayerPrefabPath,
                    InteractionMode.AutomatedAction, out bool savedSuccessfully);
                if (!savedSuccessfully)
                {
                    Debug.LogError($"Failed to save multiplayer prefab to {PlayerPrefabPath}");
                    return;
                }

                Debug.Log($"Built multiplayer weapon state into: {PlayerPrefabPath}");
            }
            finally
            {
                if (tempInstance != null && tempInstance.scene.IsValid())
                {
                    Object.DestroyImmediate(tempInstance);
                }
            }
        }

        private static void BuildInto(GameObject root, NetworkWeaponCatalog catalog)
        {
            // Locate the Tactical Presentation child and its vendor TacticalShooterPlayer.
            Transform tacticalChild = root.transform.Find("Tactical Presentation");
            if (tacticalChild == null)
            {
                Debug.LogError("Tactical Presentation child not found on player prefab.");
                return;
            }

            var vendorPlayer = tacticalChild.GetComponent<TacticalShooterPlayer>();
            if (vendorPlayer == null)
            {
                Debug.LogError("TacticalShooterPlayer not found on Tactical Presentation child.");
                return;
            }

            // Capture the vendor player's serialized fields (weaponPrefabs, camera, sounds, etc.)
            // before removing it, then restore them on the networked subclass. Older Step 6
            // builds used a shallow property copy that lost arrays. Recover those prefabs from
            // the known-good vendor character when rebuilding an affected network prefab.
            var sourcePlayer = ResolveSerializedSource(vendorPlayer);
            var fields = CaptureSerializedFields(new SerializedObject(sourcePlayer));

            var netPlayer = vendorPlayer as NetworkTacticalShooterPlayer;
            if (netPlayer == null)
            {
                Object.DestroyImmediate(vendorPlayer, true);
                netPlayer = tacticalChild.gameObject.AddComponent<NetworkTacticalShooterPlayer>();
            }

            if (netPlayer == null)
            {
                Debug.LogError("Could not add NetworkTacticalShooterPlayer to the multiplayer prefab.");
                return;
            }
            RestoreSerializedFields(new SerializedObject(netPlayer), fields);

            // Swap the weaponPrefabs array entries that have networked variants.
            SwapWeaponPrefabs(netPlayer);

            // Add NetworkWeaponState to the root (alongside NetworkCasPlayer and NetworkObject).
            var weaponState = root.GetComponent<NetworkWeaponState>();
            if (weaponState == null) weaponState = root.AddComponent<NetworkWeaponState>();

            var wsSo = new SerializedObject(weaponState);
            wsSo.FindProperty("catalog").objectReferenceValue = catalog;
            wsSo.ApplyModifiedPropertiesWithoutUndo();

            // Wire the NetworkCasPlayer's reference to the new NetworkTacticalShooterPlayer and
            // NetworkWeaponState so it can drive ID-based presentation on spawn.
            var netCas = root.GetComponent<NetworkCasPlayer>();
            if (netCas != null)
            {
                var ncpSo = new SerializedObject(netCas);
                ncpSo.FindProperty("tacticalPlayer").objectReferenceValue = netPlayer;
                ncpSo.FindProperty("weaponState").objectReferenceValue = weaponState;
                ncpSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // Add the network shot router and weapon prop bridge. The router converts owner
            // shots into NetworkShotCommands; the bridge wires the CAS weapon props to it.
            if (root.GetComponent<NetworkWeaponShotRouter>() == null)
                root.AddComponent<NetworkWeaponShotRouter>();
            if (root.GetComponent<NetworkWeaponPropBridge>() == null)
                root.AddComponent<NetworkWeaponPropBridge>();

            // Step 10 lifecycle components are part of the generated multiplayer prefab. Keep
            // this builder idempotent so rebuilding weapon state cannot silently remove health.
            var networkHealth = root.GetComponent<NetworkHealth>();
            if (networkHealth == null) networkHealth = root.AddComponent<NetworkHealth>();
            if (root.GetComponent<NetworkPlayerLifecycle>() == null)
                root.AddComponent<NetworkPlayerLifecycle>();

            if (netCas != null)
            {
                var ncpSo = new SerializedObject(netCas);
                ncpSo.FindProperty("networkHealth").objectReferenceValue = networkHealth;
                ncpSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static TacticalShooterPlayer ResolveSerializedSource(
            TacticalShooterPlayer currentPlayer)
        {
            var current = new SerializedObject(currentPlayer);
            var weapons = current.FindProperty("weaponPrefabs");
            if (weapons != null && weapons.isArray && weapons.arraySize > 0)
                return currentPlayer;

            var vendorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                VendorTacticalCharacterPath);
            var vendorPlayer = vendorPrefab != null
                ? vendorPrefab.GetComponent<TacticalShooterPlayer>()
                : null;
            return vendorPlayer != null ? vendorPlayer : currentPlayer;
        }

        private static void SwapWeaponPrefabs(NetworkTacticalShooterPlayer netPlayer)
        {
            var so = new SerializedObject(netPlayer);
            var weaponPrefabsProp = so.FindProperty("weaponPrefabs");
            if (weaponPrefabsProp == null || !weaponPrefabsProp.isArray) return;

            var vendorToNetworked = new Dictionary<GameObject, GameObject>();
            for (int i = 0; i < NetworkedWeaponPaths.Length; i++)
            {
                var vendor = AssetDatabase.LoadAssetAtPath<GameObject>(VendorWeaponPaths[i]);
                var networked = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkedWeaponPaths[i]);
                if (vendor != null && networked != null)
                    vendorToNetworked[vendor] = networked;
            }

            for (int i = 0; i < weaponPrefabsProp.arraySize; i++)
            {
                var elem = weaponPrefabsProp.GetArrayElementAtIndex(i);
                var current = elem.objectReferenceValue as GameObject;
                if (current != null && vendorToNetworked.TryGetValue(current, out var networked))
                {
                    elem.objectReferenceValue = networked;
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Dictionary<string, object> CaptureSerializedFields(SerializedObject so)
        {
            var map = new Dictionary<string, object>();
            var prop = so.GetIterator();
            while (prop.NextVisible(true))
            {
                CaptureProperty(prop, map);
            }
            return map;
        }

        private static void RestoreSerializedFields(SerializedObject so, Dictionary<string, object> map)
        {
            // Arrays must be sized before their element property paths can be restored.
            foreach (var entry in map)
            {
                var arrayProp = so.FindProperty(entry.Key);
                if (arrayProp != null && arrayProp.isArray && entry.Value is int arraySize)
                    arrayProp.arraySize = arraySize;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            so.Update();

            var prop = so.GetIterator();
            while (prop.NextVisible(true))
            {
                if (map.TryGetValue(prop.propertyPath, out object value))
                {
                    RestoreProperty(prop, value);
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CaptureProperty(SerializedProperty prop, Dictionary<string, object> map)
        {
            // Never copy the vendor MonoScript reference onto the networked subclass.
            if (prop.propertyPath == "m_Script") return;

            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                map[prop.propertyPath] = prop.arraySize;
                return;
            }

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
            if (prop.propertyPath == "m_Script") return;
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String) return;

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
