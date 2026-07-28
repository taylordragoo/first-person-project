using CAS_Demo.Scripts.FPS;
using KINEMATION.Shared.KAnimationCore.Editor.Widgets;
using UnityEditor;

namespace CAS_Demo.Scripts.Editor.FPS
{
    [CustomEditor(typeof(WeaponSettings))]
    public class WeaponSettingsEditor : UnityEditor.Editor
    {
        private TabInspectorWidget _tabInspectorWidget;

        private void OnEnable()
        {
            _tabInspectorWidget = new TabInspectorWidget(serializedObject);
            _tabInspectorWidget.Init();
        }

        public override void OnInspectorGUI()
        {
            _tabInspectorWidget.OnGUI();
        }
    }
}