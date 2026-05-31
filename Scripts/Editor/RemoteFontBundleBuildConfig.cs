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
            [Tooltip("Nome do bundle gerado (vira o nome do arquivo). Ex: font_zh-cn, font_ja-jp, font_ar.")]
            public string bundleName;

            [Tooltip("Pasta contendo os TMP_FontAsset desse idioma. Arraste a pasta do Project aqui.")]
            public DefaultAsset folder;

            [Tooltip("Fonte de origem (.ttf/.otf) para assar o TMP_FontAsset automaticamente. Pode ficar numa pasta Editor pra não ir pra build. Vazio = bake manual.")]
            public Font sourceFont;
        }

        [Tooltip("Pasta de saída dos bundles por jogo. Ex: AssetBundles/WebGL/Fonts")]
        public string outputFolder = "AssetBundles/WebGL/Fonts";

        [Tooltip("Pasta de saída dos bundles globais. Ex: AssetBundles/WebGL/GlobalFonts")]
        public string globalOutputFolder = "AssetBundles/WebGL/GlobalFonts";

        [Tooltip("Lista dinâmica de bundles. Adicione/remova quantos idiomas precisar.")]
        public List<Entry> entries = new();

        [Header("Auto-Bake (opcional)")]
        [Tooltip("Calcula automaticamente o maior point size que faz TODOS os glifos caberem em 1 página de atlas (sem perder glifo por espaço e com o menor bundle). Desligado = usa o Sampling Point Size fixo abaixo.")]
        public bool autoSizeToAtlas = true;

        [Tooltip("Lado do atlas SDF (quadrado), em pixels. Padrão 1024.")]
        public int atlasSize = 1024;

        [Tooltip("Sampling point size do glifo. Maior = mais nítido porém mais páginas de atlas (bundle maior). ~48–60 costuma caber em 1 página de 1024. Padrão 60.")]
        public int samplingPointSize = 60;

        [Tooltip("Padding do SDF em % do sampling point size. ~10% deixa as bordas suaves.")]
        [Range(1f, 25f)]
        public float paddingPercent = 10f;

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
                if (NormalizeLegacyBundleNames(config) | EnsureDefaultOutputFolders(config) | EnsureDefaultRemoteFontFolders(config))
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
            instance.entries.Add(CreateDefaultEntry("font_ja-jp", "Japanese"));
            instance.entries.Add(CreateDefaultEntry("font_ko-kr", "Korean"));
            instance.entries.Add(CreateDefaultEntry("font_th-th", "Thai"));

            AssetDatabase.CreateAsset(instance, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return instance;
        }

        private static bool EnsureDefaultOutputFolders(RemoteFontBundleBuildConfig config)
        {
            if (config == null)
                return false;

            var changed = false;
            if (string.IsNullOrWhiteSpace(config.outputFolder))
            {
                config.outputFolder = "AssetBundles/WebGL/Fonts";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(config.globalOutputFolder))
            {
                config.globalOutputFolder = "AssetBundles/WebGL/GlobalFonts";
                changed = true;
            }

            return changed;
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
                case "font_ja-jp":
                    return "Japanese";
                case "font_ko-kr":
                    return "Korean";
                case "font_th-th":
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
