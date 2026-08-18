using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FineLocalization.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FineLocalization.EditorTools
{
    public static class LocalizationEditorCsvSync
    {
        private const string UrlPattern =
            "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";

        private const string OutputFolder = "Assets/FineLocalization/Resources/Localization";

        private const string CharactersOutputFolder = "Assets/FineLocalization/Editor/GeneratedCharacters";

        private const string CharactersTxtFileName =
            "characters_all.txt";

        private const string LanguageCharactersTxtPrefix =
            "characters_";

        private const string LatinBaseCharactersTxtFileName =
            "characters_latin_base.txt";

        private const string LatinBaseCharacters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
            "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞß" +
            "àáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿ" +
            "0123456789!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~" +
            "€£¥¢₹₽₩₫₴₺¿¡…•·" +
            // Vietnamita (vi-vn): latim com diacríticos estendidos (Latin Extended-A/B + Additional U+1Exx).
            // 'vi' pula o download remoto, então a fonte base PRECISA conter estes glifos.
            "ĂăĐđĨĩŨũƠơƯư" +
            "ẠạẢảẤấẦầẨẩẪẫẬậẮắ" +
            "ẰằẲẳẴẵẶặẸẹẺẻẼẽẾế" +
            "ỀềỂểỄễỆệỈỉỊịỌọỎỏ" +
            "ỐốỒồỔổỖỗỘộỚớỜờỞở" +
            "ỠỡỢợỤụỦủỨứỪừỬửỮữ" +
            "ỰựỲỳỴỵỶỷỸỹ";

        private const int RequestTimeoutSeconds = 20;

        /*
         * Importante:
         * Nem todo caractere mostrado no jogo vem da planilha.
         * Valores como dinheiro, multiplicadores, porcentagens, IDs, números etc.
         * podem ser montados em runtime.
         *
         * Por isso adicionamos ASCII visível inteiro + alguns caracteres extras úteis.
         */
        private const string CommonRuntimeCharacters =
            "áàâãäéèêëíìîïóòôõöúùûüçñ" +
            "ÁÀÂÃÄÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇÑ" +
            "ºª°§©®™…–—•";

        private const string CurrencyRuntimeCharacters =
            "€£¥¢₩₫₴₺";

        private const string JapaneseRuntimeCharacters =
            "\u3000\u3001\u3002\u30fb\u30fc\u300c\u300d\u300e\u300f" +
            "\uff08\uff09\uff1a\uff1b\uff01\uff1f\uff03\uff05\uff0b\uff0d\uff0f\uff1d" +
            "\u3041\u3042\u3043\u3044\u3045\u3046\u3047\u3048\u3049\u304a" +
            "\u304b\u304c\u304d\u304e\u304f\u3050\u3051\u3052\u3053\u3054" +
            "\u3055\u3056\u3057\u3058\u3059\u305a\u305b\u305c\u305d\u305e" +
            "\u305f\u3060\u3061\u3062\u3063\u3064\u3065\u3066\u3067\u3068\u3069" +
            "\u306a\u306b\u306c\u306d\u306e\u306f\u3070\u3071\u3072\u3073\u3074" +
            "\u3075\u3076\u3077\u3078\u3079\u307a\u307b\u307c\u307d\u307e\u307f" +
            "\u3080\u3081\u3082\u3083\u3084\u3085\u3086\u3087\u3088\u3089\u308a" +
            "\u308b\u308c\u308d\u308e\u308f\u3090\u3091\u3092\u3093\u3094\u3095\u3096" +
            "\u30a1\u30a2\u30a3\u30a4\u30a5\u30a6\u30a7\u30a8\u30a9\u30aa" +
            "\u30ab\u30ac\u30ad\u30ae\u30af\u30b0\u30b1\u30b2\u30b3\u30b4" +
            "\u30b5\u30b6\u30b7\u30b8\u30b9\u30ba\u30bb\u30bc\u30bd\u30be" +
            "\u30bf\u30c0\u30c1\u30c2\u30c3\u30c4\u30c5\u30c6\u30c7\u30c8\u30c9" +
            "\u30ca\u30cb\u30cc\u30cd\u30ce\u30cf\u30d0\u30d1\u30d2\u30d3\u30d4" +
            "\u30d5\u30d6\u30d7\u30d8\u30d9\u30da\u30db\u30dc\u30dd\u30de\u30df" +
            "\u30e0\u30e1\u30e2\u30e3\u30e4\u30e5\u30e6\u30e7\u30e8\u30e9\u30ea" +
            "\u30eb\u30ec\u30ed\u30ee\u30ef\u30f0\u30f1\u30f2\u30f3\u30f4\u30f5\u30f6" +
            "\u4e00\u4e8c\u4e09\u56db\u4e94\u516d\u4e03\u516b\u4e5d\u5341\u767e\u5343\u4e07" +
            "\u5186\u500d\u56de\u6570\u65e5\u6708\u5e74\u6642\u5206\u79d2\u540d\u524d";

        /// <summary>
        /// Entry point do menu — abre o popup de escolha. Production é destacada
        /// como padrão para evitar que CSVs de Development fiquem locais e
        /// acabem indo numa build de release por engano.
        /// </summary>
        [MenuItem("Tools/Fine Localization/Advanced/Sheets/Sync from Google (Download + Characters)", false, 20)]
        public static void SyncCsvsAndGenerateCharactersTxt()
        {
            var settings = LocalizationSettings.Instance;
            if (settings == null)
            {
                FineLocalizationLogger.LogWarning("[FineLocalization Editor] LocalizationSettings não encontrado.");
                return;
            }

            var choice = EditorUtility.DisplayDialogComplex(
                "Sync Sheets from Google",
                "Qual conjunto de planilhas você quer baixar como CSV local?\n\n" +
                "• Production — planilhas estáveis para release (recomendado)\n" +
                "• Development — planilhas em andamento, traduções em revisão\n\n" +
                "Production é a opção mais segura: evita que CSVs de Dev fiquem locais " +
                "e acabem entrando numa build de release por engano.",
                "Production (recommended)",
                "Cancel",
                "Development"
            );

            switch (choice)
            {
                case 0: // ok
                    SyncSheetsForMode(settings, LocalizationSettings.LocalizationMode.Production);
                    return;
                case 2: // alt
                    if (!EditorUtility.DisplayDialog(
                            "Sync Development Sheets",
                            "Você está baixando as planilhas de DEVELOPMENT como CSV local.\n\n" +
                            "Lembre-se de trocar para Production antes de gerar a build de release " +
                            "(o build processor também irá avisar).\n\nContinuar?",
                            "Baixar Development",
                            "Cancel"))
                        return;
                    SyncSheetsForMode(settings, LocalizationSettings.LocalizationMode.Development);
                    return;
                default: // 1 = cancel
                    return;
            }
        }

        /// <summary>
        /// Quick API for scripted use (e.g. CI). Skips the popup.
        /// </summary>
        public static void SyncSheetsForMode(LocalizationSettings.LocalizationMode mode)
        {
            var settings = LocalizationSettings.Instance;
            if (settings == null)
            {
                FineLocalizationLogger.LogWarning("[FineLocalization Editor] LocalizationSettings não encontrado.");
                return;
            }
            SyncSheetsForMode(settings, mode);
        }

        private static void SyncSheetsForMode(LocalizationSettings settings, LocalizationSettings.LocalizationMode mode)
        {
            try
            {
                EnsureOutputFolderExists();

                var sources = mode == LocalizationSettings.LocalizationMode.Development
                    ? settings.DevSources
                    : settings.Sources;

                var modeLabel = mode.ToString();

                if (sources == null || sources.Count == 0)
                {
                    FineLocalizationLogger.LogWarning(
                        $"[FineLocalization Editor] Nenhuma source configurada em '{modeLabel}'."
                    );
                    return;
                }

                FineLocalizationLogger.Log($"[FineLocalization Editor] Sincronizando planilhas de {modeLabel}...");

                var activeSources = sources;

                var downloadedCsvs = new Dictionary<string, string>();
                var totalSheets = CountAllSheets(activeSources);
                var currentSheetIndex = 0;

                foreach (var source in activeSources)
                {
                    if (string.IsNullOrWhiteSpace(source.TableId))
                    {
                        FineLocalizationLogger.LogWarning("[FineLocalization Editor] Source ignorada: TableId vazio.");
                        continue;
                    }

                    if (source.Sheets == null || source.Sheets.Count == 0)
                    {
                        FineLocalizationLogger.LogWarning(
                            $"[FineLocalization Editor] Source {source.TableId} ignorada: nenhuma sheet."
                        );
                        continue;
                    }

                    foreach (var sheet in source.Sheets)
                    {
                        currentSheetIndex++;

                        if (sheet.Id <= 0 ||
                            string.IsNullOrWhiteSpace(sheet.Name))
                        {
                            FineLocalizationLogger.LogWarning(
                                "[FineLocalization Editor] Sheet ignorada: Id inválido ou Name vazio."
                            );
                            continue;
                        }

                        var progress = totalSheets <= 0
                            ? 0f
                            : (float)(currentSheetIndex - 1) / totalSheets;

                        EditorUtility.DisplayProgressBar(
                            "FineLocalization",
                            $"Baixando CSV: {sheet.Name}",
                            progress
                        );

                        var url = string.Format(UrlPattern, source.TableId, sheet.Id);

                        if (!TryDownloadCsv(url, sheet.Name, out var csvContent))
                            continue;

                        downloadedCsvs[sheet.Name] = csvContent;

                        SaveCsvToAssets(sheet.Name, csvContent);
                    }
                }

                if (downloadedCsvs.Count == 0)
                {
                    FineLocalizationLogger.LogWarning(
                        "[FineLocalization Editor] Nenhum CSV foi baixado. TXT de caracteres não foi gerado."
                    );
                    return;
                }

                EditorUtility.DisplayProgressBar(
                    "FineLocalization",
                    "Gerando arquivo de caracteres...",
                    0.95f
                );

                GenerateCharactersTxt(downloadedCsvs);
                GenerateLanguageCharactersTxts(downloadedCsvs);
                GenerateLatinBaseCharactersTxt();

                AssetDatabase.Refresh();

                FineLocalizationLogger.Log(
                    $"[FineLocalization Editor] Concluído! " +
                    $"{downloadedCsvs.Count} CSV(s) atualizados e " +
                    $"{CharactersTxtFileName} / {LatinBaseCharactersTxtFileName} gerados em:\n{CharactersOutputFolder}"
                );
            }
            catch (Exception e)
            {
                FineLocalizationLogger.LogError(
                    $"[FineLocalization Editor] Erro ao atualizar CSVs e gerar TXT: {e}"
                );
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/Fine Localization/Advanced/Sheets/Regenerate Characters from Saved CSVs", false, 21)]
        public static void GenerateCharactersTxtFromSavedCsvs()
        {
            EnsureOutputFolderExists();
            GenerateCharactersFromFolder(OutputFolder);
        }

        /// <summary>
        /// Gera os TXT de caracteres a partir dos CSVs de <paramref name="csvFolder"/>.
        ///
        /// Existe para o Hub poder gerar a partir do <c>SaveFolder</c> configurado, que é onde o
        /// download oficial grava. Até a v3.1 só havia o caminho fixo
        /// <c>Assets/FineLocalization/Resources/Localization</c>, então quem tinha SaveFolder em
        /// outro lugar ficava com duas cópias de cada CSV — e o gerador lia a cópia errada.
        /// </summary>
        public static void GenerateCharactersFromFolder(string csvFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(csvFolder) || !Directory.Exists(csvFolder))
                {
                    FineLocalizationLogger.LogWarning(
                        $"[FineLocalization Editor] Pasta de CSVs inexistente: {csvFolder}"
                    );
                    return;
                }

                EnsureCharactersOutputFolderExists();

                var csvFiles = Directory.GetFiles(
                    csvFolder,
                    "*.csv",
                    SearchOption.TopDirectoryOnly
                );

                if (csvFiles.Length == 0)
                {
                    FineLocalizationLogger.LogWarning(
                        $"[FineLocalization Editor] Nenhum CSV encontrado em {csvFolder}."
                    );
                    return;
                }

                var csvMap = new Dictionary<string, string>();

                foreach (var csvPath in csvFiles)
                {
                    var sheetName = Path.GetFileNameWithoutExtension(csvPath);
                    var csvContent = File.ReadAllText(csvPath, Encoding.UTF8);

                    csvMap[sheetName] = csvContent;
                }

                GenerateCharactersTxt(csvMap);
                GenerateLanguageCharactersTxts(csvMap);
                GenerateLatinBaseCharactersTxt();

                AssetDatabase.Refresh();

                FineLocalizationLogger.Log(
                    $"[FineLocalization Editor] TXT de caracteres regenerado a partir dos CSVs locais:\n" +
                    $"{Path.Combine(CharactersOutputFolder, CharactersTxtFileName)}"
                );
            }
            catch (Exception e)
            {
                FineLocalizationLogger.LogError(
                    $"[FineLocalization Editor] Erro ao gerar TXT dos CSVs locais: {e}"
                );
            }
        }

        [MenuItem("Tools/Fine Localization/Advanced/Sheets/Generate Latin Base Characters", false, 22)]
        public static void GenerateLatinBaseCharactersTxtMenu()
        {
            try
            {
                EnsureOutputFolderExists();
                GenerateLatinBaseCharactersTxt();
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                FineLocalizationLogger.LogError(
                    $"[FineLocalization Editor] Erro ao gerar Latin base TXT: {e}"
                );
            }
        }

        private static bool TryDownloadCsv(
            string url,
            string sheetName,
            out string csvContent
        )
        {
            csvContent = null;

            using var request = UnityWebRequest.Get(url);

            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;

            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                // Mantém a chamada síncrona no Editor.
                // É rápido o suficiente para esse uso como ferramenta manual.
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                FineLocalizationLogger.LogWarning(
                    $"[FineLocalization Editor] Falha ao baixar '{sheetName}'. " +
                    $"Erro: {request.error}"
                );
                return false;
            }

            csvContent = Encoding.UTF8.GetString(request.downloadHandler.data);

            if (string.IsNullOrWhiteSpace(csvContent))
            {
                FineLocalizationLogger.LogWarning(
                    $"[FineLocalization Editor] CSV vazio recebido para '{sheetName}'."
                );
                return false;
            }

            if (csvContent.Contains("signin/identifier"))
            {
                FineLocalizationLogger.LogWarning(
                    $"[FineLocalization Editor] Acesso negado à planilha de '{sheetName}'. " +
                    $"Verifique se ela está pública para exportação."
                );
                return false;
            }

            return true;
        }

        private static void SaveCsvToAssets(string sheetName, string csvContent)
        {
            var safeFileName = SanitizeFileName(sheetName);
            var filePath = Path.Combine(OutputFolder, safeFileName + ".csv");

            File.WriteAllText(filePath, csvContent, Encoding.UTF8);
        }

        private static void GenerateCharactersTxt(
            Dictionary<string, string> downloadedCsvs
        )
        {
            var seenCharacters = new HashSet<char>();
            var charactersBuilder = new StringBuilder();

            AddAsciiPrintableCharacters(seenCharacters, charactersBuilder);
            AddTextCharacters(CommonRuntimeCharacters, seenCharacters, charactersBuilder);
            AddTextCharacters(CurrencyRuntimeCharacters, seenCharacters, charactersBuilder);

            foreach (var csvPair in downloadedCsvs)
            {
                var csvContent = csvPair.Value;

                if (string.IsNullOrEmpty(csvContent))
                    continue;

                AddTextCharacters(csvContent, seenCharacters, charactersBuilder);
            }

            EnsureCharactersOutputFolderExists();
            var outputPath = Path.Combine(CharactersOutputFolder, CharactersTxtFileName);

            File.WriteAllText(
                outputPath,
                charactersBuilder.ToString(),
                Encoding.UTF8
            );

            FineLocalizationLogger.Log(
                $"[FineLocalization Editor] Arquivo de caracteres criado com " +
                $"{charactersBuilder.Length} caractere(s) únicos:\n{outputPath}"
            );
        }

        private static void GenerateLatinBaseCharactersTxt()
        {
            var seenCharacters = new HashSet<char>();
            var charactersBuilder = new StringBuilder();

            // ASCII imprimível completo: espaço (U+0020) até ~ (U+007E).
            // DEVE vir antes de LatinBaseCharacters para garantir que o espaço (U+0020)
            // esteja no atlas — sem ele todo texto com espaço quebra no TMP Static.
            AddAsciiPrintableCharacters(seenCharacters, charactersBuilder);
            AddTextCharacters(LatinBaseCharacters, seenCharacters, charactersBuilder);

            EnsureCharactersOutputFolderExists();
            var outputPath = Path.Combine(CharactersOutputFolder, LatinBaseCharactersTxtFileName);

            File.WriteAllText(
                outputPath,
                charactersBuilder.ToString(),
                Encoding.UTF8
            );

            FineLocalizationLogger.Log(
                $"[FineLocalization Editor] Arquivo Latin base criado com " +
                $"{charactersBuilder.Length} caractere(s):\n{outputPath}"
            );
        }

        private static void GenerateLanguageCharactersTxts(
            Dictionary<string, string> csvMap
        )
        {
            var settings = LocalizationSettings.Instance;
            var keyColumnIndex = settings != null ? settings.skip : 0;
            var firstLanguageColumnIndex = keyColumnIndex + 1;
            var languageCharacters = new Dictionary<string, CharacterSet>(StringComparer.OrdinalIgnoreCase);

            foreach (var csvPair in csvMap)
            {
                var csvContent = csvPair.Value;
                if (string.IsNullOrWhiteSpace(csvContent))
                    continue;

                var lines = LocalizationManager.GetLines(csvContent);
                if (lines.Count == 0)
                    continue;

                var header = LocalizationManager.GetColumns(lines[0]);
                if (header.Count <= firstLanguageColumnIndex)
                    continue;

                for (var columnIndex = firstLanguageColumnIndex; columnIndex < header.Count; columnIndex++)
                {
                    var language = header[columnIndex]?.Trim();
                    if (string.IsNullOrWhiteSpace(language))
                        continue;

                    var characterSet = GetOrCreateCharacterSet(languageCharacters, language);
                    AddLanguageRuntimeCharacters(language, characterSet.SeenCharacters, characterSet.Builder);

                    for (var lineIndex = 1; lineIndex < lines.Count; lineIndex++)
                    {
                        var columns = LocalizationManager.GetColumns(lines[lineIndex]);
                        if (columnIndex >= columns.Count)
                            continue;

                        AddTextCharacters(columns[columnIndex], characterSet.SeenCharacters, characterSet.Builder);
                    }
                }
            }

            EnsureCharactersOutputFolderExists();

            foreach (var languagePair in languageCharacters)
            {
                var safeLanguage = SanitizeFileName(languagePair.Key.Trim().ToLowerInvariant());
                var outputPath = Path.Combine(CharactersOutputFolder, $"{LanguageCharactersTxtPrefix}{safeLanguage}.txt");

                File.WriteAllText(
                    outputPath,
                    languagePair.Value.Builder.ToString(),
                    Encoding.UTF8
                );
            }

            FineLocalizationLogger.Log(
                $"[FineLocalization Editor] TXT de caracteres por idioma gerado: " +
                $"{languageCharacters.Count} idioma(s) em {CharactersOutputFolder}"
            );
        }

        private static CharacterSet GetOrCreateCharacterSet(
            Dictionary<string, CharacterSet> languageCharacters,
            string language
        )
        {
            if (languageCharacters.TryGetValue(language, out var characterSet))
                return characterSet;

            characterSet = new CharacterSet();
            languageCharacters.Add(language, characterSet);
            return characterSet;
        }

        private static void AddLanguageRuntimeCharacters(
            string language,
            HashSet<char> seenCharacters,
            StringBuilder builder
        )
        {
            if (string.IsNullOrWhiteSpace(language))
                return;

            var normalizedLanguage = language.Trim().Replace('_', '-').ToLowerInvariant();
            if (normalizedLanguage == "ja" || normalizedLanguage.StartsWith("ja-", StringComparison.OrdinalIgnoreCase))
                AddTextCharacters(JapaneseRuntimeCharacters, seenCharacters, builder);
        }

        private static void AddAsciiPrintableCharacters(
            HashSet<char> seenCharacters,
            StringBuilder builder
        )
        {
            for (var c = 32; c <= 126; c++)
            {
                AddCharacter((char)c, seenCharacters, builder);
            }
        }

        private static void AddTextCharacters(
            string text,
            HashSet<char> seenCharacters,
            StringBuilder builder
        )
        {
            foreach (var character in text)
            {
                if (char.IsControl(character))
                    continue;

                AddCharacter(character, seenCharacters, builder);
            }
        }

        private static void AddCharacter(
            char character,
            HashSet<char> seenCharacters,
            StringBuilder builder
        )
        {
            if (!seenCharacters.Add(character))
                return;

            builder.Append(character);
        }

        private static int CountAllSheets(IList<LocalizationSource> sources)
        {
            var count = 0;

            foreach (var source in sources)
            {
                if (source.Sheets == null)
                    continue;

                count += source.Sheets.Count;
            }

            return count;
        }

        private static void EnsureOutputFolderExists()
        {
            if (!Directory.Exists(OutputFolder))
                Directory.CreateDirectory(OutputFolder);

            DeleteLegacyCharactersTxtFromResources();
        }

        private static void EnsureCharactersOutputFolderExists()
        {
            if (!Directory.Exists(CharactersOutputFolder))
                Directory.CreateDirectory(CharactersOutputFolder);
        }

        private static void DeleteLegacyCharactersTxtFromResources()
        {
            if (!Directory.Exists(OutputFolder))
                return;

            DeleteFileIfExists(Path.Combine(OutputFolder, CharactersTxtFileName));
            DeleteFileIfExists(Path.Combine(OutputFolder, LatinBaseCharactersTxtFileName));

            foreach (var path in Directory.GetFiles(OutputFolder, $"{LanguageCharactersTxtPrefix}*.txt"))
            {
                DeleteFileIfExists(path);
            }
        }

        private static void DeleteFileIfExists(string path)
        {
            if (!File.Exists(path))
                return;

            File.Delete(path);

            var metaPath = path + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName.Trim();
        }

        private sealed class CharacterSet
        {
            public readonly HashSet<char> SeenCharacters = new HashSet<char>();
            public readonly StringBuilder Builder = new StringBuilder();
        }
    }
}
