#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FineLocalization.Runtime;
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
        private const string BundleFileExtension = ".ft";
        private const string GeneratedCharactersFolder = "Assets/FineLocalization/Editor/GeneratedCharacters";
        private const string LanguageCharactersTxtPrefix = "characters_";
        private const string LogPrefix = "[Fonts Bundle]";

        [MenuItem("Tools/Fine Localization/WebGL Remote Fonts/Build Bundles Now", false, 61)]
        public static void BuildWebGlFontBundles()
        {
            var config = RemoteFontBundleBuildConfig.GetOrCreate();
            var logPrefix = LogPrefix;
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

                RepairFontAssetsBeforeBundle(assetPaths, logPrefix);
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

            var sizes = new StringBuilder();
            foreach (var build in builds)
            {
                var file = Path.Combine(output, build.assetBundleName);
                if (File.Exists(file))
                    sizes.AppendLine($"  {build.assetBundleName,-20} {new FileInfo(file).Length / 1024f,8:0.0} KB");
            }

            Debug.Log(
                $"{logPrefix} OK — {builds.Count} bundle(s) em {Path.GetFullPath(output)}\n{sizes}"
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

        private static void RepairFontAssetsBeforeBundle(string[] assetPaths, string logPrefix)
        {
            if (assetPaths == null)
                return;

            for (int i = 0; i < assetPaths.Length; i++)
                RepairFontAssetBeforeBundle(assetPaths[i], logPrefix);
        }

        private static void RepairFontAssetBeforeBundle(string assetPath, string logPrefix)
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fontAsset == null)
                return;

            var changed = false;
            var atlasTexture = GetFontAtlasTexture(fontAsset);
            if (atlasTexture == null)
            {
                Debug.LogWarning($"{logPrefix} '{fontAsset.name}' sem atlasTexture em '{assetPath}'.");
                return;
            }

            atlasTexture.hideFlags = HideFlags.None;
            changed |= EnsureSubAsset(atlasTexture, fontAsset, assetPath, logPrefix);
            EditorUtility.SetDirty(atlasTexture);

            var material = SafeGetFontMaterial(fontAsset);
            if (!IsValidFontMaterial(material, atlasTexture) || !IsObjectInAsset(material, assetPath))
            {
                var repairedMaterial = CreatePersistentFontMaterial(fontAsset, atlasTexture, material, assetPath, logPrefix);
                if (repairedMaterial != null)
                {
                    fontAsset.material = repairedMaterial;
                    material = repairedMaterial;
                    changed = true;
                }
            }

            if (material != null)
            {
                material.name = fontAsset.name + " Material";
                material.hideFlags = HideFlags.None;
                changed |= TryNormalizeMainTexture(material, atlasTexture);

                ApplyFontAtlasSdfMetrics(material, fontAsset);
                EditorUtility.SetDirty(material);
            }

            if (changed)
            {
                EditorUtility.SetDirty(fontAsset);
                AssetDatabase.SaveAssetIfDirty(fontAsset);
                Debug.Log($"{logPrefix} Reparado material/atlas de '{fontAsset.name}' antes do bundle.");
            }
        }

        private static Material CreatePersistentFontMaterial(TMP_FontAsset fontAsset, Texture atlasTexture, Material currentMaterial, string assetPath, string logPrefix)
        {
            var template = HasUsableShader(currentMaterial)
                ? currentMaterial
                : SafeGetFontMaterial(TMP_Settings.defaultFontAsset);

            Material material = null;
            if (HasUsableShader(template))
                material = new Material(template);
            else
            {
                var shader = FindTmpDistanceFieldShader();
                if (shader != null)
                    material = new Material(shader);
            }

            if (material == null)
            {
                Debug.LogWarning($"{logPrefix} Nao foi possivel criar material TMP valido para '{fontAsset.name}'.");
                return null;
            }

            material.name = fontAsset.name + " Material";
            material.hideFlags = HideFlags.None;
            TryNormalizeMainTexture(material, atlasTexture);
            ApplyFontAtlasSdfMetrics(material, fontAsset);

            AssetDatabase.AddObjectToAsset(material, fontAsset);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static bool EnsureSubAsset(UnityEngine.Object asset, UnityEngine.Object owner, string ownerPath, string logPrefix)
        {
            if (asset == null || owner == null || string.IsNullOrEmpty(ownerPath))
                return false;

            var assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.Equals(assetPath, ownerPath, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning($"{logPrefix} '{asset.name}' de '{owner.name}' nao esta salvo como sub-asset do TMP_FontAsset: {assetPath}");
                return false;
            }

            AssetDatabase.AddObjectToAsset(asset, owner);
            return true;
        }

        private static bool IsObjectInAsset(UnityEngine.Object asset, string assetPath)
        {
            if (asset == null || string.IsNullOrEmpty(assetPath))
                return false;

            return string.Equals(AssetDatabase.GetAssetPath(asset), assetPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidFontMaterial(Material material, Texture expectedAtlas)
        {
            if (!HasUsableShader(material) || expectedAtlas == null)
                return false;

            try
            {
                return material.GetTexture(ShaderUtilities.ID_MainTex) == expectedAtlas;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryNormalizeMainTexture(Material material, Texture expectedAtlas)
        {
            if (material == null || expectedAtlas == null)
                return false;

            try
            {
                if (!material.HasProperty(ShaderUtilities.ID_MainTex))
                    return false;

                if (material.GetTexture(ShaderUtilities.ID_MainTex) == expectedAtlas)
                    return false;

                material.SetTexture(ShaderUtilities.ID_MainTex, expectedAtlas);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Material SafeGetFontMaterial(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return null;

            try
            {
                return fontAsset.material;
            }
            catch
            {
                return null;
            }
        }

        private static Texture GetFontAtlasTexture(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return null;

            var atlasTexture = fontAsset.atlasTexture;
            if (atlasTexture == null && fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
                atlasTexture = fontAsset.atlasTextures[0];

            return atlasTexture;
        }

        private static bool HasUsableShader(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            return IsUsableShader(material.shader);
        }

        private static bool IsUsableShader(Shader shader)
        {
            return shader != null &&
                   shader.name.IndexOf("InternalErrorShader", StringComparison.OrdinalIgnoreCase) < 0 &&
                   shader.isSupported;
        }

        private static Shader FindTmpDistanceFieldShader()
        {
            var defaultShader = SafeGetFontMaterial(TMP_Settings.defaultFontAsset)?.shader;
            if (IsUsableShader(defaultShader))
                return defaultShader;

            var mobileShader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (IsUsableShader(mobileShader))
                return mobileShader;

            var desktopShader = Shader.Find("TextMeshPro/Distance Field");
            return IsUsableShader(desktopShader) ? desktopShader : null;
        }

        private static void ApplyFontAtlasSdfMetrics(Material material, TMP_FontAsset fontAsset)
        {
            if (material == null || fontAsset == null)
                return;

            SetMaterialFloatIfPresent(material, "_TextureWidth", fontAsset.atlasWidth);
            SetMaterialFloatIfPresent(material, "_TextureHeight", fontAsset.atlasHeight);
            SetMaterialFloatIfPresent(material, "_GradientScale", fontAsset.atlasPadding + 1);
        }

        private static void SetMaterialFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName) && value > 0f)
                material.SetFloat(propertyName, value);
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
            return LanguageCode.FromBundleName(Path.GetFileNameWithoutExtension(bundleName ?? string.Empty));
        }

        /// <summary>
        /// Propositalmente mais estrita que <see cref="LanguageCode.IsSameOrRoot"/>: casar por raiz
        /// uniria "zh-cn" e "zh-tw", e o characters_zh-tw.txt entraria no bundle de zh-cn.
        /// </summary>
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
