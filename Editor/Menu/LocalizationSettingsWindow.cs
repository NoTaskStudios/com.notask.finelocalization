using System.Collections.Generic;
using FineLocalization.Runtime;
using FineLocalization.Utils;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FineLocalization.Editor
{
    public class LocalizationSettingsWindow : EditorWindow
    {
        private static SerializedObject _serializedObject;
        private static LocalizationSettings Settings => LocalizationSettings.Instance;

        private Vector2 _scrollPosition;
        private ViewMode _currentView;

        [MenuItem("Tools/◆ Fine Localization/Language Settings")]
        public static void ShowLanguageWindow()
        {
            GetWindow<LocalizationSettingsWindow>("Language Settings")._currentView = ViewMode.Language;
        }

        [MenuItem("Tools/◆ Fine Localization/CSV Settings")]
        public static void ShowCSVSettingsWindow()
        {
            GetWindow<LocalizationSettingsWindow>("CSV Settings")._currentView = ViewMode.CSVSettings;
        }

        [MenuItem("Tools/◆ Fine Localization/Reset")]
        public static void ResetSettings()
        {
            if (EditorUtility.DisplayDialog("Fine Localization", "Do you want to reset settings?", "Yes", "No"))
            {
                LocalizationSettings.Instance.Reset();
            }
        }

        [MenuItem("Tools/◆ Fine Localization/Help")]
        public static void Help()
        {
            Application.OpenURL("https://github.com/NoTaskStudios/com.notask.finelocalization");
        }

        private enum ViewMode
        {
            Language,
            CSVSettings
        }

        public void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_currentView)
            {
                case ViewMode.Language:
                    DrawLanguageSettings();
                    break;
                case ViewMode.CSVSettings:
                    DrawCSVSettings();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawLanguageSettings()
        {
            GUILayout.Label("Configuração de Linguagem", EditorStyles.boldLabel);

            if (LocalizationManager.Dictionary.Count == 0)
                LocalizationManager.Read();

            var langs = new List<string>(LocalizationManager.Dictionary.Keys);
            var index = langs.IndexOf(LocalizationManager.Language);
            var newIndex = EditorGUILayout.Popup("Idioma atual", index, langs.ToArray());

            if (newIndex != index)
                LocalizationManager.Language = langs[newIndex];
        }

        private void DrawCSVSettings()
        {
            minSize = new Vector2(350, 400);

            Settings.DisplayHelp();

            // SerializedObject para permitir edição do array Sources
            if (_serializedObject == null || _serializedObject.targetObject != Settings)
            {
                _serializedObject = new SerializedObject(Settings);
            }
            else
            {
                _serializedObject.Update();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Google Sheets Sources", EditorStyles.boldLabel);

            var sourcesProp = _serializedObject.FindProperty("Sources");
            EditorGUILayout.PropertyField(sourcesProp, new GUIContent("Sources"), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            Settings.SaveFolder = EditorGUILayout.ObjectField("Save Folder", Settings.SaveFolder, typeof(Object), false);

            EditorGUILayout.Space();
            Settings.DisplayButtons();
            Settings.DisplayWarnings();

            _serializedObject.ApplyModifiedProperties();
        }
    }
}
