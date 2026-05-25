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

        private const string CharactersTxtFileName =
            "used_characters_all.txt";

        private const string LatinBaseCharactersTxtFileName =
            "characters_latin_base.txt";

        private const string LatinBaseCharacters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
            "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞß" +
            "àáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿ" +
            "0123456789!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~" +
            "€£¥¢₹₽₩₫₴₺¿¡…•·";

        private const int RequestTimeoutSeconds = 20;

        /*
         * Importante:
         * Nem todo caractere mostrado no jogo vem da planilha.
         * Valores como dinheiro, multiplicadores, porcentagens, IDs, números etc.
         * podem ser montados em runtime.
         *
         * Por isso adicionamos ASCII visível inteiro + alguns caracteres extras úteis.
         */
        private const string ExtraRuntimeCharacters =
            "áàâãäéèêëíìîïóòôõöúùûüçñ" +
            "ÁÀÂÃÄÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇÑ" +
            "ºª°§©®™…–—•" +
            "€£¥₩₹₽";

        [MenuItem("Tools/Fine Localization/Sheets/Sync from Google (Download + Characters)", false, 20)]
        public static void SyncCsvsAndGenerateCharactersTxt()
        {
            try
            {
                EnsureOutputFolderExists();

                var activeSources = LocalizationSettings.Instance.GetActiveSources();

                if (activeSources == null || activeSources.Count == 0)
                {
                    FineLocalizationLogger.LogWarning("[FineLocalization Editor] Nenhuma source ativa encontrada.");
                    return;
                }

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
                GenerateLatinBaseCharactersTxt();

                AssetDatabase.Refresh();

                FineLocalizationLogger.Log(
                    $"[FineLocalization Editor] Concluído! " +
                    $"{downloadedCsvs.Count} CSV(s) atualizados e " +
                    $"{CharactersTxtFileName} / {LatinBaseCharactersTxtFileName} gerados em:\n{OutputFolder}"
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
                GenerateLatinBaseCharactersTxt();

                AssetDatabase.Refresh();

                FineLocalizationLogger.Log(
                    $"[FineLocalization Editor] TXT de caracteres regenerado a partir dos CSVs locais:\n" +
                    $"{Path.Combine(OutputFolder, CharactersTxtFileName)}"
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

            File.WriteAllText(filePath, csvContent, Encoding.UTF8);
        }

        private static void GenerateCharactersTxt(
            Dictionary<string, string> downloadedCsvs
        )
        {
            var seenCharacters = new HashSet<char>();
            var charactersBuilder = new StringBuilder();

            AddAsciiPrintableCharacters(seenCharacters, charactersBuilder);
            AddTextCharacters(ExtraRuntimeCharacters, seenCharacters, charactersBuilder);

            foreach (var csvPair in downloadedCsvs)
            {
                var csvContent = csvPair.Value;

                if (string.IsNullOrEmpty(csvContent))
                    continue;

                AddTextCharacters(csvContent, seenCharacters, charactersBuilder);
            }

            var outputPath = Path.Combine(OutputFolder, CharactersTxtFileName);

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

            AddTextCharacters(LatinBaseCharacters, seenCharacters, charactersBuilder);

            var outputPath = Path.Combine(OutputFolder, LatinBaseCharactersTxtFileName);

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
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            return fileName.Trim();
        }
    }
}
