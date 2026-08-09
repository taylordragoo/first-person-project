using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FirstPersonProject.EditorTools
{
    [InitializeOnLoad]
    internal static class ForceMainMenuPlayStart
    {
        private const string PrefKey = "FPSProject.ForceMainMenuPlayStart";
        private const string MainMenuPath = "Assets/Scenes/MainMenu.unity";

        static ForceMainMenuPlayStart()
        {
            if (EditorPrefs.GetBool(PrefKey, false))
                ApplyMainMenuStart();
            else
                EditorSceneManager.playModeStartScene = null;
        }

        private static void ApplyMainMenuStart()
        {
            SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath);
            if (asset == null)
            {
                Debug.LogWarning($"[ForceMainMenuPlayStart] MainMenu not found at {MainMenuPath}");
                return;
            }
            EditorSceneManager.playModeStartScene = asset;
        }

        [MenuItem("Tools/FPSProject/Force MainMenu Play Start")]
        private static void ToggleForceMainMenuPlayStart()
        {
            bool newVal = !EditorPrefs.GetBool(PrefKey, false);
            EditorPrefs.SetBool(PrefKey, newVal);
            if (newVal)
                ApplyMainMenuStart();
            else
                EditorSceneManager.playModeStartScene = null;
            Menu.SetChecked("Tools/FPSProject/Force MainMenu Play Start", newVal);
            Debug.Log($"[ForceMainMenuPlayStart] {(newVal ? "enabled" : "disabled")}");
        }

        [MenuItem("Tools/FPSProject/Force MainMenu Play Start", validate = true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked("Tools/FPSProject/Force MainMenu Play Start",
                EditorPrefs.GetBool(PrefKey, false));
            return true;
        }
    }
}
