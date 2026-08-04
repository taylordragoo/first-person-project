using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace FirstPersonProject.UI
{
    /// <summary>
    /// Stable, package-agnostic API used by the OneJS HUD and menus. The weapon lookup is
    /// reflection-based so the UI can follow the active KINEMATION shooter without adding an
    /// assembly dependency or requiring a scene reference.
    /// </summary>
    public static class ProjectSapphireBridge
    {
        private const char FieldSeparator = '\u001f';
        private const char ItemSeparator = '\u001e';
        private const string Prefix = "projectSapphire.";

        private static MonoBehaviour _cachedShooter;
        private static object _cachedWeapon;
        private static bool _paused;
        private static float _timeScaleBeforePause = 1f;

        public static bool PollPauseState()
        {
            if (EscapePressedThisFrame())
            {
                SetPaused(!_paused);
            }

            return _paused;
        }

        public static void SetPaused(bool paused)
        {
            if (_paused == paused) return;

            _paused = paused;
            if (paused)
            {
                _timeScaleBeforePause = Mathf.Approximately(Time.timeScale, 0f) ? 1f : Time.timeScale;
                Time.timeScale = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Time.timeScale = _timeScaleBeforePause;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public static void EnterMenuMode()
        {
            _paused = false;
            _timeScaleBeforePause = 1f;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public static void EnterGameplayMode()
        {
            _paused = false;
            _timeScaleBeforePause = 1f;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public static string ReadWeaponHudSnapshot()
        {
            object weapon = FindActiveWeapon();
            if (weapon == null) return EmptyWeaponSnapshot();

            try
            {
                string weaponName = Invoke<string>(weapon, "GetWeaponName", weapon.GetType().Name);
                int currentAmmo = Invoke(weapon, "GetActiveAmmo", 0);
                int magazineSize = Mathf.Max(0, Invoke(weapon, "GetMaxAmmo", 0));
                int slot = Mathf.Max(1, ReadField(_cachedShooter, "_activeWeaponIndex", 0) + 1);
                bool lowAmmo = currentAmmo > 0 && magazineSize > 0 && currentAmmo < magazineSize / 3.5f;

                return JoinFields(
                    "1",
                    Sanitize(weaponName).ToUpperInvariant(),
                    slot.ToString(CultureInfo.InvariantCulture),
                    "ballistic",
                    currentAmmo.ToString(CultureInfo.InvariantCulture),
                    magazineSize.ToString(CultureInfo.InvariantCulture),
                    magazineSize.ToString(CultureInfo.InvariantCulture),
                    "0",
                    "0",
                    lowAmmo ? "1" : "0");
            }
            catch
            {
                _cachedWeapon = null;
                return EmptyWeaponSnapshot();
            }
        }

        public static string GetResolutionOptions()
        {
            Resolution[] resolutions = Screen.resolutions;
            if (resolutions == null || resolutions.Length == 0)
            {
                return "1280 x 720" + ItemSeparator + "1600 x 900" + ItemSeparator + "1920 x 1080";
            }

            string[] labels = new string[resolutions.Length];
            for (int i = 0; i < resolutions.Length; i++)
            {
                labels[i] = resolutions[i].width + " x " + resolutions[i].height;
            }

            return string.Join(ItemSeparator.ToString(), labels);
        }

        public static int GetResolutionChoice(bool useDefaults)
        {
            if (useDefaults) return FindResolutionIndex(1920, 1080);

            int saved = PlayerPrefs.GetInt(Prefix + "resolution", -1);
            if (saved >= 0 && saved < Screen.resolutions.Length) return saved;
            return FindResolutionIndex(Screen.width, Screen.height);
        }

        public static float GetMasterVolume(bool useDefaults)
        {
            return useDefaults ? 80f : PlayerPrefs.GetFloat(Prefix + "masterVolume", AudioListener.volume * 100f);
        }

        public static bool GetFullscreen(bool useDefaults)
        {
            return useDefaults || PlayerPrefs.GetInt(Prefix + "fullscreen", Screen.fullScreen ? 1 : 0) != 0;
        }

        public static bool GetVSync(bool useDefaults)
        {
            return useDefaults || PlayerPrefs.GetInt(Prefix + "vsync", QualitySettings.vSyncCount > 0 ? 1 : 0) != 0;
        }

        public static float GetMouseSensitivity(bool useDefaults)
        {
            if (useDefaults) return 1f;
            return PlayerPrefs.GetFloat(Prefix + "mouseSensitivity", ReadSensitivityFromPlayer());
        }

        public static bool GetShowHud(bool useDefaults)
        {
            return useDefaults || PlayerPrefs.GetInt(Prefix + "showHud", 1) != 0;
        }

        public static void ApplySettings(
            float masterVolume,
            int resolutionChoice,
            bool fullscreen,
            bool vsync,
            float mouseSensitivity,
            bool showHud)
        {
            masterVolume = Mathf.Clamp(masterVolume, 0f, 100f);
            mouseSensitivity = Mathf.Max(0.001f, mouseSensitivity);

            AudioListener.volume = masterVolume / 100f;
            QualitySettings.vSyncCount = vsync ? 1 : 0;

            Resolution[] resolutions = Screen.resolutions;
            if (resolutions != null && resolutions.Length > 0)
            {
                resolutionChoice = Mathf.Clamp(resolutionChoice, 0, resolutions.Length - 1);
                Resolution resolution = resolutions[resolutionChoice];
                Screen.SetResolution(resolution.width, resolution.height, fullscreen);
            }

            ApplySensitivityToPlayers(mouseSensitivity);

            PlayerPrefs.SetFloat(Prefix + "masterVolume", masterVolume);
            PlayerPrefs.SetInt(Prefix + "resolution", resolutionChoice);
            PlayerPrefs.SetInt(Prefix + "fullscreen", fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "vsync", vsync ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "mouseSensitivity", mouseSensitivity);
            PlayerPrefs.SetInt(Prefix + "showHud", showHud ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static bool EscapePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static object FindActiveWeapon()
        {
            if (IsAlive(_cachedWeapon)) return _cachedWeapon;

            if (_cachedShooter == null)
            {
                MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

                foreach (MonoBehaviour behaviour in behaviours)
                {
                    if (behaviour == null) continue;
                    MethodInfo method = behaviour.GetType().GetMethod(
                        "GetActiveShooterWeapon",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (method == null) continue;
                    _cachedShooter = behaviour;
                    break;
                }
            }

            if (_cachedShooter == null) return null;

            try
            {
                MethodInfo method = _cachedShooter.GetType().GetMethod("GetActiveShooterWeapon", Type.EmptyTypes);
                _cachedWeapon = method?.Invoke(_cachedShooter, null);
            }
            catch
            {
                _cachedShooter = null;
                _cachedWeapon = null;
            }

            return _cachedWeapon;
        }

        private static float ReadSensitivityFromPlayer()
        {
            foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (TryReadFloatField(behaviour, "lookSensitivity", out float value) ||
                    TryReadFloatField(behaviour, "mouseSensitivity", out value))
                {
                    return value;
                }
            }

            return 1f;
        }

        private static void ApplySensitivityToPlayers(float sensitivity)
        {
            foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                TryWriteFloatField(behaviour, "lookSensitivity", sensitivity);
                TryWriteFloatField(behaviour, "mouseSensitivity", sensitivity);
                TryWriteFloatField(behaviour, "_defaultMouseSensitivity", sensitivity);
            }
        }

        private static bool TryReadFloatField(object target, string fieldName, out float value)
        {
            value = 0f;
            FieldInfo field = FindField(target?.GetType(), fieldName);
            if (field == null || field.FieldType != typeof(float)) return false;
            value = (float)field.GetValue(target);
            return true;
        }

        private static void TryWriteFloatField(object target, string fieldName, float value)
        {
            FieldInfo field = FindField(target?.GetType(), fieldName);
            if (field?.FieldType == typeof(float)) field.SetValue(target, value);
        }

        private static T ReadField<T>(object target, string fieldName, T fallback)
        {
            try
            {
                FieldInfo field = FindField(target?.GetType(), fieldName);
                return field != null ? (T)field.GetValue(target) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }

            return null;
        }

        private static T Invoke<T>(object target, string methodName, T fallback)
        {
            try
            {
                MethodInfo method = target.GetType().GetMethod(methodName, Type.EmptyTypes);
                object value = method?.Invoke(target, null);
                return value is T typed ? typed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool IsAlive(object value)
        {
            return value != null && (!(value is UnityEngine.Object unityObject) || unityObject != null);
        }

        private static int FindResolutionIndex(int width, int height)
        {
            Resolution[] resolutions = Screen.resolutions;
            for (int i = 0; i < resolutions.Length; i++)
            {
                if (resolutions[i].width == width && resolutions[i].height == height) return i;
            }

            return Mathf.Max(0, resolutions.Length - 1);
        }

        private static string EmptyWeaponSnapshot()
        {
            return JoinFields("0", "NO WEAPON", "0", "none", "0", "0", "0", "0", "0", "0");
        }

        private static string JoinFields(params string[] fields)
        {
            return string.Join(FieldSeparator.ToString(), fields);
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "UNNAMED WEAPON" : value.Replace(FieldSeparator, ' ');
        }
    }
}
