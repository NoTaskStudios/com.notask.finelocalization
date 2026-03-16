#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using FineLocalization.Runtime;
using LocalizationMode = FineLocalization.Runtime.LocalizationMode;

namespace FineLocalization.Editor
{
    public class LocalizationBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = LocalizationSettings.Instance;
            var isDev = (report.summary.options & BuildOptions.Development) != 0;
            var expectedMode = isDev ? LocalizationMode.Development : LocalizationMode.Production;

            if (settings.Mode != expectedMode)
            {
                var modeLabel = isDev ? "Development" : "Production";
                var settingsLabel = settings.Mode == LocalizationMode.Development ? "Development" : "Production";

                var confirm = EditorUtility.DisplayDialog(
                    "⚠️ Localization Mode Mismatch",
                    $"Build está como '{modeLabel}', mas Localization Mode está como '{settingsLabel}'.\n\nDeseja continuar mesmo assim?",
                    "Continuar",
                    "Cancelar"
                );

                if (!confirm)
                    throw new BuildFailedException("Build cancelado: Localization Mode incompatível com o tipo de build.");
            }

            var activeSources = settings.GetActiveSources();

            if (activeSources == null || activeSources.Count == 0)
                throw new BuildFailedException($"[Localization] '{settings.Mode}' Sources estão vazios! Configure antes de buildar.");

            Debug.Log($"[Localization] Build iniciada com modo: <b>{settings.Mode}</b>");
        }
    }
}
#endif