#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

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
                    if (GenerateForEntry(config, entry))
                        generated++;
                    else
                        skipped++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    Debug.LogError($"[Font Bake] '{entry.bundleName}' falhou: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Font Bake] Concluído. Gerados: {generated}, Pulados: {skipped}.");
        }

        public static bool GenerateForEntry(RemoteFontBundleBuildConfig config, RemoteFontBundleBuildConfig.Entry entry)
        {
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
            var pointSize = config.samplingPointSize <= 0 ? 90 : config.samplingPointSize;
            var paddingPercent = config.paddingPercent <= 0f ? 10f : config.paddingPercent;
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

            ResolveTarget(folderPath, entry.sourceFont, out var assetPath, out var assetName);
            fontAsset.name = assetName;

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

            var material = fontAsset.material;
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
    }
}

#endif
