using System.Collections.Generic;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public static class LanguageReader
    {
        public static string GetLanguageKey(string language)
        {
            var languages = LocalizationManager.Dictionary;
            string lang = language;
            string[] division = lang.Split('-');
            if (division.Length == 1)
                lang = CheckIfContainsLanguage(division[0], languages);
            else if (!languages.ContainsKey(lang)) 
                lang = LocalizationManager.Language;

            return lang;
        }

        private static string CheckIfContainsLanguage(string language,
            Dictionary<string, Dictionary<string, string>> dictionary)
        {
            foreach (var lang in dictionary.Keys)
            {
                string l = lang.Split('-')[0];
                if (language != l) continue;
                return lang;
            }
            Debug.LogWarning("language key not found; using default");
            return LocalizationManager.Language;
        }
    }
}
