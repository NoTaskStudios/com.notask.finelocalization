using System.Collections.Generic;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public static class LanguageReader
    {
        public static string GetLanguageKey(string language)
        {
            var languages = LocalizationManager.Dictionary;
            string lang = string.IsNullOrWhiteSpace(language)
                ? LocalizationManager.Language
                : language.Trim().Trim('\uFEFF').Replace('_', '-').ToLowerInvariant();

            string[] division = lang.Split('-');
            if (!languages.ContainsKey(lang) && division.Length > 0)
                lang = CheckIfContainsLanguage(division[0], languages);
            return lang;
        }

        private static string CheckIfContainsLanguage(string language,
            Dictionary<string, Dictionary<string, string>> dictionary)
        {
            foreach (var lang in dictionary.Keys)
            {
                string l = lang.Split('-')[0].Trim().ToLowerInvariant();
                if (!string.Equals(language, l, System.StringComparison.OrdinalIgnoreCase)) continue;
                return lang;
            }
            FineLocalizationLogger.LogWarning("[FineLocalization] language key not found; using default");
            return LocalizationManager.Language;
        }
    }
}
 
