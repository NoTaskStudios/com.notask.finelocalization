#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Builds one WebGL AssetBundle per entry configured in
    /// <see cref="RemoteFontBundleBuildConfig"/>. Nothing is hardcoded — to add a new
    /// language, open Tools/Fine Localization/WebGL Remote Fonts/Open Bundle Builder Window and add an entry.
    /// </summary>
    public static class BuildRemoteFontBundles
    {
        private const string DefaultOutputFolder = "AssetBundles/WebGL/Fonts";
        private const string BundleFileExtension = ".ft";

        [MenuItem("Tools/Fine Localization/WebGL Remote Fonts/Build Bundles Now", false, 61)]
        public static void BuildWebGlFontBundles()
        {
            var config = RemoteFontBundleBuildConfig.GetOrCreate();
            if (config == null)
            {
                Debug.LogError("[Fonts Bundle] RemoteFontBundleBuildConfig nao pôde ser carregado/criado.");
                return;
            }

            if (config.entries == null || config.entries.Count == 0)
            {
                Debug.LogWarning(
                    "[Fonts Bundle] Nenhuma entry configurada. Abra " +
                    "Tools/Fine Localization/WebGL Remote Fonts/Open Bundle Builder Window e adicione idiomas."
                );
                return;
            }

            var output = string.IsNullOrWhiteSpace(config.outputFolder)
                ? DefaultOutputFolder
                : config.outputFolder;

            if (!Directory.Exists(output))
                Directory.CreateDirectory(output);

            var builds = new List<AssetBundleBuild>(config.entries.Count);
            var seenNames = new HashSet<string>();

            foreach (var entry in config.entries)
            {
                if (entry == null) continue;

                var bundleName = entry.bundleName?.Trim();
                if (string.IsNullOrEmpty(bundleName))
                {
                    Debug.LogWarning("[Fonts Bundle] Entry com bundleName vazio — ignorada.");
                    continue;
                }

                if (!seenNames.Add(bundleName))
                {
                    Debug.LogWarning($"[Fonts Bundle] Bundle name duplicado '{bundleName}' — ignorado (mantém o primeiro).");
                    continue;
                }

                if (entry.folder == null)
                {
                    Debug.LogWarning($"[Fonts Bundle] Entry '{bundleName}' sem pasta atribuída — ignorada.");
                    continue;
                }

                var folderPath = AssetDatabase.GetAssetPath(entry.folder);
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    Debug.LogWarning(
                        $"[Fonts Bundle] '{bundleName}' aponta para um asset que não é pasta: {folderPath}"
                    );
                    continue;
                }

                var guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { folderPath });
                if (guids == null || guids.Length == 0)
                {
                    Debug.LogWarning(
                        $"[Fonts Bundle] Nenhum TMP_FontAsset em '{folderPath}' (bundle '{bundleName}')."
                    );
                    continue;
                }

                var assetPaths = guids.Select(AssetDatabase.GUIDToAssetPath).ToArray();
                var bundleFileName = EnsureBundleFileExtension(bundleName);
                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = bundleFileName,
                    assetNames = assetPaths
                });

                Debug.Log($"[Fonts Bundle] '{bundleFileName}' → {assetPaths.Length} asset(s)");
            }

            if (builds.Count == 0)
            {
                Debug.LogWarning("[Fonts Bundle] Nenhum bundle elegível para empacotar.");
                return;
            }

            var manifest = BuildPipeline.BuildAssetBundles(
                output,
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
                $"[Fonts Bundle] OK — {builds.Count} bundle(s) gerados em:\n{Path.GetFullPath(output)}"
            );

            AssetDatabase.Refresh();
        }

        private static string EnsureBundleFileExtension(string bundleName)
        {
            var extension = Path.GetExtension(bundleName);
            if (string.Equals(extension, BundleFileExtension, System.StringComparison.OrdinalIgnoreCase))
                return bundleName;

            return bundleName + BundleFileExtension;
        }
    }
}

#endif
