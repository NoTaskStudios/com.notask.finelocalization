#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    public static class BuildRemoteFontBundles
    {
        private const string OutputFolder = "AssetBundles/WebGL/Fonts";

        private const string ChineseSimplifiedFolder = "Assets/RemoteFonts/zh-cn";
        private const string ChineseTraditionalFolder = "Assets/RemoteFonts/zh-tw";
        private const string JapaneseFolder = "Assets/RemoteFonts/ja";
        private const string KoreanFolder = "Assets/RemoteFonts/ko";
        private const string ThaiFolder = "Assets/RemoteFonts/th";

        [MenuItem("Tools/Fine Localization/Remote Fonts/Build WebGL AssetBundles", false, 200)]
        public static void BuildWebGlFontBundles()
        {
            if (!Directory.Exists(OutputFolder))
                Directory.CreateDirectory(OutputFolder);

            var builds = new List<AssetBundleBuild>();

            AddFontBundle(builds, "fonts_zh_cn", ChineseSimplifiedFolder);
            AddFontBundle(builds, "fonts_zh_tw", ChineseTraditionalFolder);
            AddFontBundle(builds, "fonts_ja", JapaneseFolder);
            AddFontBundle(builds, "fonts_ko", KoreanFolder);
            AddFontBundle(builds, "fonts_th", ThaiFolder);

            if (builds.Count == 0)
            {
                Debug.LogWarning("[Fonts Bundle] Nenhum TMP_FontAsset encontrado para empacotar.");
                return;
            }

            var manifest = BuildPipeline.BuildAssetBundles(
                OutputFolder,
                builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.WebGL
            );

            if (manifest == null)
            {
                Debug.LogError("[Fonts Bundle] Falha ao gerar AssetBundles.");
                return;
            }

            Debug.Log(
                $"[Fonts Bundle] AssetBundles gerados com sucesso em:\n{Path.GetFullPath(OutputFolder)}"
            );

            foreach (var file in Directory.GetFiles(OutputFolder))
                Debug.Log($"[Fonts Bundle] Arquivo gerado: {file}");

            AssetDatabase.Refresh();
        }

        private static void AddFontBundle(
            List<AssetBundleBuild> builds,
            string bundleName,
            string folderPath
        )
        {
            if (!Directory.Exists(folderPath))
            {
                Debug.LogWarning($"[Fonts Bundle] Pasta não encontrada: {folderPath}");
                return;
            }

            var guids = AssetDatabase.FindAssets(
                "t:TMP_FontAsset",
                new[] { folderPath }
            );

            if (guids == null || guids.Length == 0)
            {
                Debug.LogWarning(
                    $"[Fonts Bundle] Nenhum TMP_FontAsset encontrado em: {folderPath}"
                );
                return;
            }

            var assetPaths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();

            builds.Add(new AssetBundleBuild
            {
                assetBundleName = bundleName,
                assetNames = assetPaths
            });

            Debug.Log(
                $"[Fonts Bundle] Bundle '{bundleName}' configurado com {assetPaths.Length} asset(s)."
            );
        }
    }
}

#endif
