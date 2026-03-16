#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using FineLocalization.Runtime;
using LocalizationMode = FineLocalization.Runtime.LocalizationSettings.LocalizationMode;

namespace FineLocalization.Editor
{
    public class LocalizationBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = LocalizationSettings.Instance;
            var isDev = (report.summary.options & BuildOptions.Development) != 0;
            var expectedMode = isDev ? LocalizationMode.Development : LocalizationMode.Production;

            if (settings.Mode != expectedMode)
            {
                var modeLabel = isDev ? "Development" : "Production";
                var settingsLabel = settings.Mode == LocalizationMode.Development ? "Development" : "Production";

                var choice = EditorUtility.DisplayDialogComplex(
                    "⚠️ FineLocalization Mode Incompatível",
                    $"Build está como '{modeLabel}', mas FineLocalization Mode está como '{settingsLabel}'.\n\nO que deseja fazer?",
                    $"Manter {settingsLabel}",
                    "Cancelar Build",
                    $"Trocar para {modeLabel}"
                );

                if (choice == 1)
                {
                    SessionState.SetBool("FineLocalization.BuildCancelled", true);
                    throw new BuildFailedException("[FineLocalization] Build cancelado pelo usuário.");
                }

                if (choice == 2)
                {
                    settings.Mode = expectedMode;
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[FineLocalization] Mode alterado para: {expectedMode}");
                }
            }

            var activeSources = settings.GetActiveSources();

            if (activeSources == null || activeSources.Count == 0)
                throw new BuildFailedException($"[FineLocalization] '{settings.Mode}' Sources estão vazios! Configure antes de buildar.");

            Debug.Log($"[FineLocalization] Build iniciada com modo: {settings.Mode}");
        }
    }
}
#endif