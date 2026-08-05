using UnityEngine;
using UnityEditor;
using FPSProject.Combat.Runtime;
using CAS_Demo.Scripts.FPS;

namespace FPSProject.Combat.Editor
{
    /// <summary>
    /// Editor validation for WeaponSettings ballistics configuration.
    /// Reports configuration problems without breaking existing CAS firing presentation.
    /// </summary>
    [CustomEditor(typeof(WeaponSettings))]
    public class WeaponSettingsBallisticsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var settings = (WeaponSettings)target;
            var ballistics = settings.ballistics;

            if (!ballistics.combatEnabled)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Combat Validation", EditorStyles.boldLabel);

            if (ballistics.damage <= 0f)
            {
                EditorGUILayout.HelpBox("Damage is zero or negative. No damage will be dealt.", MessageType.Warning);
            }

            if (ballistics.maxRange <= 0f)
            {
                EditorGUILayout.HelpBox("Max range is zero or negative. Shots will not travel.", MessageType.Warning);
            }

            if (ballistics.hitMask == 0)
            {
                EditorGUILayout.HelpBox("Hit mask is set to Nothing. Shots will not hit anything.", MessageType.Warning);
            }

            if (ballistics.tracerPrefab == null)
            {
                EditorGUILayout.HelpBox("Tracer prefab is missing. No tracers will be spawned.", MessageType.Warning);
            }

            if (ballistics.impactEffectLibrary == null)
            {
                EditorGUILayout.HelpBox("Impact Effect Library is missing. No impacts or decals will be spawned.", MessageType.Warning);
            }

            if (ballistics.shotType == WeaponShotType.Projectile && ballistics.projectilePrefab == null)
            {
                EditorGUILayout.HelpBox("Shot type is Projectile but no projectile prefab is assigned.", MessageType.Warning);
            }

            if (ballistics.tracerSpeed <= 0f)
            {
                EditorGUILayout.HelpBox("Tracer speed is zero or negative. Tracers will not move.", MessageType.Warning);
            }

            if (ballistics.projectileSpeed <= 0f && ballistics.shotType == WeaponShotType.Projectile)
            {
                EditorGUILayout.HelpBox("Projectile speed is zero or negative. Projectiles will not move.", MessageType.Warning);
            }
        }
    }
}
