#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
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
        private const string DefaultGlobalOutputFolder = "AssetBundles/WebGL/GlobalFonts";
        private const string BundleFileExtension = ".ft";
        private const string GeneratedCharactersFolder = "Assets/FineLocalization/Editor/GeneratedCharacters";
        private const string LanguageCharactersTxtPrefix = "characters_";

        private enum BuildOutputKind
        {
            PerGame,
            Global
        }

        [MenuItem("Tools/Fine Localization/WebGL Remote Fonts/Build Bundles Now", false, 61)]
        public static void BuildWebGlFontBundles()
        {
            BuildWebGlFontBundles(BuildOutputKind.PerGame);
        }

        [MenuItem("Tools/Fine Localization/WebGL Remote Fonts/Global/Build Global Bundles Now", false, 63)]
        public static void BuildWebGlGlobalFontBundles()
        {
            BuildWebGlFontBundles(BuildOutputKind.Global);
        }

        private static void BuildWebGlFontBundles(BuildOutputKind outputKind)
        {
            var config = RemoteFontBundleBuildConfig.GetOrCreate();
            var logPrefix = outputKind == BuildOutputKind.Global ? "[Global Fonts Bundle]" : "[Fonts Bundle]";
            if (config == null)
            {
                Debug.LogError($"{logPrefix} RemoteFontBundleBuildConfig nao pôde ser carregado/criado.");
                return;
            }

            if (config.entries == null || config.entries.Count == 0)
            {
                Debug.LogWarning(
                    $"{logPrefix} Nenhuma entry configurada. Abra " +
                    "Tools/Fine Localization/WebGL Remote Fonts/Open Bundle Builder Window e adicione idiomas."
                );
                return;
            }

            var output = GetOutputFolder(config, outputKind);

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
                    Debug.LogWarning($"{logPrefix} Entry com bundleName vazio — ignorada.");
                    continue;
                }

                if (!seenNames.Add(bundleName))
                {
                    Debug.LogWarning($"{logPrefix} Bundle name duplicado '{bundleName}' — ignorado (mantém o primeiro).");
                    continue;
                }

                if (entry.folder == null)
                {
                    Debug.LogWarning($"{logPrefix} Entry '{bundleName}' sem pasta atribuída — ignorada.");
                    continue;
                }

                var folderPath = AssetDatabase.GetAssetPath(entry.folder);
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    Debug.LogWarning(
                        $"{logPrefix} '{bundleName}' aponta para um asset que não é pasta: {folderPath}"
                    );
                    continue;
                }

                var guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { folderPath });
                if (guids == null || guids.Length == 0)
                {
                    Debug.LogWarning(
                        $"{logPrefix} Nenhum TMP_FontAsset em '{folderPath}' (bundle '{bundleName}')."
                    );
                    continue;
                }

                var assetPaths = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(assetPath => IsValidTmpFontAssetPath(assetPath, logPrefix))
                    .ToArray();

                if (assetPaths.Length == 0)
                {
                    Debug.LogWarning(
                        $"{logPrefix} Nenhum TMP_FontAsset .asset valido em '{folderPath}' (bundle '{bundleName}'). " +
                        "Gere o asset pelo TextMeshPro Font Asset Creator e arraste a pasta que contem o .asset, nao apenas o .ttf/.otf."
                    );
                    continue;
                }

                ValidateFontAssetsContainExpectedCharacters(bundleName, assetPaths, logPrefix);

                var bundleFileName = EnsureBundleFileExtension(bundleName);
                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = bundleFileName,
                    assetNames = assetPaths
                });

                Debug.Log($"{logPrefix} '{bundleFileName}' → {assetPaths.Length} asset(s)");
            }

            if (builds.Count == 0)
            {
                Debug.LogWarning($"{logPrefix} Nenhum bundle elegível para empacotar.");
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
                Debug.LogError($"{logPrefix} Falha ao gerar AssetBundles.");
                return;
            }

            Debug.Log(
                $"{logPrefix} OK — {builds.Count} bundle(s) gerados em:\n{Path.GetFullPath(output)}"
            );

            AssetDatabase.Refresh();
        }

        private static string GetOutputFolder(RemoteFontBundleBuildConfig config, BuildOutputKind outputKind)
        {
            if (outputKind == BuildOutputKind.Global)
            {
                return string.IsNullOrWhiteSpace(config.globalOutputFolder)
                    ? DefaultGlobalOutputFolder
                    : config.globalOutputFolder;
            }

            return string.IsNullOrWhiteSpace(config.outputFolder)
                ? DefaultOutputFolder
                : config.outputFolder;
        }

        private static string EnsureBundleFileExtension(string bundleName)
        {
            var extension = Path.GetExtension(bundleName);
            if (string.Equals(extension, BundleFileExtension, System.StringComparison.OrdinalIgnoreCase))
                return bundleName;

            return bundleName + BundleFileExtension;
        }

        private static bool IsValidTmpFontAssetPath(string assetPath, string logPrefix)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            if (!string.Equals(Path.GetExtension(assetPath), ".asset", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"{logPrefix} Ignorando '{assetPath}': TMP_FontAsset precisa ser um .asset, nao a fonte bruta.");
                return false;
            }

            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fontAsset != null)
                return true;

            Debug.LogWarning($"{logPrefix} Ignorando '{assetPath}': AssetDatabase nao carregou como TMP_FontAsset.");
            return false;
        }

        private static void ValidateFontAssetsContainExpectedCharacters(string bundleName, string[] assetPaths, string logPrefix)
        {
            var expectedCharacters = LoadExpectedCharactersForBundle(bundleName, out var sourceFiles);
            if (string.IsNullOrEmpty(expectedCharacters))
                return;

            foreach (var assetPath in assetPaths)
            {
                var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                if (fontAsset == null)
                    continue;

                var missing = GetMissingCharacters(fontAsset, expectedCharacters, 48, out var missingCount);
                if (missingCount == 0)
                    continue;

                Debug.LogWarning(
                    $"{logPrefix} '{fontAsset.name}' nao contem {missingCount} caractere(s) do TXT de caracteres " +
                    $"usado para o bundle '{bundleName}'. Recrie o TMP_FontAsset com: {string.Join(", ", sourceFiles)}. " +
                    $"Primeiros faltando: {missing}"
                );
            }
        }

        internal static string LoadExpectedCharactersForBundle(string bundleName, out List<string> sourceFiles)
        {
            sourceFiles = new List<string>();

            if (!Directory.Exists(GeneratedCharactersFolder))
                return string.Empty;

            var bundleLanguage = GetLanguageFromBundleName(bundleName);
            if (string.IsNullOrEmpty(bundleLanguage))
                return string.Empty;

            var builder = new StringBuilder();
            var seen = new HashSet<char>();

            foreach (var path in Directory.GetFiles(GeneratedCharactersFolder, $"{LanguageCharactersTxtPrefix}*.txt", SearchOption.TopDirectoryOnly))
            {
                var fileLanguage = Path.GetFileNameWithoutExtension(path)
                    .Substring(LanguageCharactersTxtPrefix.Length)
                    .Trim()
                    .ToLowerInvariant()
                    .Replace('_', '-');

                if (!LanguageMatchesBundle(fileLanguage, bundleLanguage))
                    continue;

                sourceFiles.Add(path);

                var text = File.ReadAllText(path, Encoding.UTF8);
                foreach (var character in text)
                {
                    if (char.IsControl(character) || !seen.Add(character))
                        continue;

                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private static string GetLanguageFromBundleName(string bundleName)
        {
            var value = Path.GetFileNameWithoutExtension(bundleName ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (value.StartsWith("font_", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("font_".Length);

            return value.Replace('_', '-');
        }

        private static bool LanguageMatchesBundle(string fileLanguage, string bundleLanguage)
        {
            if (string.IsNullOrEmpty(fileLanguage) || string.IsNullOrEmpty(bundleLanguage))
                return false;

            if (string.Equals(fileLanguage, bundleLanguage, StringComparison.OrdinalIgnoreCase))
                return true;

            return fileLanguage.StartsWith(bundleLanguage + "-", StringComparison.OrdinalIgnoreCase) ||
                   bundleLanguage.StartsWith(fileLanguage + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetMissingCharacters(TMP_FontAsset fontAsset, string expectedCharacters, int maxItems, out int missingCount)
        {
            missingCount = 0;
            var samples = new List<string>();
            var seen = new HashSet<char>();

            foreach (var character in expectedCharacters)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character) || !seen.Add(character))
                    continue;

                if (fontAsset.HasCharacter(character))
                    continue;

                missingCount++;
                if (samples.Count < maxItems)
                    samples.Add($"{character}(U+{(int)character:X4})");
            }

            return samples.Count == 0 ? "<none>" : string.Join(", ", samples);
        }
    }
}

#endif
