#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEditor.SceneManagement;
using FineLocalization.Scripts.Runtime;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Optional auto-baker: builds a STATIC SDF <see cref="TMP_FontAsset"/> per language from the
    /// <c>characters_&lt;lang&gt;.txt</c> files (generated when spreadsheets are synced) plus the
    /// per-entry <see cref="RemoteFontBundleBuildConfig.Entry.sourceFont"/>.
    ///
    /// The baked asset contains only the glyphs that language actually uses, is set to Static, and
    /// has its source-font reference cleared so the raw .ttf never ships inside the AssetBundle.
    /// Manual baking via the TextMeshPro Font Asset Creator still works — this is just a shortcut.
    /// </summary>
    public static class GenerateRemoteFontAssets
    {
        [MenuItem("Tools/Fine Localization/WebGL Remote Fonts/Generate Font Assets From Characters", false, 62)]
        public static void GenerateFromMenu()
        {
            GenerateAll(RemoteFontBundleBuildConfig.GetOrCreate());
        }

        public static void GenerateAll(RemoteFontBundleBuildConfig config)
        {
            if (config == null || config.entries == null)
            {
                Debug.LogError("[Font Bake] RemoteFontBundleBuildConfig inválido.");
                return;
            }

            int generated = 0;
            int skipped = 0;
            var synced = new List<(string language, string assetName)>();

            for (int i = 0; i < config.entries.Count; i++)
            {
                var entry = config.entries[i];
                if (entry == null)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    if (GenerateForEntry(config, entry, out var language, out var assetName))
                    {
                        generated++;
                        if (!string.IsNullOrEmpty(language) && !string.IsNullOrEmpty(assetName))
                            synced.Add((language, assetName));
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    Debug.LogError($"[Font Bake] '{entry.bundleName}' falhou: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SyncLoaderMappings(synced);

            Debug.Log($"[Font Bake] Concluído. Gerados: {generated}, Pulados: {skipped}.");
        }

        public static bool GenerateForEntry(RemoteFontBundleBuildConfig config, RemoteFontBundleBuildConfig.Entry entry, out string language, out string assetName)
        {
            language = string.Empty;
            assetName = string.Empty;

            var bundleName = entry.bundleName?.Trim();
            if (string.IsNullOrEmpty(bundleName))
            {
                Debug.LogWarning("[Font Bake] Entry sem bundleName — pulada.");
                return false;
            }

            if (entry.sourceFont == null)
            {
                Debug.LogWarning($"[Font Bake] '{bundleName}' sem Source Font (.ttf/.otf). Defina a fonte de origem na janela, ou asse manualmente pelo Font Asset Creator.");
                return false;
            }

            if (entry.folder == null)
            {
                Debug.LogWarning($"[Font Bake] '{bundleName}' sem pasta de destino.");
                return false;
            }

            var folderPath = AssetDatabase.GetAssetPath(entry.folder);
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                Debug.LogWarning($"[Font Bake] '{bundleName}': destino não é uma pasta: {folderPath}");
                return false;
            }

            var characters = SanitizeCharacters(BuildRemoteFontBundles.LoadExpectedCharactersForBundle(bundleName, out _));
            if (string.IsNullOrEmpty(characters))
            {
                Debug.LogWarning($"[Font Bake] '{bundleName}': nenhum characters_<lang>.txt encontrado. Sincronize as planilhas primeiro (gera em Assets/FineLocalization/Editor/GeneratedCharacters/).");
                return false;
            }

            var atlasSize = Mathf.Clamp(config.atlasSize <= 0 ? 1024 : config.atlasSize, 256, 8192);
            var paddingPercent = config.paddingPercent <= 0f ? 10f : config.paddingPercent;

            int pointSize;
            if (config.autoSizeToAtlas)
            {
                pointSize = FindMaxPointSizeForSingleAtlas(entry.sourceFont, characters, atlasSize, paddingPercent, out _);
                if (pointSize <= 0)
                {
                    pointSize = MinAutoPointSize;
                    Debug.LogWarning($"[Font Bake] '{bundleName}': subset grande demais pra caber em 1 atlas {atlasSize}². Usando point size {pointSize} com multi-atlas (várias páginas).");
                }
                else
                {
                    Debug.Log($"[Font Bake] '{bundleName}': auto-size → point size {pointSize} (cabe em 1 atlas {atlasSize}²).");
                }
            }
            else
            {
                pointSize = config.samplingPointSize <= 0 ? 60 : config.samplingPointSize;
            }

            var padding = Mathf.Max(1, Mathf.RoundToInt(pointSize * paddingPercent / 100f));

            // Create dynamic first so TryAddCharacters can rasterize the requested glyphs into the atlas.
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                entry.sourceFont,
                pointSize,
                padding,
                GlyphRenderMode.SDFAA,
                atlasSize,
                atlasSize,
                AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true
            );

            if (fontAsset == null)
            {
                Debug.LogError($"[Font Bake] '{bundleName}': CreateFontAsset retornou null (fonte ilegível?).");
                return false;
            }

            fontAsset.isMultiAtlasTexturesEnabled = true;
            fontAsset.TryAddCharacters(characters, out var missing);
            if (!string.IsNullOrEmpty(missing))
            {
                Debug.LogWarning(
                    $"[Font Bake] '{bundleName}': a fonte '{entry.sourceFont.name}' não possui {CountUnique(missing)} glifo(s) pedido(s) — ficarão como caixa. Amostra: {Truncate(missing, 60)}"
                );
            }

            // Freeze as Static so the runtime never tries to expand the atlas from the source font.
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

            ResolveTarget(folderPath, entry.sourceFont, out var assetPath, out assetName);
            fontAsset.name = assetName;

            language = GetLanguageFromBundleName(bundleName);

            SaveFontAsset(fontAsset, assetPath);

            var pages = fontAsset.atlasTextures?.Length ?? 0;
            Debug.Log(
                $"[Font Bake] '{bundleName}' → '{assetPath}' | name='{assetName}' | chars={CountUnique(characters)} | atlas={atlasSize} pt={pointSize} pad={padding} | pages={pages}"
            );
            return true;
        }

        private static void SaveFontAsset(TMP_FontAsset fontAsset, string assetPath)
        {
            // Overwrite any existing asset at the same path so the loader's text mapping
            // (fontAssetName) keeps matching the regenerated font.
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // Atlas textures and material MUST be persisted as sub-assets or the bundle ships an empty font.
            var atlases = fontAsset.atlasTextures;
            if (atlases != null)
            {
                for (int i = 0; i < atlases.Length; i++)
                {
                    var tex = atlases[i];
                    if (tex == null)
                        continue;

                    if (string.IsNullOrEmpty(tex.name))
                        tex.name = $"Atlas {i}";

                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }

            var material = EnsureFontMaterial(fontAsset);
            if (material != null)
            {
                material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(material, fontAsset);
            }

            // Keep the raw .ttf OUT of the AssetBundle: a Static font renders from the baked atlas only.
            var so = new SerializedObject(fontAsset);
            var sourceProp = so.FindProperty("m_SourceFontFile");
            if (sourceProp != null)
            {
                sourceProp.objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);
        }

        private static Material EnsureFontMaterial(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return null;

            var atlasTexture = GetFontAtlasTexture(fontAsset);
            var material = fontAsset.material;

            if (material == null)
            {
                var shader = FindTmpDistanceFieldShader();
                if (shader == null)
                {
                    Debug.LogWarning($"[Font Bake] '{fontAsset.name}': shader TextMeshPro Distance Field nao encontrado. Material nao criado.");
                    return null;
                }

                material = new Material(shader);
                fontAsset.material = material;
            }
            else if (!HasUsableShader(material))
            {
                var shader = FindTmpDistanceFieldShader();
                if (shader != null)
                    material.shader = shader;
                else
                    Debug.LogWarning($"[Font Bake] '{fontAsset.name}': material esta com shader invalido e nao foi possivel localizar o shader TMP Distance Field.");
            }

            if (atlasTexture != null)
                material.SetTexture(ShaderUtilities.ID_MainTex, atlasTexture);

            ApplyFontAtlasSdfMetrics(material, fontAsset);
            return material;
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

        private static Shader FindTmpDistanceFieldShader()
        {
            return Shader.Find("TextMeshPro/Mobile/Distance Field") ??
                   Shader.Find("TextMeshPro/Distance Field");
        }

        private static bool HasUsableShader(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            return material.shader.name.IndexOf("InternalErrorShader", StringComparison.OrdinalIgnoreCase) < 0;
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

        private static void ResolveTarget(string folderPath, Font sourceFont, out string assetPath, out string assetName)
        {
            // Reuse the existing TMP_FontAsset in the folder (same name + path) so we regenerate it
            // in place. Otherwise create a new "<font> SDF" asset.
            var guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { folderPath });
            if (guids != null)
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var existingPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!string.Equals(Path.GetExtension(existingPath), ".asset", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(existingPath);
                    if (existing == null)
                        continue;

                    assetPath = existingPath;
                    assetName = existing.name;
                    return;
                }
            }

            assetName = $"{sourceFont.name} SDF";
            assetPath = $"{folderPath}/{assetName}.asset";
        }

        private static string SanitizeCharacters(string characters)
        {
            if (string.IsNullOrEmpty(characters))
                return string.Empty;

            var seen = new HashSet<char>();
            var sb = new StringBuilder(characters.Length);
            foreach (var c in characters)
            {
                if (char.IsControl(c) || !seen.Add(c))
                    continue;

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static int CountUnique(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            var set = new HashSet<char>();
            foreach (var c in value)
                set.Add(c);

            return set.Count;
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value;

            return value.Substring(0, max) + "…";
        }

        private const int MinAutoPointSize = 16;
        private const int MaxAutoPointSize = 120;

        /// <summary>
        /// Binary-searches the LARGEST sampling point size whose glyphs all fit in a SINGLE
        /// atlas page of <paramref name="atlasSize"/>². Returns 0 if even the smallest size overflows.
        /// Glyphs the source font genuinely lacks are excluded from the fit test (they can never be
        /// added regardless of size) and returned via <paramref name="fontLacks"/>.
        /// </summary>
        private static int FindMaxPointSizeForSingleAtlas(Font font, string characters, int atlasSize, float paddingPercent, out string fontLacks)
        {
            fontLacks = string.Empty;
            if (font == null || string.IsNullOrEmpty(characters))
                return 0;

            // Probe at a small size with multi-atlas ON so space is never the limit: whatever stays
            // missing here is genuinely absent from the source font.
            var supported = characters;
            var probe = TMP_FontAsset.CreateFontAsset(font, 24, 2, GlyphRenderMode.SDFAA, atlasSize, atlasSize, AtlasPopulationMode.Dynamic, true);
            if (probe != null)
            {
                probe.TryAddCharacters(characters, out fontLacks);
                supported = RemoveChars(characters, fontLacks);
                DestroyFontAsset(probe);
            }

            if (string.IsNullOrEmpty(supported))
                return 0;

            int lo = MinAutoPointSize, hi = MaxAutoPointSize, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int pad = Mathf.Max(1, Mathf.RoundToInt(mid * paddingPercent / 100f));

                // Multi-atlas OFF so overflow shows up as missing chars instead of silently adding a page.
                var candidate = TMP_FontAsset.CreateFontAsset(
                    font, mid, pad, GlyphRenderMode.SDFAA, atlasSize, atlasSize,
                    AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: false
                );

                var fits = false;
                if (candidate != null)
                {
                    candidate.TryAddCharacters(supported, out var miss);
                    fits = string.IsNullOrEmpty(miss) && (candidate.atlasTextures == null || candidate.atlasTextures.Length <= 1);
                    DestroyFontAsset(candidate);
                }

                if (fits)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return best;
        }

        private static string RemoveChars(string source, string toRemove)
        {
            if (string.IsNullOrEmpty(toRemove) || string.IsNullOrEmpty(source))
                return source;

            var remove = new HashSet<char>(toRemove);
            var sb = new StringBuilder(source.Length);
            foreach (var c in source)
            {
                if (!remove.Contains(c))
                    sb.Append(c);
            }

            return sb.ToString();
        }

        private static void DestroyFontAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return;

            if (fontAsset.atlasTextures != null)
            {
                foreach (var tex in fontAsset.atlasTextures)
                {
                    if (tex != null)
                        UnityEngine.Object.DestroyImmediate(tex);
                }
            }

            if (fontAsset.material != null)
                UnityEngine.Object.DestroyImmediate(fontAsset.material);

            UnityEngine.Object.DestroyImmediate(fontAsset);
        }

        private static string GetLanguageFromBundleName(string bundleName)
        {
            var value = Path.GetFileNameWithoutExtension(bundleName ?? string.Empty).Trim().ToLowerInvariant();
            if (value.StartsWith("font_", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("font_".Length);

            return value.Replace('_', '-');
        }

        /// <summary>
        /// Auto-fills the "Remote TMP Font Asset Name" of every RemoteFontBundleLoader in the OPEN
        /// scenes for each generated language: updates the matching mapping's name (overwriting a
        /// stale one) or adds a mapping if none exists. Loaders that live only in a prefab outside an
        /// open scene are not touched — use the loader's "Add Missing Mappings From Builder Config".
        /// </summary>
        private static void SyncLoaderMappings(List<(string language, string assetName)> generated)
        {
            if (generated == null || generated.Count == 0)
                return;

            var loaders = UnityEngine.Object.FindObjectsByType<RemoteFontBundleLoader>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (loaders == null || loaders.Length == 0)
            {
                Debug.Log("[Font Bake] Nenhum RemoteFontBundleLoader nas cenas abertas pra sincronizar. (Se o loader estiver só num prefab, use 'Add Missing Mappings From Builder Config' no inspector dele.)");
                return;
            }

            var updatedLoaders = 0;
            foreach (var loader in loaders)
            {
                if (loader == null)
                    continue;

                var so = new SerializedObject(loader);
                var bundlesProp = so.FindProperty("bundles");
                if (bundlesProp == null)
                    continue;

                var changed = false;
                foreach (var (language, assetName) in generated)
                {
                    if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(assetName))
                        continue;

                    var matchIndex = -1;
                    for (int i = 0; i < bundlesProp.arraySize; i++)
                    {
                        var prefix = bundlesProp.GetArrayElementAtIndex(i).FindPropertyRelative("languagePrefix")?.stringValue;
                        if (RemoteFontBundleLoader.IsSameLanguageOrRoot(prefix, language))
                        {
                            matchIndex = i;
                            break;
                        }
                    }

                    if (matchIndex >= 0)
                    {
                        var nameProp = bundlesProp.GetArrayElementAtIndex(matchIndex).FindPropertyRelative("fontAssetName");
                        if (nameProp != null && nameProp.stringValue != assetName)
                        {
                            nameProp.stringValue = assetName;
                            changed = true;
                        }
                    }
                    else
                    {
                        var index = bundlesProp.arraySize;
                        bundlesProp.arraySize++;
                        var element = bundlesProp.GetArrayElementAtIndex(index);
                        element.FindPropertyRelative("languagePrefix").stringValue = language;
                        element.FindPropertyRelative("fontAssetName").stringValue = assetName;
                        changed = true;
                    }
                }

                if (!changed)
                    continue;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(loader);
                if (loader.gameObject.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);

                updatedLoaders++;
                Debug.Log($"[Font Bake] Loader '{loader.name}' sincronizado.");
            }

            Debug.Log($"[Font Bake] Sync de loaders: {updatedLoaders} loader(s) atualizado(s) nas cenas abertas.");
        }
    }
}

#endif
