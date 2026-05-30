#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Editor-only config that lists every language → folder mapping used by
    /// <see cref="BuildRemoteFontBundles"/>. Each entry produces one AssetBundle
    /// containing the TMP_FontAssets found inside <see cref="Entry.folder"/>.
    /// </summary>
    public class RemoteFontBundleBuildConfig : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("Nome do bundle gerado (vira o nome do arquivo). Ex: font_zh-cn, font_ja, font_ar.")]
            public string bundleName;

            [Tooltip("Pasta contendo os TMP_FontAsset desse idioma. Arraste a pasta do Project aqui.")]
            public DefaultAsset folder;
        }

        [Tooltip("Pasta de saída relativa ao projeto. Ex: AssetBundles/WebGL/Fonts")]
        public string outputFolder = "AssetBundles/WebGL/Fonts";

        [Tooltip("Lista dinâmica de bundles. Adicione/remova quantos idiomas precisar.")]
        public List<Entry> entries = new();

        private const string DefaultAssetPath =
            "Assets/FineLocalization/Editor/RemoteFontBundleBuildConfig.asset";

        private const string DefaultRemoteFontsFolder =
            "Assets/FineLocalization/RemoteFonts";

        /// <summary>
        /// Locates the project's config (first match) or creates one populated with the
        /// 5 default language entries that used to be hardcoded.
        /// </summary>
        public static RemoteFontBundleBuildConfig GetOrCreate()
        {
            var guids = AssetDatabase.FindAssets("t:RemoteFontBundleBuildConfig");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var config = AssetDatabase.LoadAssetAtPath<RemoteFontBundleBuildConfig>(path);
                if (NormalizeLegacyBundleNames(config) | EnsureDefaultRemoteFontFolders(config))
                {
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                }

                return config;
            }

            EnsureFolder(Path.GetDirectoryName(DefaultAssetPath));
            EnsureFolder(DefaultRemoteFontsFolder);

            var instance = CreateInstance<RemoteFontBundleBuildConfig>();
            instance.entries.Add(CreateDefaultEntry("font_zh-cn", "ChineseSimplified"));
            instance.entries.Add(CreateDefaultEntry("font_zh-tw", "ChineseTraditional"));
            instance.entries.Add(CreateDefaultEntry("font_ja", "Japanese"));
            instance.entries.Add(CreateDefaultEntry("font_ko", "Korean"));
            instance.entries.Add(CreateDefaultEntry("font_th", "Thai"));

            AssetDatabase.CreateAsset(instance, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return instance;
        }

        private static bool NormalizeLegacyBundleNames(RemoteFontBundleBuildConfig config)
        {
            if (config == null || config.entries == null)
                return false;

            var changed = false;
            foreach (var entry in config.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.bundleName))
                    continue;

                var normalized = entry.bundleName.Trim();
                switch (normalized)
                {
                    case "font_zh_cn":
                        entry.bundleName = "font_zh-cn";
                        changed = true;
                        break;
                    case "font_zh_tw":
                        entry.bundleName = "font_zh-tw";
                        changed = true;
                        break;
                }
            }

            return changed;
        }

        private static bool EnsureDefaultRemoteFontFolders(RemoteFontBundleBuildConfig config)
        {
            EnsureFolder(DefaultRemoteFontsFolder);
            EnsureFolder($"{DefaultRemoteFontsFolder}/ChineseSimplified");
            EnsureFolder($"{DefaultRemoteFontsFolder}/ChineseTraditional");
            EnsureFolder($"{DefaultRemoteFontsFolder}/Japanese");
            EnsureFolder($"{DefaultRemoteFontsFolder}/Korean");
            EnsureFolder($"{DefaultRemoteFontsFolder}/Thai");

            if (config == null || config.entries == null) return false;

            var changed = false;
            foreach (var entry in config.entries)
            {
                if (entry == null || entry.folder != null) continue;

                var folderName = GetDefaultFolderName(entry.bundleName);
                if (string.IsNullOrEmpty(folderName)) continue;

                var folderPath = $"{DefaultRemoteFontsFolder}/{folderName}";
                EnsureFolder(folderPath);

                entry.folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
                changed |= entry.folder != null;
            }

            return changed;
        }

        private static Entry CreateDefaultEntry(string bundleName, string folderName)
        {
            var folderPath = $"{DefaultRemoteFontsFolder}/{folderName}";
            EnsureFolder(folderPath);

            return new Entry
            {
                bundleName = bundleName,
                folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath)
            };
        }

        private static string GetDefaultFolderName(string bundleName)
        {
            switch (bundleName?.Trim())
            {
                case "font_zh-cn":
                case "font_zh_cn":
                    return "ChineseSimplified";
                case "font_zh-tw":
                case "font_zh_tw":
                    return "ChineseTraditional";
                case "font_ja":
                    return "Japanese";
                case "font_ko":
                    return "Korean";
                case "font_th":
                    return "Thai";
                default:
                    return null;
            }
        }

        private static void EnsureFolder(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath)) return;
            if (AssetDatabase.IsValidFolder(projectRelativePath)) return;

            Directory.CreateDirectory(projectRelativePath);
            AssetDatabase.Refresh();
        }
    }
}
#endif
