using System.Collections.Generic;
using System.IO;
using FineLocalization.Editor;
using FineLocalization.Runtime;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.Editor.Build
{
    public class LocaleCreatorWindow : EditorWindow
    {
        private string csvPath = string.Empty;
        private string[,] csv;

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
                csv = CSVLoader.LoadCSV(new StreamReader(csvPath));
                for (int i = 1; i < csv.GetLength(1); i++)
                {
                    string language = csv[0, i];
                    if(string.IsNullOrEmpty(language)) continue;
                    GenerateLocale(i);
                }
            }
        }

        private void GenerateLocale(int keyId = 1)
        {
            string keyName = csv[0, keyId];

            var newLocale = CreateInstance<Locale>();
        
            var texts = new List<TextKeyValue>();
            int keyCount = csv.GetLength(0)-1;

            for (int i = 1; i < keyCount; i++)
            {
                string key = csv[i, 0];
                if (string.IsNullOrEmpty(key)) continue;
                string value = csv[i, keyId];
                var textKeyValue = new TextKeyValue
                {
                    key = key,
                    value = value
                };
                texts.Add(textKeyValue);
            }

            newLocale.SetTexts(texts);
            var savePath =
                EditorUtility.SaveFilePanelInProject("Save Locale of " + keyName, "New Locale", 
                    "asset", "Save locale", "Assets\\Resources\\Localization");
            if (string.IsNullOrEmpty(savePath)) return;

            AssetDatabase.CreateAsset(newLocale, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}