using System;
using System.Collections.Generic;
using System.Linq;
using FineLocalization.Utils;
using UnityEngine;

#if UNITY_EDITOR
using com.notask.finelocalization.Scripts.Runtime.Utils;
using UnityEditor;
#endif

namespace FineLocalization.Runtime
{
    [CreateAssetMenu(fileName = "LocalizationSettings", menuName = "Fine Localization/Settings")]
    public class LocalizationSettings : ScriptableObject
    {
        public List<LocalizationSource> Sources = new();
        public UnityEngine.Object SaveFolder;
        public int skip = 0;
        
        public static string UrlPattern = "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";
        public static DateTime Timestamp;

        public static LocalizationSettings Instance => CurrentSettingsPointer.currentSettings;

        public static Action OnRunEditor = () => { };

        public List<LocalizationSource> GetActiveSources() => Sources; 
        public void Reset()
        {
            Sources = new List<LocalizationSource>
            {
                new LocalizationSource
                {
                    TableId = Constants.ExampleTableId,
                    Sheets = Constants.ExampleSheets.Select(i => new Sheet { Name = i.Key, Id = i.Value }).ToList()
                }
            };
#if UNITY_EDITOR
            SaveFolder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(@"Assets/FineLocalization/Resources/Localization");
#endif
        }
    }
}
