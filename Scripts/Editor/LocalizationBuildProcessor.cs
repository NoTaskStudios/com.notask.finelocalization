#if UNITY_EDITOR
using System.Linq;
using FineLocalization.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FineLocalization.Editor
{
    public class LocalizationBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = LocalizationSettings.Instance;
            if (settings == null)
                throw new BuildFailedException("[FineLocalization] LocalizationSettings nao encontrado.");

            if (!EditorUtility.DisplayDialog(
                    "Localization Settings",
                    $"Current LocalizationSettings is \"{settings.name}\".\nMode: {settings.Mode}\n\nAre you sure you want to build with this localization setting?",
                    "Yes",
                    "No (Cancel build)"))
                throw new BuildFailedException("Build cancelled (opted for changing localization)");

            var activeSources = settings.GetActiveSources();

            if (activeSources == null || activeSources.Count == 0)
                throw new BuildFailedException($"[FineLocalization] Planilha '{settings.name}' esta vazia! Configure antes de buildar.");

            ValidateSettingsForBuild(settings, report);

            FineLocalizationLogger.Log(() => $"[FineLocalization] Build usando settings: {settings.name}");
        }

        private static void ValidateSettingsForBuild(LocalizationSettings settings, BuildReport report)
        {
            var requiresBundledCsvs = report.summary.platform == BuildTarget.WebGL;

            foreach (var source in settings.GetActiveSources())
            {
                if (source?.Sheets == null || source.Sheets.Count == 0)
                    throw new BuildFailedException($"[FineLocalization] Source vazia em '{settings.name}'.");

                foreach (var sheet in source.Sheets)
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.Name))
                        throw new BuildFailedException($"[FineLocalization] Sheet invalida em '{settings.name}'.");

                    if (requiresBundledCsvs && sheet.TextAsset == null)
                    {
                        throw new BuildFailedException(
                            $"[FineLocalization] Sheet '{sheet.Name}' nao tem CSV baixado em '{settings.name}'. " +
                            "Clique em Download Sheets antes de gerar WebGL."
                        );
                    }

                    if (sheet.TextAsset == null)
                        continue;

                    ValidateSheetCsv(settings, sheet.Name, sheet.TextAsset.text);
                }
            }
        }

        private static void ValidateSheetCsv(LocalizationSettings settings, string sheetName, string csvText)
        {
            var lines = LocalizationManager.GetLines(csvText);
            if (lines.Count == 0)
                throw new BuildFailedException($"[FineLocalization] CSV vazio na sheet '{sheetName}'.");

            var header = LocalizationManager.GetColumns(lines[0]);
            var keyColumnIndex = settings.skip;
            var firstLanguageColumnIndex = keyColumnIndex + 1;

            if (keyColumnIndex < 0 || header.Count <= firstLanguageColumnIndex)
            {
                throw new BuildFailedException(
                    $"[FineLocalization] Header invalido na sheet '{sheetName}'. " +
                    $"Skip={settings.skip}, colunas={header.Count}. Esperado: <colunas ignoradas>,Key,<langs...>."
                );
            }

            var languages = header
                .Skip(firstLanguageColumnIndex)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();

            if (languages.Count == 0)
                throw new BuildFailedException($"[FineLocalization] Nenhum idioma encontrado na sheet '{sheetName}'.");

            if (languages.Count != languages.Distinct(System.StringComparer.OrdinalIgnoreCase).Count())
                throw new BuildFailedException($"[FineLocalization] Idiomas duplicados na sheet '{sheetName}'.");
        }
    }
}
#endif
