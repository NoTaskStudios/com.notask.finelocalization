using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FineLocalization.Runtime
{
    /// <summary>
    /// Índice dos bundles de fonte que existem de verdade, <b>gerado</b> pelo Bundle Builder.
    /// Não edite à mão.
    ///
    /// Antes da v3.2 cada idioma exigia uma linha digitada em "Remote Font Mappings" no
    /// inspector — prefixo e nome do TMP_FontAsset, um por um. Nada disso era informação nova:
    /// o prefixo sai do nome do bundle e o nome da fonte sai do asset assado. Pior, o runtime
    /// remontava a URL como <c>"font_" + prefixo + extensão</c>, então um bundle chamado fora da
    /// convenção gerava 404 que só aparecia em Play.
    ///
    /// Aqui o nome do arquivo é gravado <b>a partir do artefato construído</b>, então a URL e o
    /// arquivo no CDN não podem divergir.
    /// </summary>
    public class FontBundleManifest : ScriptableObject
    {
        /// <summary>Nome do asset em Resources, sem extensão.</summary>
        public const string AssetName = "FineLocalizationFontBundles";

        [Serializable]
        public class Entry
        {
            [Tooltip("Idioma que este bundle atende, derivado do nome do bundle.")]
            public string language;

            [Tooltip("Nome do arquivo construído, com extensão. É o último segmento da URL.")]
            public string bundleFileName;

            [Tooltip("Nome do TMP_FontAsset dentro do bundle. Desambigua bundle com mais de uma fonte.")]
            public string fontAssetName;
        }

        [Tooltip("Gerado pelo Bundle Builder. Editar à mão aqui não muda o que está no CDN.")]
        public List<Entry> entries = new();

        [Tooltip("Quando este manifesto foi gerado. Só para diagnóstico.")]
        public string generatedAt;

        private static FontBundleManifest _instance;
        private static bool _loadAttempted;
        private List<string> _languageBuffer;

        /// <summary>True quando há pelo menos um bundle declarado.</summary>
        public bool HasAnyBundle => entries != null && entries.Count > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            _loadAttempted = false;
        }

        /// <summary>
        /// Manifesto do projeto, ou null quando nenhum bundle foi construído ainda. Não cria nada
        /// e não loga: rodar sem fonte remota é uma configuração válida.
        /// </summary>
        public static FontBundleManifest Load()
        {
            if (_loadAttempted)
                return _instance;

            _loadAttempted = true;
            _instance = Resources.Load<FontBundleManifest>(AssetName);
            return _instance;
        }

        /// <summary>
        /// Entrada que melhor atende <paramref name="language"/>, ou null. Usa a mesma escada de
        /// candidatos que escolhe a coluna do CSV, então "cn" acha o bundle de "zh-cn" e "zh-hk"
        /// prefere o Tradicional ao Simplificado.
        /// </summary>
        public Entry Find(string language)
        {
            if (entries == null || entries.Count == 0)
                return null;

            _languageBuffer ??= new List<string>(entries.Count);
            _languageBuffer.Clear();

            for (int i = 0; i < entries.Count; i++)
                _languageBuffer.Add(entries[i] == null ? string.Empty : entries[i].language);

            var chosen = LanguageCode.SelectBest(language, _languageBuffer);
            if (chosen == null)
                return null;

            var index = _languageBuffer.IndexOf(chosen);
            return index >= 0 ? entries[index] : null;
        }

#if UNITY_EDITOR
        private const string FolderPath = "Assets/FineLocalization/Resources";
        private const string AssetPath = FolderPath + "/" + AssetName + ".asset";

        /// <summary>Caminho do asset gerado. Editor-only.</summary>
        public static string EditorAssetPath => AssetPath;

        /// <summary>Manifesto do projeto sem criar nada. Editor-only.</summary>
        public static FontBundleManifest FindExisting()
        {
            return AssetDatabase.LoadAssetAtPath<FontBundleManifest>(AssetPath);
        }

        /// <summary>
        /// Reescreve o manifesto com <paramref name="built"/>. Chamado pelo Bundle Builder depois
        /// de empacotar, nunca pelo runtime.
        /// </summary>
        public static FontBundleManifest Write(IEnumerable<Entry> built, string generatedAtUtc)
        {
            EnsureFolders();

            var manifest = FindExisting();
            var created = manifest == null;
            if (created)
                manifest = CreateInstance<FontBundleManifest>();

            manifest.entries.Clear();
            if (built != null)
            {
                foreach (var entry in built)
                {
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.bundleFileName))
                        manifest.entries.Add(entry);
                }
            }

            manifest.generatedAt = generatedAtUtc;
            manifest._languageBuffer = null;

            if (created)
                AssetDatabase.CreateAsset(manifest, AssetPath);

            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssetIfDirty(manifest);

            // O runtime pode já ter carregado um manifesto velho neste domínio.
            _instance = manifest;
            _loadAttempted = true;

            return manifest;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FineLocalization"))
                AssetDatabase.CreateFolder("Assets", "FineLocalization");

            if (!AssetDatabase.IsValidFolder(FolderPath))
                AssetDatabase.CreateFolder("Assets/FineLocalization", "Resources");
        }
#endif
    }
}
