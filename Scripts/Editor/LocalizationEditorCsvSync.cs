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
            "used_characters_all.txt";

        private const string LanguageCharactersTxtPrefix =
            "used_characters_";

        private const string LatinBaseCharactersTxtFileName =
            "characters_latin_base.txt";

        private const string LatinBaseCharacters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
            "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞß" +
            "àáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿ" +
            "0123456789!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~" +
            "€£¥¢₹₽₩₫₴₺¿¡…•·";

        private const int RequestTimeoutSeconds = 20;

        // Garante TXT/CSV em UTF-8 sem BOM. Isso evita falhas silenciosas no
        // TextMeshPro Font Asset Creator ao usar "Characters from File".
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // Blocos pequenos e seguros para UI. Não é Unicode completo: é um reforço
        // para caracteres comuns que podem aparecer em runtime, sem explodir memória no WebGL mobile.
        private const string CjkPunctuationCharacters =
            "。、，．・：；？！ー〜～（）［］「」『』【】《》〈〉…‥※〒・";

        private const string JapaneseSafetyCharacters =
            "フ" +
            "ぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞ" +
            "ただちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽ" +
            "まみむめもゃやゅゆょよらりるれろゎわゐゑをんゔ" +
            "ァアィイゥウェエォオカガキギクグケゲコゴサザシジスズセゼソゾ" +
            "タダチヂッツヅテデトドナニヌネノハバパヒビピフブプヘベペホボポ" +
            "マミムメモャヤュユョヨラリルレロヮワヰヱヲンヴヵヶ" +
            "パピプペポファフィフェフォティディトゥドゥキャキュキョシャシュショチャチュチョニャニュニョヒャヒュヒョミャミュミョリャリュリョ";

        // Caracteres japoneses/kanjis usados em mensagens montadas em runtime.
        // Não altera download nem carregamento do AssetBundle; só reforça o TXT usado para gerar o TMP_FontAsset.
        private const string JapaneseRuntimeCriticalCharacters =
            "断接続切終了開始無効有効期限利用一時後試既中別検出違反操作許可内部題発生確認地域制現在場所情報残高不足入金認証完了重複登録結果待支払処理読込達自動競手超額最大調整低下回復応例外影響";

        private const string KoreanSafetyCharacters =
            "가나다라마바사아자차카타파하거너더러머버서어저처커터퍼허고노도로모보소오조초코토포호" +
            "구누두루무부수우주추쿠투푸후기니디리미비시이지치키티피히";

        private const string ThaiSafetyCharacters =
            "กขฃคฅฆงจฉชซฌญฎฏฐฑฒณดตถทธนบปผฝพฟภมยรลวศษสหฬอฮ" +
            "ะาำิีึืุูเแโใไๅๆ็่้๊๋์ํ๎๐๑๒๓๔๕๖๗๘๙฿";

        private const string ChineseSafetyPunctuationCharacters =
            "的一是在不了有和人这中大为上个国我以要他时来用们生到作地于出就分对成会可主发年动同工也能下过子说产种面而方后多定行学法所民得经十三之进着等部度家电力里如水化高自二理起小物现实加量都两体制机当使点从业本去把性好应开它合还因由其些然前外天政四日那社义事平形相全表间样与关各重新线内数正心反你明看原又么利比或但质气第向道命此变条只没结解问意建月公无系军很情者最立代想已通并提直题党程展五果料象员革位入常文总次品式活设及管特件长求老头基资边流路级少图山统接知较将组见计别她手角期根论运农指几九区强放决西被干做必战先回则任取据处理世受领太共权收证改清美再采转更单风切打白教速花带安场身车例真务具万每目至达走积示议声报斗完类八离华名确才科张信马节话米整空元况今集温传土许步群广石记需段研界拉林律叫且究观越织装影算低持音众书布复容儿须际商非验连断深难近矿千周委素技备半办青省列习响约支般史感劳便团往酸历市克何除消构府称太准精值号率族维划选标写存候毛亲快效斯院查江型眼王按格养易置派层片始却专状育厂京识适属圆包火住调满县局照参红细引听该铁价严龙飞";

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
            "€£¥¢₹₽₩₫₴₺₿";

        /// <summary>
        /// Entry point do menu — abre o popup de escolha. Production é destacada
        /// como padrão para evitar que CSVs de Development fiquem locais e
        /// acabem indo numa build de release por engano.
        /// </summary>
        [MenuItem("Tools/Fine Localization/Sheets/Sync from Google (Download + Characters)", false, 20)]
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

        [MenuItem("Tools/Fine Localization/Sheets/Regenerate Characters from Saved CSVs", false, 21)]
        public static void GenerateCharactersTxtFromSavedCsvs()
        {
            try
            {
                EnsureOutputFolderExists();

                var csvFiles = Directory.GetFiles(
                    OutputFolder,
                    "*.csv",
                    SearchOption.TopDirectoryOnly
                );

                if (csvFiles.Length == 0)
                {
                    FineLocalizationLogger.LogWarning(
                        $"[FineLocalization Editor] Nenhum CSV encontrado em {OutputFolder}."
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

        [MenuItem("Tools/Fine Localization/Sheets/Generate Latin Base Characters", false, 22)]
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

            File.WriteAllText(filePath, csvContent, Utf8NoBom);
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
            AddGlobalSafetyCharacters(seenCharacters, charactersBuilder);

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
                Utf8NoBom
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

            AddTextCharacters(LatinBaseCharacters, seenCharacters, charactersBuilder);

            EnsureCharactersOutputFolderExists();
            var outputPath = Path.Combine(CharactersOutputFolder, LatinBaseCharactersTxtFileName);

            File.WriteAllText(
                outputPath,
                charactersBuilder.ToString(),
                Utf8NoBom
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
                    Utf8NoBom
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
            AddLanguageSafetyCharacters(language, characterSet.SeenCharacters, characterSet.Builder);
            languageCharacters.Add(language, characterSet);
            return characterSet;
        }

        private static void AddGlobalSafetyCharacters(
            HashSet<char> seenCharacters,
            StringBuilder builder
        )
        {
            AddTextCharacters(CjkPunctuationCharacters, seenCharacters, builder);
            AddTextCharacters(JapaneseSafetyCharacters, seenCharacters, builder);
            AddTextCharacters(JapaneseRuntimeCriticalCharacters, seenCharacters, builder);
            AddTextCharacters(KoreanSafetyCharacters, seenCharacters, builder);
            AddTextCharacters(ThaiSafetyCharacters, seenCharacters, builder);
        }

        private static void AddLanguageSafetyCharacters(
            string language,
            HashSet<char> seenCharacters,
            StringBuilder builder
        )
        {
            if (string.IsNullOrWhiteSpace(language))
                return;

            var normalized = language.Trim().Replace('_', '-').ToLowerInvariant();
            var root = normalized.Split('-')[0];

            if (root == "ja")
            {
                AddTextCharacters(CjkPunctuationCharacters, seenCharacters, builder);
                AddTextCharacters(JapaneseSafetyCharacters, seenCharacters, builder);
                AddTextCharacters(JapaneseRuntimeCriticalCharacters, seenCharacters, builder);
                return;
            }

            if (root == "ko")
            {
                AddTextCharacters(CjkPunctuationCharacters, seenCharacters, builder);
                AddTextCharacters(KoreanSafetyCharacters, seenCharacters, builder);
                return;
            }

            if (root == "th")
            {
                AddTextCharacters(ThaiSafetyCharacters, seenCharacters, builder);
                return;
            }

            if (root == "zh")
            {
                AddTextCharacters(CjkPunctuationCharacters, seenCharacters, builder);
                AddTextCharacters(ChineseSafetyPunctuationCharacters, seenCharacters, builder);
            }
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
