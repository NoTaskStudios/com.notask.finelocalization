using FineLocalization.Runtime;
using UnityEditor;

namespace FineLocalization.Editor
{
    [CustomEditor(typeof(LocalizationSettings))]
    public class LocalizationSettingsEditor : UnityEditor.Editor 
    {
        public override void OnInspectorGUI()
        {
            var settings = (LocalizationSettings) target;

            settings.DisplayMode();
            settings.DisplayHelp();
            DrawDefaultInspector();
            settings.DisplayButtons();
            settings.DisplayWarnings();
        }
    }
}