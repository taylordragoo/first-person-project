using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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
        private static bool _gameplayCursorActive = true;
#if UNITY_EDITOR
        private static bool _gameViewFocusScheduled;
        private static int _gameViewFocusDelayCalls;
        private static bool _gameViewCursorOverrideSubscribed;
        private static double _gameViewCursorOverrideUntil;
        private static UnityEditor.EditorWindow _gameView;
        private static MethodInfo _allowCursorLockAndHide;
        private static readonly object[] AllowCursorLockArguments = { true };
        private static readonly object[] DisallowCursorLockArguments = { false };
#endif

        public static bool PollPauseState()
        {
            if (EscapePressedThisFrame())
            {
                SetPaused(!_paused);
            }

            // Cursor state can be changed by the Editor, the OS, or UI Toolkit after a
            // button callback completes. Enforce the desired gameplay state every frame
            // instead of trusting a one-shot lock request made during the Resume click.
            ApplyCursorState();

            return _paused;
        }

        public static void SetPaused(bool paused)
        {
            if (paused && !_paused)
            {
                _timeScaleBeforePause = Mathf.Approximately(Time.timeScale, 0f) ? 1f : Time.timeScale;
            }

            _paused = paused;
            Time.timeScale = paused ? 0f : _timeScaleBeforePause;
            SetGameplayInputActive(!paused);
            ApplyCursorState();
#if UNITY_EDITOR
            if (paused) StopGameViewCursorOverride(true);
#endif
        }

        public static void EnterMenuMode()
        {
            _gameplayCursorActive = false;
            _paused = false;
            _timeScaleBeforePause = 1f;
            Time.timeScale = 1f;
            SetGameplayInputActive(false);
            ApplyCursorState();
#if UNITY_EDITOR
            StopGameViewCursorOverride(true);
#endif
        }

        public static void EnterGameplayMode()
        {
            _gameplayCursorActive = true;
            _paused = false;
            _timeScaleBeforePause = 1f;
            Time.timeScale = 1f;
            SetGameplayInputActive(true);
            CaptureGameplayCursor();
        }

        public static void CaptureGameplayCursor()
        {
            _gameplayCursorActive = true;
            SetGameplayInputActive(true);
            ApplyCursorState();
#if UNITY_EDITOR
            ScheduleGameViewFocus();
#endif
        }

        private static void SetGameplayInputActive(bool active)
        {
#if ENABLE_INPUT_SYSTEM
            PlayerInput[] playerInputs = UnityEngine.Object.FindObjectsByType<PlayerInput>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (PlayerInput playerInput in playerInputs)
            {
                // Disabled PlayerInput components include presentation rigs and network
                // proxies. Their owner decides when they become local gameplay input.
                if (!playerInput.enabled) continue;

                if (active)
                {
                    playerInput.ActivateInput();
                }
                else if (playerInput.inputIsActive)
                {
                    playerInput.DeactivateInput();
                }
            }
#endif
        }

        private static void ApplyCursorState()
        {
            bool shouldLock = _gameplayCursorActive && !_paused;
            CursorLockMode desiredLockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;

            if (Cursor.lockState != desiredLockState)
            {
                Cursor.lockState = desiredLockState;
            }

            Cursor.visible = !shouldLock;
        }

#if UNITY_EDITOR
        private static void ScheduleGameViewFocus()
        {
            // Let the UI click finish and React remove the pause overlay before focusing
            // the Game View. Locking the cursor without Game View focus only changes the
            // reported lock state; Unity still waits for a click before capturing input.
            _gameViewFocusDelayCalls = 2;
            if (_gameViewFocusScheduled) return;

            _gameViewFocusScheduled = true;
            UnityEditor.EditorApplication.delayCall += AdvanceGameViewFocus;
        }

        private static void AdvanceGameViewFocus()
        {
            if (!Application.isPlaying || !_gameplayCursorActive || _paused)
            {
                _gameViewFocusScheduled = false;
                return;
            }

            if (--_gameViewFocusDelayCalls > 0)
            {
                UnityEditor.EditorApplication.delayCall += AdvanceGameViewFocus;
                return;
            }

            _gameViewFocusScheduled = false;
            Type gameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) return;

            UnityEditor.EditorWindow gameView = UnityEditor.EditorWindow.GetWindow(gameViewType);
            gameView.Focus();
            _gameView = gameView;
            _allowCursorLockAndHide = gameViewType.GetMethod(
                "AllowCursorLockAndHide",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _gameViewCursorOverrideUntil = UnityEditor.EditorApplication.timeSinceStartup + 0.5d;

            if (!_gameViewCursorOverrideSubscribed)
            {
                _gameViewCursorOverrideSubscribed = true;
                UnityEditor.EditorApplication.update += EnforceGameViewCursorCapture;
            }

            EnforceGameViewCursorCapture();
        }

        private static void EnforceGameViewCursorCapture()
        {
            if (!Application.isPlaying || !_gameplayCursorActive || _paused)
            {
                StopGameViewCursorOverride(true);
                return;
            }

            if (UnityEditor.EditorApplication.timeSinceStartup >= _gameViewCursorOverrideUntil)
            {
                StopGameViewCursorOverride(false);
                return;
            }

            if (_gameView == null || _allowCursorLockAndHide == null) return;

            if (UnityEditor.EditorWindow.focusedWindow != _gameView)
            {
                _gameView.Focus();
            }

            _allowCursorLockAndHide.Invoke(_gameView, AllowCursorLockArguments);
            ApplyCursorState();
        }

        private static void StopGameViewCursorOverride(bool revokeCursorLockPermission)
        {
            if (_gameViewCursorOverrideSubscribed)
            {
                UnityEditor.EditorApplication.update -= EnforceGameViewCursorCapture;
                _gameViewCursorOverrideSubscribed = false;
            }

            if (revokeCursorLockPermission && _gameView != null && _allowCursorLockAndHide != null)
            {
                _allowCursorLockAndHide.Invoke(_gameView, DisallowCursorLockArguments);
            }

            _gameViewCursorOverrideUntil = 0d;
        }
#endif

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
