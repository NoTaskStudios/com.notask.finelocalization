using System;
using FineLocalization.Runtime;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.Editor
{
    [CustomEditor(typeof(CurrentSettingsPointer))]
    public class LocalizationPointerEditor : UnityEditor.Editor
    {
        private static LocalizationSettings[] foundSettings;
        private static string[] settingsOptions = Array.Empty<string>();
        private static int currentOptionId = 0;
        
        public override void OnInspectorGUI()
        {
            var pointer = target as CurrentSettingsPointer;
            if (!pointer) return;
            SetupSettingsData();
            
            var chosenOption = EditorGUILayout.Popup("Change Used Localization", currentOptionId, settingsOptions);
            if (chosenOption != currentOptionId)
            {
                pointer.settings = foundSettings[chosenOption];
                EditorUtility.SetDirty(pointer);
                AssetDatabase.SaveAssets();
            }
            currentOptionId = chosenOption;
            
            DrawDefaultInspector();
        }
        
        private void SetupSettingsData()
        {
            if (foundSettings == null)
            {
                currentOptionId = -1;
                foundSettings = Resources.LoadAll<LocalizationSettings>("");
            }
            settingsOptions = new string[foundSettings.Length];
            for (int i = 0; i < settingsOptions.Length; i++)
            {
                settingsOptions[i] = foundSettings[i].name;
                if (currentOptionId > 0 || foundSettings[i] != CurrentSettingsPointer.currentSettings) continue;
                currentOptionId = i;
            }

            if (currentOptionId >= 0 && currentOptionId < foundSettings.Length) return;
            currentOptionId = Mathf.Clamp(currentOptionId, 0, foundSettings.Length - 1);
        }
    }
}
