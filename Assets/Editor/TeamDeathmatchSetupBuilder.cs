using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Match;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirstPersonProject.EditorTools
{
    public static class TeamDeathmatchSetupBuilder
    {
        private const string PlayerPrefabPath =
            "Assets/FPSProject/Multiplayer/Prefabs/CAS_Player_Network.prefab";
        private const string BotPrefabPath =
            "Assets/FPSProject/Multiplayer/Prefabs/PassiveTargetBot.prefab";
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

                DestroyChild(root.transform, "Skeleton_Kinemation_Mannequin");
                DestroyChild(root.transform, "Camera");
                StripPlayerOnlyComponents(root);
                EnableTacticalCharacter(root);

                root.AddComponent<NetworkObject>();
                NetworkHealth health = root.AddComponent<NetworkHealth>();
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
                characterController.enabled = true;

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

        private static void StripPlayerOnlyComponents(GameObject root)
        {
            foreach (MonoBehaviour behaviour in
                     root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(behaviour);
            }

            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                Object.DestroyImmediate(camera);
            foreach (AudioListener listener in
                     root.GetComponentsInChildren<AudioListener>(true))
                Object.DestroyImmediate(listener);
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
                Object.DestroyImmediate(source);
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(body);
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

        private static void DestroyChild(Transform root, string childName)
        {
            Transform child = root.Find(childName);
            if (child != null) Object.DestroyImmediate(child.gameObject);
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
