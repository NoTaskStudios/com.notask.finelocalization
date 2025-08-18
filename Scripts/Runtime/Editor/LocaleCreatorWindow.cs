using System.Collections.Generic;
using Localization;
using UnityEditor;
using UnityEngine;

namespace OpaGames.Editor.Build
{
    public class LocaleCreatorWindow : EditorWindow
    {
        private string csvPath = string.Empty;

        [MenuItem("Tools/Locale Creator")]
        public static void ShowWindow()
        {
            GetWindow<LocaleCreatorWindow>("Locale Creator");
        }

        private void OnGUI()
        {
            GUILayout.Label("Load Locale From CSV", EditorStyles.boldLabel);

            if (GUILayout.Button("Load CSV"))
            {
                csvPath = EditorUtility.OpenFilePanel("Load CSV File", "", "csv");
            }

            if (!string.IsNullOrEmpty(csvPath) && GUILayout.Button("Generate Scriptable Object"))
            {
                GenerateLocale();
                GenerateLocale("Portuguese(pt)");
            }
        }

        private void GenerateLocale(string keyName = "English(en)")
        {
            var data = CSVLoader.LoadCSV(csvPath);

            var newLocale = CreateInstance<Locale>();
        
            var texts = new List<TextKeyValue>();

            foreach (var entry in data)
            {
                var textKeyValue = new TextKeyValue
                {
                    key = entry["Key"],
                    value = entry[keyName]
                };

                texts.Add(textKeyValue);
            }

            newLocale.SetTexts(texts);
            var savePath =
                EditorUtility.SaveFilePanelInProject("Save Locale of " + keyName, "New Locale", "asset", "Save locale");
            if (string.IsNullOrEmpty(savePath)) return;

            AssetDatabase.CreateAsset(newLocale, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}