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
                var resolvedLanguage = ResolveLanguage(value);
                if (_language == resolvedLanguage) return;
                _language = resolvedLanguage;
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
            _runtimeCsvOverride = csvBySheet != null && csvBySheet.Count > 0 ? csvBySheet : null;
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

        public static void Refresh()
        {
            OnLocalizationChanged();
        }

        private static string ResolveLanguage(string language)
        {
            // 1) Exact match (case-sensitive).
            if (!string.IsNullOrWhiteSpace(language) && Dictionary.ContainsKey(language))
                return language;

            // 2) Case-insensitive match — protects against CSV header "en-US" vs runtime "en-us".
            if (!string.IsNullOrWhiteSpace(language))
            {
                foreach (var key in Dictionary.Keys)
                {
                    if (string.Equals(key, language, StringComparison.OrdinalIgnoreCase))
                    {
                        FineLocalizationLogger.LogWarning(
                            () => $"[FineLocalization] Language '{language}' matched case-insensitively to dictionary key '{key}'. Padronize a coluna do CSV para evitar isso."
                        );
                        return key;
                    }
                }
            }

            // 3) Fallback para DefaultLanguage (en-us).
            if (Dictionary.ContainsKey(DefaultLanguage))
            {
                if (!string.IsNullOrWhiteSpace(language))
                    FineLocalizationLogger.LogWarning(
                        () => $"[FineLocalization] Language '{language}' não encontrado no dicionário — caindo para DefaultLanguage '{DefaultLanguage}'."
                    );
                return DefaultLanguage;
            }

            // 4) Último recurso: primeira chave do dicionário. AVISA porque isso é provavelmente
            //    o causador de bugs do tipo "pedi en-us, mas estou vendo ja-jp".
            if (Dictionary.Count > 0)
            {
                var fallback = Dictionary.Keys.First();
                FineLocalizationLogger.LogError(
                    () => $"[FineLocalization] ⚠ Language '{language}' não encontrado e DefaultLanguage '{DefaultLanguage}' também não. " +
                          $"Caindo para PRIMEIRA chave do dicionário: '{fallback}'. Verifique se a coluna do idioma desejado existe no seu CSV."
                );
                return fallback;
            }

            return DefaultLanguage;
        }

        public static void Initialize(string language)
        {
            if (Dictionary.Count == 0)
                Read();
        
            var resolvedLanguage = LanguageReader.GetLanguageKey(
                language.Trim().ToLowerInvariant()
            );
        
            Language = resolvedLanguage;
        }

        public static void Read()
        {
            if (Dictionary.Count > 0) return;

            var keys = new HashSet<string>(); // evita duplicidade global de chave
            var settings = LocalizationSettings.Instance;
            if (settings == null)
            {
                FineLocalizationLogger.LogError("[FineLocalization] LocalizationSettings não encontrado.");
                return;
            }

            var sources = settings.GetActiveSources();
            if (sources == null || sources.Count == 0)
            {
                FineLocalizationLogger.LogError("[FineLocalization] Nenhuma source de localização configurada.");
                return;
            }

            foreach (var source in sources)
            {
                if (source?.Sheets == null || source.Sheets.Count == 0)
                {
                    FineLocalizationLogger.LogWarning("[FineLocalization] Source vazia ignorada.");
                    continue;
                }

                foreach (var sheet in source.Sheets)
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.Name))
                    {
                        FineLocalizationLogger.LogWarning("[FineLocalization] Sheet inválida ignorada.");
                        continue;
                    }

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
                    rawText ??= sheet.TextAsset != null ? sheet.TextAsset.text : null;
                    if (string.IsNullOrWhiteSpace(rawText))
                    {
                        FineLocalizationLogger.LogError(
                            () => $"[FineLocalization] Sheet `{sheet.Name}` sem CSV válido. Baixe a planilha no Editor antes do build."
                        );
                        continue;
                    }
                    
                    var lines = GetLines(rawText);
                    if (lines.Count == 0)
                    {
                        FineLocalizationLogger.LogError(() => $"[FineLocalization] Sheet `{sheet.Name}` está vazio.");
                        continue;
                    }
                    
                    var header = GetColumns(lines[0]);
                    var keyColumnIndex = settings.skip;
                    var firstLanguageColumnIndex = keyColumnIndex + 1;

                    if (keyColumnIndex < 0 || header.Count <= firstLanguageColumnIndex)
                    {
                        FineLocalizationLogger.LogError(
                            () => $"[FineLocalization] Header inválido em `{sheet.Name}`. Skip={settings.skip}, colunas={header.Count}. Esperado: <colunas ignoradas>,Key,<langs...>"
                        );
                        continue;
                    }

                    var languages = header
                        .Skip(firstLanguageColumnIndex)
                        .Where(i => !string.IsNullOrWhiteSpace(i))
                        .ToList();

                    if (languages.Count == 0)
                    {
                        FineLocalizationLogger.LogError(() => $"[FineLocalization] Nenhum idioma encontrado em `{sheet.Name}`.");
                        continue;
                    }

                    if (languages.Count != languages.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                    {
                        FineLocalizationLogger.LogError(() => $"[FineLocalization] Idiomas duplicados em `{sheet.Name}`. Sheet ignorado.");
                        continue;
                    }
                    
                    // Cria dicionários por idioma (pula colunas ignoradas e Key)
                    for (var i = firstLanguageColumnIndex; i < header.Count; i++)
                    {
                        var lang = header[i];
                        if (string.IsNullOrWhiteSpace(lang)) continue;

                        if (!Dictionary.ContainsKey(lang))
                            Dictionary.Add(lang, new Dictionary<string, string>(StringComparer.Ordinal));
                    }
                    
                    // Linhas de dados
                    for (var i = 1; i < lines.Count; i++)
                    {
                        var cols = GetColumns(lines[i]);
                        if (cols.Count <= keyColumnIndex) continue;
                        
                        var key = cols[keyColumnIndex];
                        if (string.IsNullOrWhiteSpace(key)) continue;
                    
                        // Permite a mesma key em outros sheets; se quiser global único, mantenha esse HashSet:
                        if (keys.Contains(key))
                        {
                            FineLocalizationLogger.LogWarning(() => $"[FineLocalization] key duplicada `{key}` (sheet `{sheet.Name}`). Linha ignorada.");
                            continue;
                        }
                        keys.Add(key);
                    
                        for (var j = firstLanguageColumnIndex; j < header.Count; j++)
                        {
                            var lang = header[j];
                            if (string.IsNullOrWhiteSpace(lang)) continue;

                            var value = j < cols.Count ? cols[j] : string.Empty;
                    
                            if (!Dictionary[lang].ContainsKey(key))
                                Dictionary[lang].Add(key, value);
                            else{
                                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] key duplicada `{key}` para idioma `{lang}` em `{sheet.Name}`.");
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

        // ---------------------------------------------------------------
        // DIAGNOSTICS — chame em runtime quando suspeitar de
        // "está renderizando o idioma errado". O output mostra exatamente
        // o que está no dicionário e qual idioma o LocalizationManager
        // está considerando ativo.
        // ---------------------------------------------------------------

        /// <summary>
        /// Imprime no console o estado completo do LocalizationManager:
        /// idioma ativo, lista de idiomas carregados e contagem de keys por idioma.
        /// </summary>
        public static void DumpDiagnostics()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== LocalizationManager Diagnostics ===");
            sb.AppendLine($"Active language (_language): '{_language}'");
            sb.AppendLine($"Public  Language property:   '{Language}'");
            sb.AppendLine($"DefaultLanguage:             '{DefaultLanguage}'");
            sb.AppendLine($"Dictionary loaded languages: {Dictionary.Count}");
            foreach (var kv in Dictionary)
                sb.AppendLine($"  - '{kv.Key}' ({kv.Value.Count} keys)");
            sb.AppendLine($"_runtimeCsvOverride: {( _runtimeCsvOverride == null ? "null" : _runtimeCsvOverride.Count + " sheet(s)")}");
            sb.AppendLine($"RuntimeCsvResolver:  {(RuntimeCsvResolver == null ? "null" : "set")}");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Imprime o valor armazenado para uma key em CADA idioma carregado.
        /// Útil para confirmar se a parsing colocou os textos nas colunas corretas.
        /// </summary>
        public static void DumpKey(string key)
        {
            if (Dictionary.Count == 0)
            {
                Debug.Log($"[FineLocalization] Dictionary vazio. Chame Read() ou aguarde o download.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== Key '{key}' em todos os idiomas ===");
            foreach (var kv in Dictionary)
            {
                var found = kv.Value.TryGetValue(key, out var v);
                if (!found)
                {
                    sb.AppendLine($"  [{kv.Key}] <NOT FOUND>");
                    continue;
                }

                // Mostra cada char como hex pra detectar mojibake / chars JP escondidos em valor "en-us".
                var preview = v.Length > 80 ? v.Substring(0, 80) + "…" : v;
                var hasNonAscii = ContainsNonAscii(v);
                sb.AppendLine($"  [{kv.Key}] {(hasNonAscii ? "⚠ non-ASCII " : "")}'{preview}'");
            }
            Debug.Log(sb.ToString());
        }

        private static bool ContainsNonAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] > 0x7F) return true;
            return false;
        }

        public static string Localize(string localizationKey)
        {
            if (Dictionary.Count == 0)
                Read();

            if (!Dictionary.ContainsKey(Language))
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Language not found: {Language}.");
                return localizationKey;
            }

            var exists = Dictionary[Language].TryGetValue(localizationKey, out var value);

            if (!exists || string.IsNullOrEmpty(value))
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Translation not found: {localizationKey} ({Language}).");
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
            var activeSources = settings.GetActiveSources();
            if (string.IsNullOrEmpty(sheetName))
                sheetName = activeSources.FirstOrDefault()?.Sheets?.FirstOrDefault()?.Name;

            string currentCsv = null;

            if (_runtimeCsvOverride != null)
                _runtimeCsvOverride.TryGetValue(sheetName, out currentCsv);

            if (string.IsNullOrWhiteSpace(currentCsv) && RuntimeCsvResolver != null)
                currentCsv = RuntimeCsvResolver(sheetName);

            if (string.IsNullOrWhiteSpace(currentCsv))
            {
                var sheet = activeSources
                    .SelectMany(s => s.Sheets)
                    .FirstOrDefault(s => s.Name == sheetName);

                if (sheet != null) currentCsv = sheet.TextAsset?.text;

            }

            if (string.IsNullOrWhiteSpace(currentCsv))
            {
                FineLocalizationLogger.LogError(() => $"[FineLocalization] Não foi possível carregar CSV de `{sheetName}` para persistência.");
                return;
            }

            // Reescrever linha/coluna no CSV
            var lines = GetLines(currentCsv);
            if (lines.Count == 0)
            {
                FineLocalizationLogger.LogError(() => $"[FineLocalization] CSV vazio em `{sheetName}`.");
                return;
            }

            var header = GetColumns(lines[0]); // Index, Key, lang...
            var keyColumnIndex = settings.skip;
            var firstLanguageColumnIndex = keyColumnIndex + 1;

            if (keyColumnIndex < 0 || header.Count <= firstLanguageColumnIndex)
            {
                FineLocalizationLogger.LogError(() => $"[FineLocalization] Header inválido em `{sheetName}`.");
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
                if (cols.Count > keyColumnIndex && string.Equals(cols[keyColumnIndex], key, StringComparison.Ordinal))
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
                newCols[keyColumnIndex] = key; // Key
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

        private static readonly Regex _quotedFieldRegex = new Regex("\"[\\s\\S]+?\"", RegexOptions.Compiled);
        private static readonly char[] _cjkChars = { '。', '、', '：', '！', '（', '）' };

        public static List<string> GetLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();

            text = text.Replace("\r\n", "\n").Replace("\"\"", "[_quote_]");

            // Single pass over quoted fields instead of N Replace() calls over the full text
            text = _quotedFieldRegex.Replace(text, m =>
                m.Value.Replace("\"", null)
                       .Replace(",", "[_comma_]")
                       .Replace("\n", "[_newline_]")
            );

            // CJK spacing via StringBuilder — avoids 6 successive string allocations
            if (text.IndexOfAny(_cjkChars) >= 0)
            {
                var sb = new System.Text.StringBuilder(text.Length + 16);
                foreach (char c in text)
                {
                    switch (c)
                    {
                        case '。': sb.Append("。 "); break;
                        case '、': sb.Append("、 "); break;
                        case '：': sb.Append("： "); break;
                        case '！': sb.Append("！ "); break;
                        case '（': sb.Append(" （"); break;
                        case '）': sb.Append("） "); break;
                        default:   sb.Append(c);    break;
                    }
                }
                text = sb.ToString();
            }

            text = text.Trim();
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
 
