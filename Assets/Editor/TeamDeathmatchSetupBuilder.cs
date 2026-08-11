using System.Collections.Generic;
using FirstPersonProject.Integrations.Kinemation.Multiplayer;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Match;
using KINEMATION.KShooterCore.Runtime.Camera;
using KINEMATION.TacticalShooterPack.Scripts.Animation;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FirstPersonProject.EditorTools
{
    public static class TeamDeathmatchSetupBuilder
    {
        private const string PlayerPrefabPath =
            "Assets/FPSProject/Multiplayer/Prefabs/CAS_Player_Network.prefab";
        private const string BotPrefabPath =
            "Assets/FPSProject/Multiplayer/Prefabs/PassiveTargetBot.prefab";
        private const string BotWeaponPrefabPath =
            "Assets/KINEMATION/TacticalShooterPack/Prefabs/Weapons/W_AK105.prefab";
        private const string BotNavMeshAgentTypeName = "Bot";
        private const string MultiplayerScenePath =
            "Assets/Scenes/OperationsDemoMultiplayer.unity";

        [MenuItem("Tools/FPSProject/Build Team Deathmatch Infrastructure")]
        public static void Build()
        {
            GameObject botPrefab = BuildPassiveBotPrefab();
            if (botPrefab == null) return;

            if (!AddTeamMemberToPlayerPrefab()) return;
            if (!WireActiveMultiplayerScene(botPrefab)) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TeamDeathmatchSetupBuilder] Built match manager, passive bot prefab, "
                + "and player team membership.");
        }

        [MenuItem("Tools/FPSProject/Rebuild Passive Bot Character")]
        public static void RebuildPassiveBotCharacter()
        {
            GameObject botPrefab = BuildPassiveBotPrefab();
            if (botPrefab == null) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TeamDeathmatchSetupBuilder] Rebuilt the passive bot from the "
                + "CAS tactical character presentation.", botPrefab);
        }

        private static GameObject BuildPassiveBotPrefab()
        {
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPrefabPath);
            if (sourcePrefab == null)
            {
                Debug.LogError($"Multiplayer player prefab not found at {PlayerPrefabPath}.");
                return null;
            }

            GameObject root = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (root == null)
            {
                Debug.LogError("Could not instantiate the CAS multiplayer player prefab.");
                return null;
            }

            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                root.name = "PassiveTargetBot";
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;

                StripPlayerGameplayComponents(root);
                ConfigureCasSourceRig(root);
                EnableTacticalCharacter(root);
                DisableOwnerOnlyComponents(root);
                RemoveChildColliders(root);

                NetworkObject networkObject = GetOrAdd<NetworkObject>(root);
                NetworkHealth health = GetOrAdd<NetworkHealth>(root);
                PassiveTargetBot bot = root.AddComponent<PassiveTargetBot>();
                ConfigureBotReferences(bot, health, root);

                CharacterController characterController =
                    root.GetComponent<CharacterController>();
                if (characterController == null)
                {
                    characterController = root.AddComponent<CharacterController>();
                    characterController.center = new Vector3(0f, 0.95f, 0f);
                    characterController.height = 1.9f;
                    characterController.radius = 0.35f;
                }
                characterController.enabled = false;

                CapsuleCollider hitCollider = GetOrAdd<CapsuleCollider>(root);
                hitCollider.center = characterController.center;
                hitCollider.height = characterController.height;
                hitCollider.radius = characterController.radius;
                hitCollider.enabled = true;

                NavMeshAgent navAgent = GetOrAdd<NavMeshAgent>(root);
                navAgent.agentTypeID = ResolveNavMeshAgentTypeId(
                    BotNavMeshAgentTypeName);
                navAgent.radius = 0.35f;
                navAgent.height = 1.9f;
                navAgent.baseOffset = 0f;
                navAgent.speed = 2.4f;
                navAgent.acceleration = 7f;
                navAgent.angularSpeed = 300f;
                navAgent.stoppingDistance = 0.5f;
                navAgent.autoBraking = true;

                GetOrAdd<NetworkTransform>(root);
                GetOrAdd<PassiveTargetBotNavigator>(root);
                BotCasPresentationAdapter presentation =
                    GetOrAdd<BotCasPresentationAdapter>(root);
                ConfigurePresentationAdapter(presentation, root);

                ValidateBotPrefabRoot(root, networkObject);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BotPrefabPath,
                    out bool saved);
                if (!saved || prefab == null)
                {
                    Debug.LogError($"Could not create passive bot prefab at {BotPrefabPath}.");
                    return null;
                }

                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void StripPlayerGameplayComponents(GameObject root)
        {
            var removedTypes = new HashSet<string>
            {
                typeof(NetworkCasPlayer).FullName,
                typeof(NetworkWeaponState).FullName,
                typeof(NetworkWeaponShotRouter).FullName,
                typeof(NetworkWeaponPropBridge).FullName,
                typeof(NetworkPlayerLifecycle).FullName,
                typeof(NetworkTeamMember).FullName,
                "FPSProject.Combat.Runtime.WeaponCombatRuntime"
            };

            foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && removedTypes.Contains(behaviour.GetType().FullName))
                    Object.DestroyImmediate(behaviour);
            }
        }

        private static void ConfigureCasSourceRig(GameObject root)
        {
            Transform casSource = root.transform.Find("Skeleton_Kinemation_Mannequin");
            if (casSource == null)
            {
                Debug.LogError("CAS player prefab is missing its hidden animation source rig.", root);
                return;
            }

            casSource.gameObject.SetActive(true);
            foreach (Renderer renderer in casSource.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Animator animator in casSource.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private static void DisableOwnerOnlyComponents(GameObject root)
        {
            foreach (PlayerInput input in root.GetComponentsInChildren<PlayerInput>(true))
            {
                input.DeactivateInput();
                input.enabled = false;
            }

            foreach (Camera cameraComponent in root.GetComponentsInChildren<Camera>(true))
                cameraComponent.enabled = false;
            foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
        }

        private static void RemoveChildColliders(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.transform != root.transform)
                    Object.DestroyImmediate(collider);
            }
        }

        private static void EnableTacticalCharacter(GameObject root)
        {
            Transform presentation = root.transform.Find("Tactical Presentation");
            if (presentation == null)
            {
                Debug.LogError("CAS player prefab is missing Tactical Presentation.", root);
                return;
            }

            presentation.gameObject.SetActive(true);
            foreach (SkinnedMeshRenderer renderer in
                     presentation.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;
                if (renderer.name == "SK_Head_01a.001"
                    || renderer.name == "SK_Helmet_01a.001"
                    || renderer.name == "Headset_Helmet_Fixed01.001")
                {
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }

            foreach (Animator animator in
                     presentation.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = true;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private static void ConfigureBotReferences(PassiveTargetBot bot,
            NetworkHealth health, GameObject root)
        {
            var tintRenderers = new List<Renderer>();
            AddRendererByName(root, tintRenderers, "SK_Helmet_01a.001");
            AddRendererByName(root, tintRenderers, "SK_Vest_01a.001");

            var serializedBot = new SerializedObject(bot);
            serializedBot.FindProperty("networkHealth").objectReferenceValue = health;
            SerializedProperty rendererProperty = serializedBot.FindProperty("renderers");
            rendererProperty.arraySize = tintRenderers.Count;
            for (int i = 0; i < tintRenderers.Count; i++)
                rendererProperty.GetArrayElementAtIndex(i).objectReferenceValue =
                    tintRenderers[i];
            serializedBot.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePresentationAdapter(BotCasPresentationAdapter adapter,
            GameObject root)
        {
            var serialized = new SerializedObject(adapter);
            serialized.FindProperty("controller").objectReferenceValue =
                root.GetComponent<NetworkFPSExampleController>();
            serialized.FindProperty("tacticalAnimation").objectReferenceValue =
                root.GetComponentInChildren<TacticalProceduralAnimation>(true);
            serialized.FindProperty("tacticalPlayer").objectReferenceValue =
                root.GetComponentInChildren<NetworkTacticalShooterPlayer>(true);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            TacticalProceduralAnimation tacticalAnimation =
                root.GetComponentInChildren<TacticalProceduralAnimation>(true);
            NetworkTacticalShooterPlayer tacticalPlayer =
                root.GetComponentInChildren<NetworkTacticalShooterPlayer>(true);
            if (tacticalAnimation == null || tacticalPlayer == null)
                throw new System.InvalidOperationException(
                    "Passive bot source is missing Tactical presentation components.");

            GameObject ak105 = AssetDatabase.LoadAssetAtPath<GameObject>(BotWeaponPrefabPath);
            if (ak105 == null)
                throw new System.InvalidOperationException(
                    $"Bot presentation weapon missing at {BotWeaponPrefabPath}.");

            var serializedTacticalPlayer = new SerializedObject(tacticalPlayer);
            SerializedProperty weaponPrefabs =
                serializedTacticalPlayer.FindProperty("weaponPrefabs");
            weaponPrefabs.arraySize = 1;
            weaponPrefabs.GetArrayElementAtIndex(0).objectReferenceValue = ak105;
            FPSCameraAnimator localFpsCamera =
                root.GetComponentInChildren<FPSCameraAnimator>(true);
            if (localFpsCamera == null || (localFpsCamera.transform != root.transform
                    && !localFpsCamera.transform.IsChildOf(root.transform)))
            {
                throw new System.InvalidOperationException(
                    "Passive bot is missing its local Tactical FPS camera animator.");
            }

            serializedTacticalPlayer.FindProperty("fpsCamera").objectReferenceValue =
                localFpsCamera;
            serializedTacticalPlayer.ApplyModifiedPropertiesWithoutUndo();

            if (tacticalAnimation != null) tacticalAnimation.enabled = false;
            if (tacticalPlayer != null) tacticalPlayer.enabled = false;
        }

        private static T GetOrAdd<T>(GameObject root) where T : Component
        {
            T component = root.GetComponent<T>();
            return component != null ? component : root.AddComponent<T>();
        }

        private static int ResolveNavMeshAgentTypeId(string agentTypeName)
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(i);
                if (string.Equals(NavMesh.GetSettingsNameFromID(settings.agentTypeID),
                        agentTypeName, System.StringComparison.Ordinal))
                {
                    return settings.agentTypeID;
                }
            }

            throw new System.InvalidOperationException(
                $"Required NavMesh agent type '{agentTypeName}' is not configured.");
        }

        private static void ValidateBotPrefabRoot(GameObject root, NetworkObject networkObject)
        {
            NetworkObject[] networkObjects = root.GetComponentsInChildren<NetworkObject>(true);
            if (networkObjects.Length != 1 || networkObjects[0] != networkObject)
                throw new System.InvalidOperationException(
                    "Passive bot must contain exactly one root NetworkObject.");

            if (root.GetComponent<NetworkFPSExampleController>() == null
                || root.GetComponent<FirstPersonProject.Integrations.Kinemation.CasTacticalPlayerBridge>() == null
                || root.transform.Find("Skeleton_Kinemation_Mannequin") == null)
            {
                throw new System.InvalidOperationException(
                    "Passive bot lost required CAS/Tactical presentation components.");
            }

            ValidateLocalObjectReferences(root);
        }

        private static void ValidateLocalObjectReferences(GameObject root)
        {
            foreach (Component owner in root.GetComponentsInChildren<Component>(true))
            {
                if (owner == null) continue;
                var serialized = new SerializedObject(owner);
                SerializedProperty property = serialized.GetIterator();
                if (!property.NextVisible(true)) continue;

                do
                {
                    if (property.propertyType
                            != SerializedPropertyType.ObjectReference
                        || property.objectReferenceValue == null)
                    {
                        continue;
                    }

                    Object reference = property.objectReferenceValue;
                    Component referencedComponent = reference as Component;
                    GameObject referencedObject = reference as GameObject;
                    if (referencedComponent == null && referencedObject == null) continue;

                    // Persistent GameObject references are prefab assets (weapons/items) and are
                    // valid. Component references must always resolve inside this unpacked bot.
                    if (referencedComponent == null && EditorUtility.IsPersistent(reference))
                        continue;

                    Transform referencedTransform = referencedComponent != null
                        ? referencedComponent.transform
                        : referencedObject.transform;
                    bool isLocal = referencedTransform == root.transform
                        || referencedTransform.IsChildOf(root.transform);
                    if (!EditorUtility.IsPersistent(reference) && isLocal) continue;

                    throw new System.InvalidOperationException(
                        $"{owner.GetType().Name}.{property.propertyPath} references external "
                        + $"object {reference.name}.");
                }
                while (property.NextVisible(true));
            }
        }

        private static void AddRendererByName(GameObject root,
            ICollection<Renderer> renderers, string objectName)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != objectName) continue;
                renderers.Add(renderer);
                return;
            }
        }

        private static bool AddTeamMemberToPlayerPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
            {
                Debug.LogError($"Multiplayer player prefab not found at {PlayerPrefabPath}.");
                return false;
            }

            try
            {
                if (root.GetComponent<NetworkTeamMember>() == null)
                    root.AddComponent<NetworkTeamMember>();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath,
                    out bool savedSuccessfully);
                if (!savedSuccessfully)
                {
                    Debug.LogError($"Could not update player prefab at {PlayerPrefabPath}.");
                    return false;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return true;
        }

        private static bool WireActiveMultiplayerScene(GameObject botPrefab)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MultiplayerScenePath)
            {
                Debug.LogError($"Open {MultiplayerScenePath} before running the builder. "
                    + $"The active scene is {scene.path}.");
                return false;
            }

            GameObject managerObject = GameObject.Find("Team Deathmatch");
            if (managerObject == null)
                managerObject = new GameObject("Team Deathmatch");

            if (managerObject.GetComponent<NetworkObject>() == null)
                managerObject.AddComponent<NetworkObject>();

            TeamDeathmatchManager matchManager =
                managerObject.GetComponent<TeamDeathmatchManager>();
            if (matchManager == null)
                matchManager = managerObject.AddComponent<TeamDeathmatchManager>();

            var serializedManager = new SerializedObject(matchManager);
            serializedManager.FindProperty("passiveBotPrefab").objectReferenceValue = botPrefab;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(managerObject);
            EditorSceneManager.MarkSceneDirty(scene);
            return EditorSceneManager.SaveScene(scene);
        }
    }
}
