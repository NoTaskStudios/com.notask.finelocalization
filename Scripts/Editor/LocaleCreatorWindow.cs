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

        [MenuItem("Tools/Fine Localization/Import Local CSV → Locale Assets", false, 40)]
        public static void ShowWindow()
        {
            GetWindow<LocaleCreatorWindow>("Local CSV Importer");
        }

        private void OnGUI()
        {
            GUILayout.Label("Generate Locale assets from a local .csv file", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Selecione um arquivo .csv local. Para cada coluna de idioma, será gerado um Locale ScriptableObject.\n" +
                "Use este atalho apenas quando precisar importar uma planilha que não vem do Google Sheets.",
                MessageType.Info
            );

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
