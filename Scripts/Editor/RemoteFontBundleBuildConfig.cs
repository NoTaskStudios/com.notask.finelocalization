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
            [Tooltip("Nome do bundle gerado (vira o nome do arquivo). Ex: font_zh_cn, font_ja, font_ar.")]
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
                return AssetDatabase.LoadAssetAtPath<RemoteFontBundleBuildConfig>(path);
            }

            EnsureFolder(Path.GetDirectoryName(DefaultAssetPath));

            var instance = CreateInstance<RemoteFontBundleBuildConfig>();
            instance.entries.Add(new Entry { bundleName = "font_zh_cn" });
            instance.entries.Add(new Entry { bundleName = "font_zh_tw" });
            instance.entries.Add(new Entry { bundleName = "font_ja" });
            instance.entries.Add(new Entry { bundleName = "font_ko" });
            instance.entries.Add(new Entry { bundleName = "font_th" });

            AssetDatabase.CreateAsset(instance, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return instance;
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
