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

        [MenuItem("Tools/Fine Localization/Language Settings")]
        public static void ShowLanguageWindow()
        {
            GetWindow<LocalizationSettingsWindow>("Language Settings")._currentView = ViewMode.Language;
        }

        [MenuItem("Tools/Fine Localization/CSV Settings")]
        public static void ShowCSVSettingsWindow()
        {
            EditorUtility.OpenPropertyEditor(Settings);
        }

        [MenuItem("Tools/Fine Localization/Reset")]
        public static void ResetSettings()
        {
            if (EditorUtility.DisplayDialog("Fine Localization", "Do you want to reset settings?", "Yes", "No"))
            {
                LocalizationSettings.Instance.Reset();
            }
        }

        [MenuItem("Tools/Fine Localization/Help")]
        public static void Help()
        {
            Application.OpenURL("https://github.com/NoTaskStudios/com.notask.finelocalization");
        }

        private enum ViewMode
        {
            Language,
        }

        public void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            switch (_currentView)
            {
                case ViewMode.Language:
                    DrawLanguageSettings();
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
        
    }
}
