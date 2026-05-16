using System.Linq;
using FineLocalization.EditorTools;
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
            DrawSettings();
            DisplayButtons();
            DisplayWarnings();
        }

        private void DrawSettings()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LocalizationSettings.EnableLogs)));
            EditorGUILayout.HelpBox("Disable this for WebGL/release builds to suppress FineLocalization info, warning and error logs globally.", MessageType.Info);
            DrawModeField();
            DrawSourcesFields();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LocalizationSettings.SaveFolder)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LocalizationSettings.skip)));

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawModeField()
        {
            var modeProperty = serializedObject.FindProperty(nameof(LocalizationSettings.Mode));
            var mode = (LocalizationSettings.LocalizationMode)modeProperty.enumValueIndex;
            var color = mode == LocalizationSettings.LocalizationMode.Development
                ? new Color(1f, 0.85f, 0.3f)
                : new Color(0.4f, 1f, 0.5f);

            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = color;
            EditorGUILayout.PropertyField(modeProperty, new GUIContent("FineLocalization Mode"));
            GUI.backgroundColor = originalColor;

            EditorGUILayout.HelpBox(
                $"Active sources: {(mode == LocalizationSettings.LocalizationMode.Development ? "Development Sources" : "Production Sources")}",
                MessageType.Info
            );
        }

        private void DrawSourcesFields()
        {
            var productionSources = serializedObject.FindProperty(nameof(LocalizationSettings.Sources));
            var developmentSources = serializedObject.FindProperty(nameof(LocalizationSettings.DevSources));

            DrawSourceField(
                productionSources,
                "Production Sources",
                settings.Mode == LocalizationSettings.LocalizationMode.Production
            );

            DrawSourceField(
                developmentSources,
                "Development Sources",
                settings.Mode == LocalizationSettings.LocalizationMode.Development
            );
        }

        private static void DrawSourceField(SerializedProperty property, string label, bool active)
        {
            var originalColor = GUI.backgroundColor;
            if (active)
                GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);

            EditorGUILayout.PropertyField(property, new GUIContent(active ? $"{label} (Active)" : label), true);
            GUI.backgroundColor = originalColor;
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
            if (GUILayout.Button("▣ Update CSVs + Character TXTs", buttonStyle)) LocalizationEditorCsvSync.SyncCsvsAndGenerateCharactersTxt();
            if (GUILayout.Button("❖ Open Editor", buttonStyle)) LocalizationSettings.RaiseOnRunEditor();
        }

        private void DisplayWarnings()
        {
            var activeSources = settings.GetActiveSources();
            var modeName = settings.Mode.ToString();

            if (activeSources == null || activeSources.Count == 0)
            {
                EditorGUILayout.HelpBox($"No Table Ids configured for {modeName}.", MessageType.Warning);
            }
            else if (settings.SaveFolder == null)
            {
                EditorGUILayout.HelpBox("Save Folder is not set.", MessageType.Warning);
            }
            else if (activeSources.Any(s => s.Sheets.Count == 0))
            {
                EditorGUILayout.HelpBox($"Some {modeName} sources have no resolved sheets.", MessageType.Warning);
            }
            else if (activeSources.Any(s => s.Sheets.Any(sh => sh.TextAsset == null)))
            {
                EditorGUILayout.HelpBox($"Some {modeName} sheets are not downloaded.", MessageType.Warning);
            }
        }
    }
}
