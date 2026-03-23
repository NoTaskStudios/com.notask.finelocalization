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

            var choice = EditorUtility.DisplayDialogComplex(
                "Planilha de Tradução",
                "Qual planilha de tradução deseja usar nesta build?",
                "Development",
                "Production",
                ""
            );

            settings.Mode = choice == 0 ? LocalizationMode.Development : LocalizationMode.Production;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            var activeSources = settings.GetActiveSources();

            if (activeSources == null || activeSources.Count == 0)
                throw new BuildFailedException($"[FineLocalization] Planilha '{settings.Mode}' está vazia! Configure antes de buildar.");

            //Debug.log($"[FineLocalization] Build usando planilha: {settings.Mode}");
        }
    }
}
#endif