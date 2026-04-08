#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using FineLocalization.Runtime;

namespace FineLocalization.Editor
{
    public class LocalizationBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = LocalizationSettings.Instance;

            if (!EditorUtility.DisplayDialog(
                    "Localization Settings"
                    , $"Current LocalizationSettings is \"{settings.name}\", are you sure you want to build with this localization setting?"
                    , "Yes", "No (Cancel build)"))
                throw new BuildFailedException("Build cancelled (opted for changing localization)");

            var activeSources = settings.GetActiveSources();

            if (activeSources == null || activeSources.Count == 0)
                throw new BuildFailedException($"[FineLocalization] Planilha '{settings.name}' está vazia! Configure antes de buildar.");

            //Debug.log($"[FineLocalization] Build usando planilha: {settings.Mode}");
        }
    }
}
#endif