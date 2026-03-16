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
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = LocalizationSettings.Instance;
            var settingsLabel = settings.Mode == LocalizationMode.Development ? "Development" : "Production";

            var choice = EditorUtility.DisplayDialogComplex(
                "Planilha de Tradução",
                $"Qual planilha de tradução deseja usar nesta build?\n\nAtual: {settingsLabel}",
                $"Usar Development",
                $"Usar Production",
                ""
            );

            if (choice == 1)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Debug.Log($"[FineLocalization] Planilha alterada para: {settingsLabel}");
            }

            var activeSources = settings.GetActiveSources();

            if (activeSources == null || activeSources.Count == 0)
                throw new BuildFailedException($"[FineLocalization] Planilha '{settings.Mode}' está vazia! Configure antes de buildar.");

            Debug.Log($"[FineLocalization] Build usando planilha: {settingsLabel}");
        }
    }
}
#endif