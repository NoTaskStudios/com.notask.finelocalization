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

            ConfirmModeForBuild(settings, report);

            var activeSources = settings.GetActiveSources();
            if (activeSources == null || activeSources.Count == 0)
            {
                throw new BuildFailedException(
                    $"[FineLocalization] '{settings.name}' nao possui sources configuradas para o modo {settings.Mode}. Configure antes de buildar."
                );
            }

            ValidateSettingsForBuild(settings, report);

            FineLocalizationLogger.Log(
                () => $"[FineLocalization] Build usando settings: '{settings.name}' | Mode: {settings.Mode}"
            );
        }

        /// <summary>
        /// Quando as planilhas estao em Development, oferece trocar para Production antes
        /// de seguir com o build. Em Production, apenas pede confirmacao rapida.
        /// </summary>
        private static void ConfirmModeForBuild(LocalizationSettings settings, BuildReport report)
        {
            var isDevSettings = settings.Mode == LocalizationSettings.LocalizationMode.Development;
            var isDevBuild = (report.summary.options & BuildOptions.Development) != 0;

            if (isDevSettings)
            {
                var headline = isDevBuild
                    ? "Build de desenvolvimento usando planilhas de Development."
                    : "ATENCAO: Build de RELEASE usando planilhas de DEVELOPMENT.";

                var choice = EditorUtility.DisplayDialogComplex(
                    "Localization Mode",
                    $"{headline}\n\n" +
                    $"Settings: \"{settings.name}\"\n" +
                    $"Mode atual: Development\n\n" +
                    "Deseja trocar para Production antes do build?",
                    "Trocar para Production e continuar",
                    "Cancelar build",
                    "Continuar com Development"
                );

                switch (choice)
                {
                    case 0:
                        SwitchModeAndSave(settings, LocalizationSettings.LocalizationMode.Production);
                        break;
                    case 1:
                        throw new BuildFailedException("Build cancelado pelo usuario na verificacao de Localization Mode.");
                    case 2:
                        // Mantem Development e segue.
                        break;
                }
                return;
            }

            // Production: confirmacao rapida apenas.
            var proceed = EditorUtility.DisplayDialog(
                "Localization Settings",
                $"Build usando \"{settings.name}\" em modo Production.\n\nContinuar?",
                "Continuar",
                "Cancelar"
            );

            if (!proceed)
                throw new BuildFailedException("Build cancelado pelo usuario.");
        }

        private static void SwitchModeAndSave(LocalizationSettings settings, LocalizationSettings.LocalizationMode newMode)
        {
            settings.Mode = newMode;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
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
