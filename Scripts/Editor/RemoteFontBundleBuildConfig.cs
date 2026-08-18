#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using FineLocalization.Runtime;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Config editor-only que lista cada idioma → pasta usada por
    /// <see cref="BuildRemoteFontBundles"/>. Cada entry gera um AssetBundle com os
    /// TMP_FontAsset encontrados dentro de <see cref="Entry.folder"/>.
    ///
    /// As entries não precisam mais ser digitadas: o Hub as propõe a partir das colunas de idioma
    /// da planilha (<see cref="AddMissingEntries"/>). O único campo que continua humano é
    /// <see cref="Entry.sourceFont"/> — escolha tipográfica e de licença, não dá para derivar.
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

        [Tooltip("Pasta de saída dos bundles. Ex: AssetBundles/WebGL/Fonts")]
        public string outputFolder = DefaultOutputFolder;

        [Tooltip("Lista dinâmica de bundles. O Hub preenche a partir das colunas da planilha.")]
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

        private const string DefaultOutputFolder = "AssetBundles/WebGL/Fonts";

        private const string DefaultAssetPath =
            "Assets/FineLocalization/Editor/RemoteFontBundleBuildConfig.asset";

        private const string RemoteFontsFolder = "Assets/FineLocalization/RemoteFonts";

        /// <summary>
        /// Config do projeto, criada vazia se não existir. Vazia de propósito: as entries vêm das
        /// colunas que a planilha tem de verdade, não de uma lista de idiomas chutada.
        /// </summary>
        public static RemoteFontBundleBuildConfig GetOrCreate()
        {
            var guids = AssetDatabase.FindAssets("t:RemoteFontBundleBuildConfig");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var config = AssetDatabase.LoadAssetAtPath<RemoteFontBundleBuildConfig>(path);

                if (NormalizeLegacyBundleNames(config) | EnsureOutputFolder(config))
                {
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssetIfDirty(config);
                }

                return config;
            }

            EnsureFolder(Path.GetDirectoryName(DefaultAssetPath));

            var instance = CreateInstance<RemoteFontBundleBuildConfig>();
            AssetDatabase.CreateAsset(instance, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return instance;
        }

        /// <summary>
        /// Caminhos de todas as configs do projeto. Mais de uma é ambíguo — <see cref="GetOrCreate"/>
        /// pega a primeira que o AssetDatabase devolver, o que não é estável. O Hub avisa.
        /// </summary>
        public static string[] FindAllConfigPaths()
        {
            var guids = AssetDatabase.FindAssets("t:RemoteFontBundleBuildConfig");
            if (guids == null || guids.Length == 0)
                return new string[0];

            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);

            return paths;
        }

        /// <summary>Idioma que uma entry atende, derivado do nome do bundle.</summary>
        public static string LanguageOf(Entry entry)
        {
            return entry == null ? string.Empty : LanguageOf(entry.bundleName);
        }

        /// <summary>
        /// Idioma de um nome de bundle. Tira a extensão primeiro: sem isso "font_ar.ft" daria
        /// o idioma "ar.ft".
        /// </summary>
        public static string LanguageOf(string bundleName)
        {
            if (string.IsNullOrWhiteSpace(bundleName))
                return string.Empty;

            return LanguageCode.FromBundleName(Path.GetFileNameWithoutExtension(bundleName.Trim()));
        }

        /// <summary>
        /// Entry que atende <paramref name="language"/>, ou null. Usa
        /// <see cref="LanguageCode.IsExactOrSubtag"/> e não casamento por raiz: "zh-cn" e "zh-tw"
        /// são bundles diferentes e precisam de entries separadas.
        /// </summary>
        public Entry FindEntryForLanguage(string language)
        {
            if (entries == null || string.IsNullOrWhiteSpace(language))
                return null;

            foreach (var entry in entries)
            {
                if (LanguageCode.IsExactOrSubtag(LanguageOf(entry), language))
                    return entry;
            }

            return null;
        }

        /// <summary>
        /// Cria uma entry para cada idioma de <paramref name="languages"/> que ainda não tem uma,
        /// junto com a pasta <c>RemoteFonts/&lt;idioma&gt;</c>. Devolve os idiomas adicionados.
        ///
        /// O nome do bundle espelha a coluna da planilha (<c>font_&lt;coluna&gt;</c>), então o
        /// <c>characters_&lt;coluna&gt;.txt</c> gerado pelo sync sempre casa com ele.
        /// <c>sourceFont</c> fica null — é o que sobra para o dev preencher.
        /// </summary>
        public List<string> AddMissingEntries(IEnumerable<string> languages)
        {
            var added = new List<string>();
            if (languages == null)
                return added;

            entries ??= new List<Entry>();

            foreach (var raw in languages)
            {
                var language = LanguageCode.Normalize(raw);
                if (language.Length == 0 || FindEntryForLanguage(language) != null)
                    continue;

                // Um idioma pode aparecer duas vezes na fonte (duas planilhas com a mesma coluna).
                if (added.Contains(language))
                    continue;

                var folderPath = $"{RemoteFontsFolder}/{language}";
                EnsureFolder(folderPath);

                entries.Add(new Entry
                {
                    bundleName = "font_" + language,
                    folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath),
                    sourceFont = null
                });

                added.Add(language);
            }

            if (added.Count > 0)
            {
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssetIfDirty(this);
            }

            return added;
        }

        private static bool EnsureOutputFolder(RemoteFontBundleBuildConfig config)
        {
            if (config == null || !string.IsNullOrWhiteSpace(config.outputFolder))
                return false;

            config.outputFolder = DefaultOutputFolder;
            return true;
        }

        /// <summary>Nomes de bundle da v2 usavam '_' na região: font_zh_cn → font_zh-cn.</summary>
        private static bool NormalizeLegacyBundleNames(RemoteFontBundleBuildConfig config)
        {
            if (config == null || config.entries == null)
                return false;

            var changed = false;
            foreach (var entry in config.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.bundleName))
                    continue;

                var language = LanguageOf(entry.bundleName);
                if (language.Length == 0)
                    continue;

                var expected = "font_" + language;
                var current = entry.bundleName.Trim();
                if (string.Equals(current, expected, System.StringComparison.Ordinal))
                    continue;

                // Um nome fora da convenção gerava um arquivo que o runtime nunca pedia — 404 que
                // só aparecia em Play. Avisar, porque o arquivo no CDN muda de nome.
                Debug.LogWarning(
                    $"[FineLocalization] Bundle '{current}' renomeado para '{expected}' para bater com a " +
                    $"convenção. Rode Build Bundles e reenvie o arquivo com o nome novo."
                );

                entry.bundleName = expected;
                changed = true;
            }

            return changed;
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
