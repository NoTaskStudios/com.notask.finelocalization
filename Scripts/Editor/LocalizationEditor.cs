using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FineLocalization.Runtime;
using UnityEditor;

namespace FineLocalization.Editor
{
    public class LocalizationEditor
    {
        public Dictionary<string, SortedDictionary<string, string>> SheetDictionary = new();
        public List<long> SheetIds = new();
        public List<string> SheetNames = new();

        public readonly Dictionary<string, ActionType> KeysActions = new();
        public readonly Dictionary<string, string> Keys = new();

        public string CurrentKey;
        public string PrevKey = "";

        public static LocalizationEditor Instance => _instance ??= new LocalizationEditor();
        private static LocalizationEditor _instance;

        private static LocalizationSettings Settings => LocalizationSettings.Instance;

        private static string SheetFileName(string sheetName)
        {
            return Path.Combine(AssetDatabase.GetAssetPath(Settings.SaveFolder), sheetName + ".csv");
        }

        public void LoadSetting()
        {
            SheetNames.Clear();
            SheetIds.Clear();

            foreach (var source in Settings.Sources)
            {
                foreach (var sheet in source.Sheets)
                {
                    SheetNames.Add(sheet.Name);
                    SheetIds.Add(sheet.Id);
                }
            }
        }

        public bool ReadSorted(string sheetName)
        {
            var skip = LocalizationSettings.Instance.skip;
            SheetDictionary.Clear();

            var fileName = SheetFileName(sheetName);
            if (!File.Exists(fileName))
            {
                EditorUtility.DisplayDialog("Error", $"File not found: {fileName}!\nPlease check your Settings and download sheets.", "OK");
                return false;
            }

            var lines = LocalizationManager.GetLines(File.ReadAllText(fileName));
            if (lines.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", $"CSV file is empty: {fileName}", "OK");
                return false;
            }
            
            var languages = lines[0].Split(',').Select(i => i.Trim()).ToList();
            
            for (var i = skip + 1; i < languages.Count; i++)
            {
                var lang = languages[i];
                if (!SheetDictionary.ContainsKey(lang))
                    SheetDictionary.Add(lang, new SortedDictionary<string, string>());
            }
            
            for (var i = 2; i < lines.Count; i++)
            {
                var columns = LocalizationManager.GetColumns(lines[i]);
                if (columns.Count <= skip) continue;
                
                var keyIndex = skip;
                var key = columns[keyIndex];
                if (string.IsNullOrWhiteSpace(key)) continue;
                
                for (var j = skip + 1; j < languages.Count && j < columns.Count; j++)
                {
                    var lang = languages[j];
                    var value = columns[j];

                    if (!SheetDictionary.ContainsKey(lang))
                        SheetDictionary[lang] = new SortedDictionary<string, string>();

                    SheetDictionary[lang][key] = value;
                }
            }

            return true;
        }

        public bool IsNewKey(string key)
        {
            return SheetDictionary.FirstOrDefault().Value.ContainsKey(key) &&
                   KeysActions.ContainsKey(key) &&
                   KeysActions[key] == ActionType.Add;
        }

        public string DeleteKey(string key)
        {
            if (IsNewKey(key))
            {
                KeysActions.Remove(key);
                Keys.Remove(key);
            }
            else
            {
                if (KeysActions.ContainsKey(key))
                    KeysActions[key] = ActionType.Delete;
                else
                    KeysActions.Add(key, ActionType.Delete);
            }

            foreach (var language in SheetDictionary.Keys)
            {
                SheetDictionary[language].Remove(key);
            }

            return "";
        }

        public string AddKey()
        {
            var key = $"New_key_{Guid.NewGuid().ToString().Substring(0, 8)}";
            Keys.Add(key, key);

            foreach (var language in SheetDictionary.Keys)
            {
                SheetDictionary[language].Add(key, "");
            }

            KeysActions.Add(key, ActionType.Add);
            return key;
        }

        public void ResetSheet()
        {
            CurrentKey = PrevKey != "" && Keys.ContainsKey(PrevKey) ? Keys[PrevKey] : "";
            SheetDictionary.Clear();
            Keys.Clear();
            KeysActions.Clear();
            PrevKey = "";
        }

        public void GetAllKeys()
        {
            if (Keys.Count != 0) return;

            var first = SheetDictionary.FirstOrDefault();
            if (first.Value == null) return;

            foreach (var key in first.Value.Keys)
            {
                Keys.Add(key, key);
            }
        }
    }
}
