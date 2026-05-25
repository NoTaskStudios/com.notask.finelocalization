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
        // Cores reutilizadas (alocadas uma unica vez)
        private static readonly Color ProductionColor    = new(0.40f, 1.00f, 0.50f);
        private static readonly Color DevelopmentColor   = new(1.00f, 0.78f, 0.30f);
        private static readonly Color ActiveSourceColor  = new(0.55f, 0.85f, 1.00f);
        private static readonly Color AlertColor         = new(1.00f, 0.85f, 0.30f);
        private static readonly Color InactiveDimColor   = new(0.65f, 0.65f, 0.65f);

        private LocalizationSettings settings;

        // Foldout state for the inactive source list — collapsed by default to reduce clutter.
        private bool _showInactiveSources;

        public override void OnInspectorGUI()
        {
            settings = (LocalizationSettings)target;
            serializedObject.Update();

            CurrentSettingsInfo();
            DrawModeBanner();
            DrawHelp();
            DrawGeneralFields();
            DrawSourcesSection();
            DrawButtons();
            DrawWarnings();

            serializedObject.ApplyModifiedProperties();
        }

        // -------------------------------------------------------------------
        // Mode banner (big colored header + quick swap button)
        // -------------------------------------------------------------------

        private void DrawModeBanner()
        {
            var mode = settings.Mode;
            var isDev = mode == LocalizationSettings.LocalizationMode.Development;
            var color = isDev ? DevelopmentColor : ProductionColor;
            var modeLabel = isDev ? "DEVELOPMENT" : "PRODUCTION";

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = isDev ? new Color(0.55f, 0.4f, 0.05f) : new Color(0.05f, 0.4f, 0.1f) }
            };

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"MODE: {modeLabel}", headerStyle, GUILayout.Height(22));

            // Quick swap button
            var swapLabel = isDev ? "→ Switch to Production" : "→ Switch to Development";
            if (GUILayout.Button(swapLabel, GUILayout.Width(190), GUILayout.Height(22)))
            {
                Undo.RecordObject(settings, "Swap Localization Mode");
                settings.Mode = isDev
                    ? LocalizationSettings.LocalizationMode.Production
                    : LocalizationSettings.LocalizationMode.Development;
                EditorUtility.SetDirty(settings);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            GUI.backgroundColor = prevBg;
        }

        // -------------------------------------------------------------------
        // General fields
        // -------------------------------------------------------------------

        private void DrawGeneralFields()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LocalizationSettings.EnableLogs)));
            EditorGUILayout.HelpBox(
                "Disable for WebGL/release builds to suppress FineLocalization info/warning/error logs globally.",
                MessageType.Info
            );

            // Hidden the duplicated Mode dropdown — replaced by the banner button above.
            // Users can still flip it via the banner; raw enum dropdown was redundant.

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LocalizationSettings.SaveFolder)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(LocalizationSettings.skip)));
        }

        // -------------------------------------------------------------------
        // Sources section — active list prominent, inactive folded
        // -------------------------------------------------------------------

        private void DrawSourcesSection()
        {
            var production = serializedObject.FindProperty(nameof(LocalizationSettings.Sources));
            var development = serializedObject.FindProperty(nameof(LocalizationSettings.DevSources));

            var isDev = settings.Mode == LocalizationSettings.LocalizationMode.Development;
            var activeProp   = isDev ? development : production;
            var inactiveProp = isDev ? production  : development;
            var activeLabel   = isDev ? "Development Sources (Active)" : "Production Sources (Active)";
            var inactiveLabel = isDev ? "Production Sources" : "Development Sources";
            var activeSources = settings.GetActiveSources();

            EditorGUILayout.Space(4);

            // Active list — prominent
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = ActiveSourceColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int sourceCount = activeSources?.Count ?? 0;
            int sheetCount = 0;
            int downloadedCount = 0;
            if (activeSources != null)
            {
                for (int i = 0; i < activeSources.Count; i++)
                {
                    var src = activeSources[i];
                    if (src?.Sheets == null) continue;
                    sheetCount += src.Sheets.Count;
                    for (int j = 0; j < src.Sheets.Count; j++)
                        if (src.Sheets[j] != null && src.Sheets[j].TextAsset != null)
                            downloadedCount++;
                }
            }

            EditorGUILayout.LabelField(
                $"{activeLabel}    [ {sourceCount} sources / {sheetCount} sheets / {downloadedCount} downloaded ]",
                EditorStyles.boldLabel
            );
            EditorGUILayout.PropertyField(activeProp, new GUIContent("Sources"), true);

            EditorGUILayout.EndVertical();
            GUI.backgroundColor = prevBg;

            // Inactive list — dim + collapsible
            var prevColor = GUI.contentColor;
            GUI.contentColor = InactiveDimColor;
            _showInactiveSources = EditorGUILayout.Foldout(
                _showInactiveSources,
                $"{inactiveLabel} (inactive) — click to expand",
                true
            );
            if (_showInactiveSources)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(inactiveProp, new GUIContent("Sources"), true);
                EditorGUI.indentLevel--;
            }
            GUI.contentColor = prevColor;
        }

        // -------------------------------------------------------------------
        // Existing pieces — kept lightweight
        // -------------------------------------------------------------------

        private void CurrentSettingsInfo()
        {
            if (settings == CurrentSettingsPointer.CurrentSettings) return;

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = AlertColor;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "This LocalizationSettings is NOT the active one currently used at runtime.",
                EditorStyles.boldLabel
            );

            if (GUILayout.Button("Use it as current settings"))
            {
                CurrentSettingsPointer.CurrentSettings = settings;
                EditorUtility.SetDirty(CurrentSettingsPointer.SettingsPointer);
                AssetDatabase.SaveAssets();
            }
            EditorGUILayout.EndVertical();

            GUI.backgroundColor = prevBg;
            EditorGUILayout.Space(4);
        }

        private void DrawHelp()
        {
            EditorGUILayout.HelpBox(
                "1. Add Table Id(s) and Save Folder\n" +
                "2. Press Resolve Sheets\n" +
                "3. Press Download Sheets\n\n" +
                "Tip: keep Production sheets stable; use Development sheets for in-progress translations.",
                MessageType.None
            );
        }

        private void DrawButtons()
        {
            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                fixedHeight = 30
            };

            if (GUILayout.Button("↺ Resolve Sheets", buttonStyle)) settings.ResolveGoogleSheets();
            if (GUILayout.Button("▼ Download Sheets", buttonStyle)) settings.DownloadGoogleSheets();
            if (GUILayout.Button("▣ Update CSVs + Character TXTs", buttonStyle))
                LocalizationEditorCsvSync.SyncCsvsAndGenerateCharactersTxt();
            if (GUILayout.Button("❖ Open Editor", buttonStyle)) LocalizationSettings.RaiseOnRunEditor();
        }

        private void DrawWarnings()
        {
            var activeSources = settings.GetActiveSources();
            var modeName = settings.Mode.ToString();

            if (activeSources == null || activeSources.Count == 0)
                EditorGUILayout.HelpBox($"No Table Ids configured for {modeName}.", MessageType.Warning);
            else if (settings.SaveFolder == null)
                EditorGUILayout.HelpBox("Save Folder is not set.", MessageType.Warning);
            else if (activeSources.Any(s => s.Sheets.Count == 0))
                EditorGUILayout.HelpBox($"Some {modeName} sources have no resolved sheets.", MessageType.Warning);
            else if (activeSources.Any(s => s.Sheets.Any(sh => sh.TextAsset == null)))
                EditorGUILayout.HelpBox($"Some {modeName} sheets are not downloaded.", MessageType.Warning);
        }
    }
}
