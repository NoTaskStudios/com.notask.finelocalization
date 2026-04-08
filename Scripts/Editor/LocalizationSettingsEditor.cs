using System.Linq;
using FineLocalization.Runtime;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.Editor
{
    [CustomEditor(typeof(LocalizationSettings))]
    public class LocalizationSettingsEditor : UnityEditor.Editor 
    {
        private LocalizationSettings settings;
        
        public override void OnInspectorGUI()
        {
            settings = (LocalizationSettings) target;

            CurrentSettingsInfo();
            DisplayHelp();
            DrawDefaultInspector();
            DisplayButtons();
            DisplayWarnings();
        }

        private void CurrentSettingsInfo()
        {
            if (settings == CurrentSettingsPointer.CurrentSettings) return;
            var alertColor = new Color(1f, 0.85f, 0.3f);
            var originalColor = GUI.backgroundColor;
            GUIStyle style = new GUIStyle();
            style.normal.textColor = alertColor;
            style.richText = true;
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space(8);
            GUI.backgroundColor = alertColor;
            EditorGUILayout.LabelField("<b>This LocalizationSettings is not the currently used LocalizationSettings", style);
            if (GUILayout.Button("Use it as current settings"))
            {
                CurrentSettingsPointer.CurrentSettings = settings;
                EditorUtility.SetDirty(CurrentSettingsPointer.SettingsPointer);
                AssetDatabase.SaveAssets();
            }
            GUI.backgroundColor = originalColor;
            EditorGUILayout.Space(8);
            EditorGUILayout.EndVertical();
            //currentOptionId = EditorGUILayout.Popup("currentlyActiveSettings", currentOptionId, settingsOptions);
        }

        private void DisplayHelp()
        {
            EditorGUILayout.HelpBox("1. Add Table Id(s) and Save Folder\n2. Press Resolve Sheets\n3. Press Download Sheets", MessageType.None);
        }

        private void DisplayButtons()
        {
            var buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 30 };

            if (GUILayout.Button("↺ Resolve Sheets", buttonStyle)) settings.ResolveGoogleSheets();
            if (GUILayout.Button("▼ Download Sheets", buttonStyle)) settings.DownloadGoogleSheets();
            if (GUILayout.Button("❖ Open Editor", buttonStyle)) LocalizationSettings.OnRunEditor();
        }

        private void DisplayWarnings()
        {
            if (settings.Sources == null || settings.Sources.Count == 0)
            {
                EditorGUILayout.HelpBox("No Table Ids configured.", MessageType.Warning);
            }
            else if (settings.SaveFolder == null)
            {
                EditorGUILayout.HelpBox("Save Folder is not set.", MessageType.Warning);
            }
            else if (settings.Sources.Any(s => s.Sheets.Count == 0))
            {
                EditorGUILayout.HelpBox("Some sources have no resolved sheets.", MessageType.Warning);
            }
            else if (settings.Sources.Any(s => s.Sheets.Any(sh => sh.TextAsset == null)))
            {
                EditorGUILayout.HelpBox("Some sheets are not downloaded.", MessageType.Warning);
            }
        }
    }
}