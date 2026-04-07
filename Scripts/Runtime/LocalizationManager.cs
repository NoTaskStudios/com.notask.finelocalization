using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public static class LocalizationManager
    {
        public static event Action OnLocalizationChanged = () => { };

        // Dicionário: idioma -> (chave -> valor)
        public static Dictionary<string, Dictionary<string, string>> Dictionary = new();

        // CSVs baixados em runtime (memória). Chave = sheet.Name
        private static Dictionary<string, string> _runtimeCsvOverride = null;

        // Hook opcional para ler CSV persistido (ex.: IndexedDB/WebGL) quando não houver override em memória
        public static Func<string, string> RuntimeCsvResolver;

        // Hook opcional para persistir CSV atualizado (edições em runtime)
        public static Action<string, string> RuntimeCsvPersistenceHook;

        public const string DefaultLanguage = "en-us";
        private static string _language = DefaultLanguage;

        public static string Language
        {
            get => _language;
            set
            {
                if (_language == value) return;
                _language = Dictionary.ContainsKey(value) ? value : DefaultLanguage;
                OnLocalizationChanged();
            }
        }

        public static void AutoLanguage()
        {
            Language = "en-us";
        }

        /// <summary>
        /// Injeta CSVs baixados em runtime (substitui TextAssets).
        /// </summary>
        public static void LoadFromCsvMap(Dictionary<string, string> csvBySheet)
        {
            _runtimeCsvOverride = csvBySheet != null && csvBySheet.Count > 0
                ? new Dictionary<string, string>(csvBySheet)
                : null;

            ReloadAll();
        }

        public static void ReloadAll()
        {
            var currentLang = _language;
            Dictionary.Clear();
            Read();
            Language = currentLang; // revalida idioma
            OnLocalizationChanged();
        }

        public static void Initialize(string language)
        {
            LanguageReader.GetLanguageKey(language.ToLower());
            if (Dictionary.Count == 0)
                Read();

            Language = language;
        }

        public static void Read()
        {
            if (Dictionary.Count > 0) return;

            var keys = new HashSet<string>(); // evita duplicidade global de chave
            var settings = LocalizationSettings.Instance;

            foreach (var source in settings.GetActiveSources())
            {
                foreach (var sheet  in source.Sheets)
                {
                    // 1) override em memória
                    string rawText = null;
                    if (_runtimeCsvOverride != null &&
                        _runtimeCsvOverride.TryGetValue(sheet.Name, out var csvFromRuntime) &&
                        !string.IsNullOrWhiteSpace(csvFromRuntime))
                    {
                        rawText = csvFromRuntime;
                    }
                    // 2) resolver externo (persistente / disco / IndexedDB)
                    else if (RuntimeCsvResolver != null)
                    {
                        var csvFromDisk = RuntimeCsvResolver(sheet.Name);
                        if (!string.IsNullOrWhiteSpace(csvFromDisk))
                            rawText = csvFromDisk;
                    }
                    // 3) fallback TextAsset
                    rawText ??= sheet.TextAsset.text;
                    
                    var lines = GetLines(rawText);
                    if (lines.Count == 0)
                    {
                        //Debug.logError($"[Fine Localization] Sheet `{sheet.Name}` está vazio.");
                        continue;
                    }
                    
                    var header = lines[0]
                        .Split(',')
                        .Select(i => i.Trim())
                        .Where(i => !string.IsNullOrWhiteSpace(i))
                        .ToList();
                    
                    if (header.Count < 3)
                    {
                        //Debug.logError($"[Fine Localization] Header inválido em `{sheet.Name}`. Esperado: Index,Key,<langs...>");
                        continue;
                    }
                    
                    if (header.Count != header.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                    {
                        //Debug.logError($"[Fine Localization] Idiomas duplicados em `{sheet.Name}`. Sheet ignorado.");
                        continue;
                    }

                    var skip = settings.skip;
                    
                    // Cria dicionários por idioma (pula Index e Key)
                    for (var i = skip; i < header.Count; i++)
                    {
                        var lang = header[i];
                        if (!Dictionary.ContainsKey(lang))
                            Dictionary.Add(lang, new Dictionary<string, string>(StringComparer.Ordinal));
                    }
                    
                    // Linhas de dados
                    for (var i = 1; i < lines.Count; i++)
                    {
                        var cols = GetColumns(lines[i]);
                        if (cols.Count < skip) continue;
                        
                        var key = cols[skip];
                        if (string.IsNullOrWhiteSpace(key)) continue;
                    
                        // Permite a mesma key em outros sheets; se quiser global único, mantenha esse HashSet:
                        if (keys.Contains(key))
                        {
                            //Debug.logWarning($"[Fine Localization] key duplicada `{key}` (sheet `{sheet.Name}`). Linha ignorada.");
                            continue;
                        }
                        keys.Add(key);
                    
                        for (var j = skip+1; j < header.Count; j++)
                        {
                            var lang = header[j];
                            var value = j < cols.Count ? cols[j] : string.Empty;
                    
                            if (!Dictionary[lang].ContainsKey(key))
                                Dictionary[lang].Add(key, value);
                            else{
                                //Debug.logWarning($"[Fine Localization] key duplicada `{key}` para idioma `{lang}` em `{sheet.Name}`.");
                            }
                        }
                    }
                }
                
            }

            // Define idioma padrão automático se nada foi setado ainda
            if (string.IsNullOrEmpty(_language))
                AutoLanguage();
            else
                Language = _language; // valida caso idioma não exista (cai para Default)
        }

        public static bool HasKey(string localizationKey)
        {
            return Dictionary.ContainsKey(Language) &&
                   Dictionary[Language].ContainsKey(localizationKey);
        }

        public static string Localize(string localizationKey)
        {
            if (Dictionary.Count == 0)
                Read();

            if (!Dictionary.ContainsKey(Language))
                throw new KeyNotFoundException("Language not found: " + Language);

            var exists = Dictionary[Language].TryGetValue(localizationKey, out var value);

            if (!exists || string.IsNullOrEmpty(value))
            {
                //Debug.logWarning($"[Fine Localization] Translation not found: {localizationKey} ({Language}).");
                return localizationKey; // <-- sempre retorna a key como fallback
            }

            return value;
        }

        public static string Localize(string localizationKey, params object[] args)
        {
            var pattern = Localize(localizationKey);
            return string.Format(pattern, args);
        }

        /// <summary>
        /// Atualiza uma tradução em memória e, opcionalmente, persiste no CSV do sheet.
        /// </summary>
        public static void SetTranslation(string language, string key, string value,
            bool persist = false, string sheetName = null)
        {
            if (!Dictionary.ContainsKey(language))
                Dictionary[language] = new Dictionary<string, string>(StringComparer.Ordinal);

            Dictionary[language][key] = value;
            OnLocalizationChanged();

            if (!persist) return;

            // Preparar CSV atual do sheet
            var settings = LocalizationSettings.Instance;
            if (string.IsNullOrEmpty(sheetName))
                sheetName = settings.Sources.FirstOrDefault()?.Sheets?.FirstOrDefault()?.Name;

            string currentCsv = null;

            if (_runtimeCsvOverride != null)
                _runtimeCsvOverride.TryGetValue(sheetName, out currentCsv);

            if (string.IsNullOrWhiteSpace(currentCsv) && RuntimeCsvResolver != null)
                currentCsv = RuntimeCsvResolver(sheetName);

            if (string.IsNullOrWhiteSpace(currentCsv))
            {
                var sheet = settings.Sources
                    .SelectMany(s => s.Sheets)
                    .FirstOrDefault(s => s.Name == sheetName);

                if (sheet != null) currentCsv = sheet.TextAsset?.text;

            }

            if (string.IsNullOrWhiteSpace(currentCsv))
            {
                //Debug.logError($"[Fine Localization] Não foi possível carregar CSV de `{sheetName}` para persistência.");
                return;
            }

            // Reescrever linha/coluna no CSV
            var lines = GetLines(currentCsv);
            if (lines.Count == 0)
            {
                //Debug.logError($"[Fine Localization] CSV vazio em `{sheetName}`.");
                return;
            }

            var header = GetColumns(lines[0]); // Index, Key, lang...
            if (header.Count < 2)
            {
                //Debug.logError($"[Fine Localization] Header inválido em `{sheetName}`.");
                return;
            }

            var langIdx = header.FindIndex(h => string.Equals(h, language, StringComparison.OrdinalIgnoreCase));
            if (langIdx < 0)
            {
                // Adiciona nova coluna de idioma
                header.Add(language);
                lines[0] = SerializeRow(header);

                for (int i = 1; i < lines.Count; i++)
                {
                    var cols = GetColumns(lines[i]);
                    while (cols.Count < header.Count) cols.Add(string.Empty);
                    lines[i] = SerializeRow(cols);
                }
                langIdx = header.Count - 1;
            }

            bool found = false;
            for (int i = 1; i < lines.Count; i++)
            {
                var cols = GetColumns(lines[i]);
                if (cols.Count > 1 && string.Equals(cols[1], key, StringComparison.Ordinal))
                {
                    while (cols.Count <= langIdx) cols.Add(string.Empty);
                    cols[langIdx] = value;
                    lines[i] = SerializeRow(cols);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var newCols = new List<string>(header.Count);
                for (int c = 0; c < header.Count; c++) newCols.Add(string.Empty);
                newCols[1] = key;          // Key
                newCols[langIdx] = value;  // Valor do idioma
                lines.Add(SerializeRow(newCols));
            }

            var newCsv = string.Join("\n", lines);

            if (_runtimeCsvOverride == null)
                _runtimeCsvOverride = new Dictionary<string, string>();

            _runtimeCsvOverride[sheetName] = newCsv;

            // Persiste para disco/IndexedDB via hook injetado pelo downloader
            RuntimeCsvPersistenceHook?.Invoke(sheetName, newCsv);

            // Recarrega tudo para refletir no cache do LocalizationManager
            ReloadAll();
        }

        // --- CSV helpers ---

        public static List<string> GetLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();

            text = text.Replace("\r\n", "\n").Replace("\"\"", "[_quote_]");
            var matches = Regex.Matches(text, "\"[\\s\\S]+?\"");

            foreach (Match match in matches)
            {
                text = text.Replace(
                    match.Value,
                    match.Value.Replace("\"", null)
                               .Replace(",", "[_comma_]")
                               .Replace("\n", "[_newline_]")
                );
            }

            // Espaços em idiomas CJK (mantendo sua lógica original)
            text = text.Replace("。", "。 ")
                       .Replace("、", "、 ")
                       .Replace("：", "： ")
                       .Replace("！", "！ ")
                       .Replace("（", " （")
                       .Replace("）", "） ")
                       .Trim();

            return text.Split('\n').Where(i => i != "").ToList();
        }

        public static List<string> GetColumns(string line)
        {
            return line.Split(',')
                       .Select(j => j.Trim())
                       .Select(j => j.Replace("[_quote_]", "\"")
                                     .Replace("[_comma_]", ",")
                                     .Replace("[_newline_]", "\n"))
                       .ToList();
        }

        private static string SerializeRow(List<string> cols)
        {
            string Escape(string s)
            {
                if (s == null) return "";
                bool needQuote = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
                if (needQuote)
                {
                    s = s.Replace("\"", "\"\"");
                    return $"\"{s}\"";
                }
                return s;
            }

            for (int i = 0; i < cols.Count; i++)
                cols[i] = Escape(cols[i]);

            return string.Join(",", cols);
        }
    }
}
 