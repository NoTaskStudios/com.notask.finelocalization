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
            [Tooltip("Prefixo de idioma usado para detectar quando carregar esta fonte. Ex: zh, zh-tw, ja, ko, th")]
            public string languagePrefix;

            [Tooltip("Opcional. Se vazio, usa Base Bundle Url + languagePrefix.")]
            public string bundleUrlOverride;

            [Tooltip("Nome exato do TMP_FontAsset dentro do bundle")]
            public string fontAssetName;
        }

        [Header("Remote Font Bundles")]
        [Tooltip("URL base da pasta dos bundles. Ex: https://cdn.site.com/fonts/")]
        [SerializeField] private string baseBundleUrl;

        [Tooltip("Extensão adicionada depois do prefixo quando usar a URL base. Ex: .ft")]
        [SerializeField] private string bundleFileExtension = ".ft";

        [SerializeField] private List<RemoteFontBundleConfig> bundles = new();

        [Header("Fallback Target")]
        [Tooltip("Se marcado, adiciona nos fallbacks globais do TMP Settings.")]
        [SerializeField] private bool addToGlobalTmpFallbacks = true;

        [Tooltip("Fontes principais do projeto que devem receber o fallback remoto diretamente.")]
        [SerializeField] private List<TMP_FontAsset> mainFontAssets = new();

        [Tooltip("Também adiciona o fallback nas fontes usadas pelos TMP_Text ativos na cena.")]
        [SerializeField] private bool addToActiveTextFonts = true;

        [Tooltip("Aplica o fallback em TODAS as TMP_FontAsset carregadas em memória (inclui fontes dentro de prefabs ainda não instanciados — ex: popups). Mais robusto, custo desprezível porque normalmente há poucas fontes.")]
        [SerializeField] private bool addToAllLoadedFonts = true;

        [Tooltip("(Último recurso) Após o registro, força SetActive(false/true) em todos TMP_Text ativos. Garante refresh em casos teimosos, mas é caro em cenas grandes. Mantenha desligado a menos que veja warnings de glyph faltando.")]
        [SerializeField] private bool aggressiveRebuild = false;

        [Header("Localization Integration")]
        [Tooltip("Carrega o bundle de fonte automaticamente quando LocalizationManager.Language mudar.")]
        [SerializeField] private bool loadOnLocalizationChanged = true;

        [Tooltip("Tenta carregar a fonte do idioma atual quando este componente for habilitado.")]
        [SerializeField] private bool loadCurrentLanguageOnEnable = false;

        [Header("Script Detection (Optimization)")]
        [Tooltip("Quando marcado, idiomas de script Latin (en, pt, es, fr, de, ...) pulam o download — assume-se que as fontes do projeto já cobrem esses glyphs. Desligue se sua fonte padrão NÃO cobre acentos / caracteres latinos estendidos.")]
        [SerializeField] private bool skipDownloadForLatinScripts = true;

        [Tooltip("Prefixos extras que devem ser tratados como Latin (não baixar bundle). Use lowercase, sem region. Ex: 'tlh', 'eo'.")]
        [SerializeField] private List<string> extraLatinPrefixes = new();

        [Tooltip("Força o download do bundle para esses prefixos mesmo que sejam Latin. Use se sua fonte padrão é minimalista e não cobre acentos. Ex: 'tr', 'vi'.")]
        [SerializeField] private List<string> forceRemoteFontPrefixes = new();

        [Header("Manual Test")]
        [Tooltip("Idioma usado pelo menu de contexto de teste no Inspector.")]
        [SerializeField] private string testLanguage = "ja-jp";

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

        public void TestLanguage(string language)
        {
            StartCoroutine(EnsureFontForLanguage(language, success =>
                FineLocalizationLogger.Log(
                    () => $"[RemoteFontBundleLoader] Teste manual finalizado para '{language}'. Success: {success}"
                )
            ));
        }

        [ContextMenu("Fine Localization/Test Remote Font Language")]
        private void TestConfiguredLanguage()
        {
            TestLanguage(testLanguage);
        }

        [ContextMenu("Fine Localization/Dump Diagnostics")]
        private void DumpDiagnosticsFromContextMenu()
        {
            DumpDiagnostics();
        }

        /// <summary>
        /// Diagnóstico: imprime no console qual idioma está ativo, se ele é Latin,
        /// quais bundles foram baixados, e em quais TMP_FontAssets o fallback foi adicionado.
        /// Chame `loader.DumpDiagnostics()` quando aparecer um warning de glyph faltando para
        /// entender se foi o loader que registrou (ou não) o fallback.
        /// </summary>
        public void DumpDiagnostics()
        {
            var lang = LocalizationManager.Language ?? "<null>";
            var normalized = string.IsNullOrWhiteSpace(lang) ? "" : lang.Trim().ToLowerInvariant();
            var isLatin = !string.IsNullOrEmpty(normalized) && IsLatinScript(normalized);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== RemoteFontBundleLoader Diagnostics ===");
            sb.AppendLine($"Current language: '{lang}' (normalized: '{normalized}')");
            sb.AppendLine($"Is Latin script:  {isLatin}  →  " +
                          (isLatin ? "loader does NOTHING for this language" : "loader will try to load a bundle"));
            sb.AppendLine($"Loaded bundles:   {_loadedLanguages.Count} ({string.Join(", ", _loadedLanguages)})");
            sb.AppendLine($"Loading now:      {_loadingLanguages.Count} ({string.Join(", ", _loadingLanguages)})");

            var globals = TMPro.TMP_Settings.fallbackFontAssets;
            sb.AppendLine($"TMP_Settings.fallbackFontAssets: {(globals?.Count ?? 0)} entries");
            if (globals != null)
                for (int i = 0; i < globals.Count; i++)
                    sb.AppendLine($"  [{i}] {globals[i]?.name ?? "<null>"}");

            FineLocalizationLogger.Log(sb.ToString());
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
            // IMPORTANT: this path does NOT modify any TMP_FontAsset or TMP_Settings.
            if (IsLatinScript(normalizedLanguage))
            {
                FineLocalizationLogger.Log(
                    () => $"[RemoteFontBundleLoader] SKIP ({normalizedLanguage}): script Latin detectado — " +
                          "nenhum download, nenhum fallback registrado, nenhuma fonte tocada."
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
                    $"Result: {request.result} | HTTP: {request.responseCode} | " +
                    $"Downloaded: {request.downloadedBytes} bytes | " +
                    $"Error: {FormatRequestError(request)}"
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

        private static string FormatRequestError(UnityWebRequest request)
        {
            return string.IsNullOrWhiteSpace(request.error)
                ? "<none>"
                : request.error;
        }

        // Reusable buffer to avoid HashSet alloc per call.
        private static readonly HashSet<TMP_FontAsset> _seenFontsBuffer = new();

        /// <summary>
        /// Strategy:
        /// 1) Add the fallback to every TMP_FontAsset loaded in memory (including those in
        ///    prefabs that haven't been instantiated yet — e.g. popups). This is the cheap
        ///    catch-all: typical projects have 1–20 font assets vs hundreds of texts.
        /// 2) Single scan of scene TMP_Text instances to force a mesh rebuild so already-visible
        ///    text picks up the new fallback. Uses SetAllDirty + ForceMeshUpdate(ignoreActiveState:true)
        ///    — the conservative call known to work in all TMP versions.
        /// 3) Optional aggressive rebuild (SetActive toggle) only if the user opts in.
        /// </summary>
        private IEnumerator ApplyFallbackToSceneAndRebuild(TMP_FontAsset fontAsset)
        {
            yield return null;

            // 1) Register fallback on font assets ---------------------------------------
            _seenFontsBuffer.Clear();

            // 1a) All loaded TMP_FontAsset instances (covers prefabs / addressables that
            //     are loaded but not yet instantiated — fixes "popup with missing glyphs").
            if (addToAllLoadedFonts)
            {
                var allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                for (int i = 0; i < allFonts.Length; i++)
                {
                    var f = allFonts[i];
                    if (f == null || f == fontAsset) continue;
                    if (_seenFontsBuffer.Add(f))
                        AddFallbackToFont(f, fontAsset);
                }
            }

#if UNITY_2022_2_OR_NEWER
            var texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var texts = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
#endif

            // 1b) Scene text fonts — also covered if addToAllLoadedFonts ran above
            //     (the HashSet dedupes), but kept here for cases where the master toggle is off.
            if (addToActiveTextFonts)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null || t.font == null) continue;
                    if (_seenFontsBuffer.Add(t.font))
                        AddFallbackToFont(t.font, fontAsset);
                }
            }

            _seenFontsBuffer.Clear();

            // 2) Force rebuild of visible texts ---------------------------------------
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;

                t.SetAllDirty();
                t.havePropertiesChanged = true;
                t.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
            }

            // 3) Optional aggressive pass --------------------------------------------
            if (aggressiveRebuild)
            {
                yield return null;
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i];
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    var go = t.gameObject;
                    go.SetActive(false);
                    go.SetActive(true);
                }
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

            return CombineBundleUrl(baseBundleUrl, "font_" + prefix + bundleFileExtension);
        }

        private static string CombineBundleUrl(string baseUrl, string fileName)
        {
            var trimmedBaseUrl = baseUrl.Trim();
            if (trimmedBaseUrl.EndsWith("/", StringComparison.Ordinal))
                return trimmedBaseUrl + fileName;

            return trimmedBaseUrl + "/" + fileName;
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
