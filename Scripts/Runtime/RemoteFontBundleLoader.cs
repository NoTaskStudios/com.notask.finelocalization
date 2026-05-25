using System;
using System.Collections;
using System.Collections.Generic;
using FineLocalization.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace FineLocalization.Scripts.Runtime
{
    public class RemoteFontBundleLoader : MonoBehaviour
    {
        [Serializable]
        private class RemoteFontBundleConfig
        {
            [Tooltip("Ex: zh, ja, ko, th")]
            public string languagePrefix;

            [Tooltip("Opcional. Se vazio, usa Base Bundle Url + languagePrefix.")]
            public string bundleUrlOverride;

            [Tooltip("Nome exato do TMP_FontAsset dentro do bundle")]
            public string fontAssetName;
        }

        [Header("Remote Font Bundles")]
        [Tooltip("URL base dos bundles. Ex: https://cdn.site.com/fonts/ ou https://cdn.site.com/fonts/font_")]
        [SerializeField] private string baseBundleUrl;

        [Tooltip("Extensão adicionada depois do prefixo quando usar a URL base. Ex: .bundle")]
        [SerializeField] private string bundleFileExtension = ".bundle";

        [SerializeField] private List<RemoteFontBundleConfig> bundles = new();

        [Header("Fallback Target")]
        [Tooltip("Se marcado, adiciona nos fallbacks globais do TMP Settings.")]
        [SerializeField] private bool addToGlobalTmpFallbacks = true;

        [Tooltip("Fontes principais do projeto que devem receber o fallback remoto diretamente.")]
        [SerializeField] private List<TMP_FontAsset> mainFontAssets = new();

        [Tooltip("Também adiciona o fallback nas fontes usadas pelos TMP_Text ativos na cena.")]
        [SerializeField] private bool addToActiveTextFonts = true;

        [Header("Localization Integration")]
        [Tooltip("Carrega o bundle de fonte automaticamente quando LocalizationManager.Language mudar.")]
        [SerializeField] private bool loadOnLocalizationChanged = true;

        [Tooltip("Tenta carregar a fonte do idioma atual quando este componente for habilitado.")]
        [SerializeField] private bool loadCurrentLanguageOnEnable = true;

        [Header("Script Detection (Optimization)")]
        [Tooltip("Quando marcado, idiomas de script Latin (en, pt, es, fr, de, ...) pulam o download — assume-se que as fontes do projeto já cobrem esses glyphs. Desligue se sua fonte padrão NÃO cobre acentos / caracteres latinos estendidos.")]
        [SerializeField] private bool skipDownloadForLatinScripts = true;

        [Tooltip("Prefixos extras que devem ser tratados como Latin (não baixar bundle). Use lowercase, sem region. Ex: 'tlh', 'eo'.")]
        [SerializeField] private List<string> extraLatinPrefixes = new();

        [Tooltip("Força o download do bundle para esses prefixos mesmo que sejam Latin. Use se sua fonte padrão é minimalista e não cobre acentos. Ex: 'tr', 'vi'.")]
        [SerializeField] private List<string> forceRemoteFontPrefixes = new();

        /// <summary>
        /// ISO 639-1 codes that are written in Latin script. These are skipped by default
        /// because typical Unity project fonts already cover Latin + Latin Extended.
        /// Non-Latin scripts (CJK, Arabic, Hebrew, Thai, Devanagari, Cyrillic, Greek, etc.)
        /// fall through to the download path.
        /// </summary>
        private static readonly HashSet<string> DefaultLatinScriptPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            // Western European
            "en", "es", "pt", "fr", "de", "it", "nl", "ca", "gl", "eu", "oc", "rm",
            // Nordic
            "sv", "no", "nb", "nn", "da", "fi", "is", "fo",
            // Central / Eastern European (Latin script)
            "pl", "cs", "sk", "ro", "hu", "sl", "hr", "bs", "sq", "lt", "lv", "et",
            // Turkic / others using Latin
            "tr", "az", "uz", "tk", "kk",
            // Southeast Asian (Latin alphabet)
            "id", "ms", "vi", "tl", "fil",
            // Celtic / British Isles
            "ga", "cy", "gd", "br", "kw",
            // African (Latin script)
            "sw", "af", "zu", "xh", "yo", "ig", "ha", "so", "rw", "mg", "st", "sn", "ny",
            // Misc Latin
            "lb", "fy", "mt", "ku", "ht", "qu", "gn"
        };

        private readonly HashSet<string> _loadedLanguages = new();
        private readonly HashSet<string> _loadingLanguages = new();

        private void OnEnable()
        {
            if (loadOnLocalizationChanged)
                LocalizationManager.OnLocalizationChanged += EnsureCurrentLanguageFont;

            if (loadCurrentLanguageOnEnable)
                EnsureCurrentLanguageFont();
        }

        private void OnDisable()
        {
            if (loadOnLocalizationChanged)
                LocalizationManager.OnLocalizationChanged -= EnsureCurrentLanguageFont;
        }

        public void EnsureCurrentLanguageFont()
        {
            StartCoroutine(EnsureFontForLanguage(LocalizationManager.Language));
        }

        public IEnumerator EnsureFontForLanguage(
            string language,
            Action<bool> onComplete = null
        )
        {
            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] >>> INICIANDO EnsureFontForLanguage: {language}");

            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Solicitado idioma: {language}");

            if (string.IsNullOrWhiteSpace(language))
            {
                FineLocalizationLogger.LogWarning("[RemoteFontBundleLoader] Idioma vazio. Nenhum bundle será carregado.");
                onComplete?.Invoke(true);
                yield break;
            }

            var normalizedLanguage = language.Trim().ToLowerInvariant();

            // Optimization: if the language uses Latin script (en, pt, es, fr, ...) we
            // assume the project's bundled fonts already cover its glyphs. No network call,
            // no AssetBundle download, no TMP rebuild — instant return.
            if (IsLatinScript(normalizedLanguage))
            {
                FineLocalizationLogger.Log(
                    () => $"[RemoteFontBundleLoader] '{normalizedLanguage}' usa script Latin — pulando download (fontes do projeto já cobrem)."
                );
                onComplete?.Invoke(true);
                yield break;
            }

            var config = FindConfig(normalizedLanguage);

            if (config == null)
            {
                FineLocalizationLogger.Log(
                    () => $"[RemoteFontBundleLoader] Nenhum bundle configurado para '{normalizedLanguage}'."
                );

                onComplete?.Invoke(true);
                yield break;
            }

            var prefix = config.languagePrefix.Trim().ToLowerInvariant();
            var bundleUrl = GetBundleUrl(config, prefix);

            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] Config encontrada. " +
                    $"Idioma: {normalizedLanguage} | " +
                    $"Prefix: {prefix} | " +
                    $"URL: {bundleUrl} | " +
                    $"FontAsset: {config.fontAssetName}"
            );

            if (_loadedLanguages.Contains(prefix))
            {
                FineLocalizationLogger.Log(
                    () => $"[RemoteFontBundleLoader] Fonte de '{prefix}' já estava carregada."
                );

                onComplete?.Invoke(true);
                yield break;
            }

            if (!_loadingLanguages.Add(prefix))
            {
                FineLocalizationLogger.Log(
                    () => $"[RemoteFontBundleLoader] Fonte de '{prefix}' já está carregando."
                );

                onComplete?.Invoke(true);
                yield break;
            }

            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] Iniciando download do AssetBundle: {bundleUrl}"
            );

            if (string.IsNullOrWhiteSpace(bundleUrl))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[RemoteFontBundleLoader] URL do bundle vazia para '{prefix}'."
                );

                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            using var request = UnityWebRequestAssetBundle.GetAssetBundle(bundleUrl);

            yield return request.SendWebRequest();

            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] Requisição finalizada. " +
                    $"Result: {request.result} | Erro: {request.error}"
            );

            if (request.result != UnityWebRequest.Result.Success)
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[RemoteFontBundleLoader] Falha ao baixar bundle de fonte '{language}'. " +
                        $"Erro: {request.error}"
                );

                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            var bundle = DownloadHandlerAssetBundle.GetContent(request);

            if (bundle == null)
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[RemoteFontBundleLoader] Bundle retornou null para '{language}'."
                );

                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] AssetBundle carregado com sucesso para '{language}'."
            );

            var fontLoadRequest =
                bundle.LoadAssetAsync<TMP_FontAsset>(config.fontAssetName);

            yield return fontLoadRequest;

            var fontAsset = fontLoadRequest.asset as TMP_FontAsset;

            if (fontAsset == null)
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[RemoteFontBundleLoader] TMP_FontAsset '{config.fontAssetName}' não encontrado no bundle."
                );

                bundle.Unload(false);
                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] TMP_FontAsset encontrado: {fontAsset.name} | " +
                    $"Character Count: {fontAsset.characterTable?.Count ?? 0} | " +
                    $"Glyph Count: {fontAsset.glyphTable?.Count ?? 0}"
            );
            RegisterFallback(fontAsset);
            yield return StartCoroutine(ApplyFallbackToSceneAndRebuild(fontAsset));

            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] Fonte registrada como fallback: {fontAsset.name}"
            );

            _loadedLanguages.Add(prefix);
            _loadingLanguages.Remove(prefix);

            bundle.Unload(false);

            onComplete?.Invoke(true);
        }
        // Reusable buffer to avoid HashSet alloc per call.
        private static readonly HashSet<TMP_FontAsset> _seenFontsBuffer = new();

        /// <summary>
        /// Single scene scan: dedupes fonts (so each font's fallback table is touched once),
        /// then forces mesh update on each active text. Avoids the triple
        /// Resources.FindObjectsOfTypeAll scan and the SetActive(false/true) toggle which
        /// forced a full layout rebuild on every TMP_Text — both extremely expensive on WebGL/2GB devices.
        /// </summary>
        private IEnumerator ApplyFallbackToSceneAndRebuild(TMP_FontAsset fontAsset)
        {
            yield return null;

#if UNITY_2022_2_OR_NEWER
            var texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var texts = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
#endif

            if (addToActiveTextFonts)
            {
                _seenFontsBuffer.Clear();
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null || t.font == null) continue;
                    if (_seenFontsBuffer.Add(t.font))
                        AddFallbackToFont(t.font, fontAsset);
                }
                _seenFontsBuffer.Clear();
            }

            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;

                t.havePropertiesChanged = true;
                t.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: true);
            }
        }
        private RemoteFontBundleConfig FindConfig(string language)
        {
            foreach (var config in bundles)
            {
                if (string.IsNullOrWhiteSpace(config.languagePrefix))
                    continue;

                var prefix = config.languagePrefix.Trim().ToLowerInvariant();

                if (language == prefix || language.StartsWith(prefix + "-"))
                    return config;
            }

            return null;
        }

        /// <summary>
        /// Returns true if the given (already lowercased) language tag is written in Latin
        /// script and therefore does not require a remote font download. Honors the
        /// <see cref="skipDownloadForLatinScripts"/> master toggle, <see cref="extraLatinPrefixes"/>,
        /// and the explicit <see cref="forceRemoteFontPrefixes"/> override.
        /// </summary>
        private bool IsLatinScript(string normalizedLanguage)
        {
            if (!skipDownloadForLatinScripts) return false;
            if (string.IsNullOrEmpty(normalizedLanguage)) return false;

            // Take only the root prefix ("en-us" → "en", "pt-br" → "pt").
            var dashIndex = normalizedLanguage.IndexOf('-');
            var rootPrefix = dashIndex >= 0
                ? normalizedLanguage.Substring(0, dashIndex)
                : normalizedLanguage;

            // Explicit override wins: user can force a download for languages that ARE Latin
            // but whose project font is too minimalistic (e.g. lacks diacritics).
            if (MatchesAnyPrefix(forceRemoteFontPrefixes, rootPrefix, normalizedLanguage))
                return false;

            if (DefaultLatinScriptPrefixes.Contains(rootPrefix))
                return true;

            if (MatchesAnyPrefix(extraLatinPrefixes, rootPrefix, normalizedLanguage))
                return true;

            return false;
        }

        private static bool MatchesAnyPrefix(List<string> list, string rootPrefix, string normalizedLanguage)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i]?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(item)) continue;
                if (item == rootPrefix || item == normalizedLanguage)
                    return true;
            }
            return false;
        }

        private string GetBundleUrl(RemoteFontBundleConfig config, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(config.bundleUrlOverride))
                return config.bundleUrlOverride.Trim();

            if (string.IsNullOrWhiteSpace(baseBundleUrl))
                return string.Empty;

            return baseBundleUrl.Trim() + prefix + bundleFileExtension;
        }

        private void RegisterFallback(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null) return;

            if (addToGlobalTmpFallbacks)
            {
                var globalFallbacks = TMP_Settings.fallbackFontAssets;

                if (globalFallbacks != null && !globalFallbacks.Contains(fontAsset))
                    globalFallbacks.Add(fontAsset);
            }

            foreach (var mainFont in mainFontAssets)
                AddFallbackToFont(mainFont, fontAsset);

            // Scene-text fallback registration moved to ApplyFallbackToSceneAndRebuild
            // (single dedup'd scan instead of one AddFallbackToFont call per text).

            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
        }

        private static void AddFallbackToFont(TMP_FontAsset targetFont, TMP_FontAsset fallbackFont)
        {
            if (targetFont == null || fallbackFont == null || targetFont == fallbackFont)
                return;

            targetFont.fallbackFontAssetTable ??= new List<TMP_FontAsset>();

            if (!targetFont.fallbackFontAssetTable.Contains(fallbackFont))
                targetFont.fallbackFontAssetTable.Add(fallbackFont);

            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, targetFont);
        }
    }
}
