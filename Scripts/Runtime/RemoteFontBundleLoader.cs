using System;
using System.Collections;
using System.Collections.Generic;
using FineLocalization.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace FineLocalization.Scripts.Runtime
{
    /// <summary>
    /// Baixa fontes por idioma como AssetBundle e as registra como fallback do TextMeshPro.
    ///
    /// Só idiomas fora do script Latin passam pela rede — as fontes embutidas do projeto já
    /// cobrem Latin e Latin Extended. Isso mantém o build pequeno: os glifos de CJK, tailandês,
    /// árabe e afins só chegam ao jogador que realmente escolhe esses idiomas.
    ///
    /// O trabalho pesado fica em <see cref="TmpFontRepair"/> (material e métricas SDF) e
    /// <see cref="TmpFallbackRegistry"/> (tabelas de fallback). Aqui ficam configuração,
    /// download e o rebuild dos textos.
    /// </summary>
    [DisallowMultipleComponent]
    public class RemoteFontBundleLoader : MonoBehaviour
    {
        [Serializable]
        private class RemoteFontBundleConfig
        {
            [Tooltip("Prefixo de idioma que dispara esta fonte. Ex: zh, zh-tw, ja, ko, th")]
            public string languagePrefix;

            [Tooltip("Nome exato do TMP_FontAsset dentro do bundle. Ex: NotoSansJP-used")]
            public string fontAssetName;
        }

        [Header("Origem dos bundles")]
        [Tooltip("URL base da pasta dos bundles. Ex: https://cdn.site.com/languages/")]
        [SerializeField] private string baseBundleUrl;

        [Tooltip("Ligado = fontes globais, mesma pasta para todos os jogos (bundles maiores). Desligado = fontes por jogo em /<gameId>/ (otimizadas).")]
        [SerializeField] private bool useGlobalLanguage;

        [Tooltip("Segmento do jogo na URL. Vazio = Product Name do Player Settings, minúsculo e sem espaços. Ignorado com Global Language ligado.")]
        [SerializeField] private string gameId;

        [Tooltip("Extensão dos bundles gerados pelo Bundle Builder.")]
        [SerializeField] private string bundleFileExtension = ".ft";

        [SerializeField] private List<RemoteFontBundleConfig> bundles = new();

        [Header("Alvos do fallback")]
        [Tooltip("Fontes base do projeto que recebem a fonte remota como fallback prioritário. Coloque aqui a fonte principal da UI, não as fontes CJK.")]
        [SerializeField] private List<TMP_FontAsset> mainFontAssets = new();

        [Tooltip("Fallback permanente de baixa prioridade para glifos soltos que a fonte principal não tem (₴, ₹). Nunca removido ao trocar idioma. Deixe vazio se a fonte principal já cobre.")]
        [SerializeField] private List<TMP_FontAsset> coverageFallbackFontAssets = new();

        [Header("Avançado")]
        [Tooltip("TMP_Text reconstruídos por frame. Menor = menos travada em aparelho fraco; maior = troca de idioma mais rápida.")]
        [Min(1)]
        [SerializeField] private int rebuildBatchSize = 24;

        [Tooltip("Prefixos extras a tratar como Latin — não baixam bundle. Use minúsculo.")]
        [SerializeField] private List<string> extraLatinPrefixes = new();

        [Tooltip("Força bundle remoto mesmo para idiomas Latin. Use quando a fonte base não tem acentuação completa. Ex: vi, tr")]
        [SerializeField] private List<string> forceRemoteFontPrefixes = new();

        private readonly Dictionary<string, TMP_FontAsset> _fonts = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
        // Só existe para segurar referências: sem isso a Unity descarrega o material e o atlas
        // vindos do bundle assim que ele é fechado, e a fonte remota vira quadrados vazios.
        private readonly HashSet<UnityEngine.Object> _keepAlive = new();
        private bool _ignoreNextLocalizationChanged;

        /// <summary>True quando uma fonte remota está carregada e instalada.</summary>
        public static bool IsReady { get; private set; }

        /// <summary>Idioma da última fonte remota processada.</summary>
        public static string LastLanguage { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            IsReady = false;
            LastLanguage = null;
            _legacyComplete = _ => { };
            _legacyCompleteWithLanguage = (_, _) => { };
        }

        private void OnEnable()
        {
            TmpFontRepair.MaterialTemplates = mainFontAssets;
            TmpFontRepair.IsRemoteFont = IsManagedFont;
            TmpFallbackRegistry.IsManagedRemote = IsManagedFont;
            TmpFallbackRegistry.InstallCoverage(coverageFallbackFontAssets, mainFontAssets);

            LocalizationManager.OnLocalizationChanged += HandleLocalizationChanged;
            StartCoroutine(EnsureOnEnable());
        }

        private void OnDisable()
        {
            LocalizationManager.OnLocalizationChanged -= HandleLocalizationChanged;
        }

        // ------------------------------------------------------------------ API

        /// <summary>
        /// True quando o idioma precisa de uma fonte que o projeto não embute — ou seja, não é
        /// script Latin (considerando as listas de override do inspector).
        /// </summary>
        public bool NeedsRemoteFont(string language)
        {
            return !string.IsNullOrEmpty(LanguageCode.Normalize(language)) &&
                   !LanguageCode.IsLatinScript(language, extraLatinPrefixes, forceRemoteFontPrefixes);
        }

        /// <summary>True quando existe um bundle configurado capaz de atender o idioma.</summary>
        public bool CanServeLanguage(string language)
        {
            return NeedsRemoteFont(language) && FindConfig(language) != null;
        }

        /// <summary>True quando a fonte do idioma já está baixada e renderizável.</summary>
        public bool IsServing(string language)
        {
            var config = FindConfig(language);
            return config != null &&
                   _fonts.TryGetValue(LanguageCode.Normalize(config.languagePrefix), out var font) &&
                   TmpFontRepair.IsUsable(font);
        }

        /// <summary>
        /// Garante que a fonte do idioma esteja baixada e instalada como fallback.
        /// O callback recebe false quando o idioma precisava de fonte remota e ela não pôde
        /// ser obtida — nesse caso quem chamou deve cair para um idioma Latin.
        ///
        /// Não reconstrói os textos: quem chama deve trocar as strings primeiro e só então
        /// chamar <see cref="RebuildCurrentTexts"/>, para o mesh ser refeito uma única vez.
        /// </summary>
        public IEnumerator EnsureFontForLanguage(string language, Action<bool> onComplete = null)
        {
            var normalized = LanguageCode.Normalize(language);

            if (string.IsNullOrEmpty(normalized))
            {
                onComplete?.Invoke(false);
                yield break;
            }

            if (!NeedsRemoteFont(normalized))
            {
                // Idioma Latin: as fontes do projeto bastam. Tira a fonte remota do idioma
                // anterior para ela não continuar resolvendo glifos indevidamente.
                TmpFallbackRegistry.RemoveManagedRemote(null, mainFontAssets, FindSceneTexts());
                Complete(normalized, true, onComplete, notify: false);
                yield break;
            }

            var config = FindConfig(normalized);
            if (config == null)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Nenhum bundle de fonte configurado para '{normalized}'.");
                Complete(normalized, false, onComplete, notify: false);
                yield break;
            }

            var prefix = LanguageCode.Normalize(config.languagePrefix);

            if (_fonts.TryGetValue(prefix, out var cached))
            {
                if (TmpFallbackRegistry.InstallRemote(cached, mainFontAssets, FindSceneTexts()))
                {
                    Complete(normalized, true, onComplete, notify: false);
                    yield break;
                }

                // A fonte em cache ficou inutilizável (asset descarregado entre cenas). Descarta
                // para que a próxima tentativa volte a baixar em vez de falhar para sempre.
                _fonts.Remove(prefix);
            }

            if (!_loading.Add(prefix))
            {
                while (_loading.Contains(prefix))
                    yield return null;

                Complete(normalized, _fonts.ContainsKey(prefix), onComplete, notify: false);
                yield break;
            }

            var success = false;
            yield return DownloadAndInstall(prefix, config.fontAssetName, ok => success = ok);
            _loading.Remove(prefix);

            Complete(normalized, success, onComplete, notify: true);
        }

        /// <summary>Reconstrói o mesh de todos os TMP_Text ativos, em lotes, sem travar o frame.</summary>
        public IEnumerator RebuildCurrentTexts()
        {
            var texts = FindSceneTexts();

            // O TMP percorre a lista global de fallbacks para todo caractere ausente na fonte
            // principal. Uma entrada quebrada ali derruba o rebuild inteiro — limpa antes.
            TmpFallbackRegistry.SanitizeGlobals();
            TmpFontRepair.ClearFallbackMaterialCache();

            var remote = CurrentRemoteFont();
            var batch = Mathf.Max(1, rebuildBatchSize);
            var rebuilt = 0;
            var skipped = 0;

            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null || text.font == null || !text.gameObject.activeInHierarchy)
                {
                    skipped++;
                    continue;
                }

                if (!TmpFallbackRegistry.ValidateTree(text.font))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    text.SetAllDirty();
                    text.UpdateMeshPadding();
                    text.havePropertiesChanged = true;
                    text.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: true);

                    TmpFontRepair.NormalizeTextMaterials(text, remote);

                    text.SetMaterialDirty();
                    text.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: false);
                    rebuilt++;
                }
                catch (Exception ex)
                {
                    // Um TMP_Text quebrado (material nulo, objeto destruído no meio do frame)
                    // nunca pode abortar o rebuild dos outros.
                    skipped++;
                    FineLocalizationLogger.LogWarning(
                        () => $"[FineLocalization] Rebuild ignorado em '{text.name}': {ex.GetType().Name}: {ex.Message}"
                    );
                }

                if (rebuilt > 0 && rebuilt % batch == 0)
                    yield return null;
            }

            FineLocalizationLogger.Log(() => $"[FineLocalization] Rebuild de textos: {rebuilt} reconstruídos, {skipped} ignorados.");
        }

        /// <summary>
        /// Faz o loader ignorar o próximo <c>OnLocalizationChanged</c>. Usado pelo
        /// <see cref="RuntimeLocaleDownloader"/>, que já preparou a fonte antes de notificar.
        /// </summary>
        public void IgnoreNextLocalizationChanged() => _ignoreNextLocalizationChanged = true;

        /// <summary>Segmento de URL por jogo derivado do Product Name, quando gameId está vazio.</summary>
        public static string GetDefaultGameId()
        {
            var productName = Application.productName;
            return string.IsNullOrWhiteSpace(productName)
                ? string.Empty
                : productName.Trim().ToLowerInvariant().Replace(" ", "");
        }

        [ContextMenu("Fine Localization/Recarregar fonte do idioma atual")]
        private void ReloadCurrentLanguageFont()
        {
            if (Application.isPlaying)
                StartCoroutine(EnsureFontForLanguage(LocalizationManager.Language));
        }

        // -------------------------------------------------------------- Download

        private IEnumerator DownloadAndInstall(string prefix, string fontAssetName, Action<bool> onComplete)
        {
            var url = BuildBundleUrl(prefix);
            if (string.IsNullOrWhiteSpace(url))
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Base Bundle URL vazia — não é possível baixar a fonte de '{prefix}'.");
                onComplete(false);
                yield break;
            }

            FineLocalizationLogger.Log(() => $"[FineLocalization] Baixando fonte de '{prefix}': {url}");

            using var request = UnityWebRequestAssetBundle.GetAssetBundle(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FineLocalization] Falha ao baixar a fonte de '{prefix}'. HTTP {request.responseCode}, erro '{request.error}', URL {url}");
                onComplete(false);
                yield break;
            }

            var bundle = DownloadHandlerAssetBundle.GetContent(request);
            if (bundle == null)
            {
                Debug.LogError($"[FineLocalization] O bundle de fonte de '{prefix}' baixou mas não pôde ser aberto. URL {url}");
                onComplete(false);
                yield break;
            }

            TMP_FontAsset font = null;

            if (!string.IsNullOrWhiteSpace(fontAssetName))
            {
                var byName = bundle.LoadAssetAsync<TMP_FontAsset>(fontAssetName);
                yield return byName;
                font = byName.asset as TMP_FontAsset;
            }

            // Carrega tudo mesmo já tendo a fonte: o material e o atlas vêm como assets
            // separados e precisam ficar vivos enquanto a fonte estiver instalada.
            var all = bundle.LoadAllAssetsAsync();
            yield return all;
            TmpFontRepair.KeepAlive(all.allAssets, _keepAlive);

            font ??= FirstFontAsset(all.allAssets, fontAssetName);
            var bundleMaterial = BestMaterial(all.allAssets, font);

            if (font == null)
            {
                Debug.LogError(
                    $"[FineLocalization] Nenhum TMP_FontAsset no bundle de '{prefix}'. Esperado '{fontAssetName}'. " +
                    $"Assets no bundle: {string.Join(", ", bundle.GetAllAssetNames())}"
                );
                bundle.Unload(false);
                onComplete(false);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(fontAssetName) &&
                !font.name.Equals(fontAssetName, StringComparison.OrdinalIgnoreCase))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] Bundle de '{prefix}' traz '{font.name}', mas o configurado é '{fontAssetName}'. Usando o que veio."
                );
            }

            // Registra antes de reparar: TmpFontRepair.IsRemoteFont consulta _fonts para saber
            // que pode recriar o material e reescrever as métricas SDF desta fonte.
            _fonts[prefix] = font;

            TmpFontRepair.Repair(font, bundleMaterial);
            TmpFontRepair.ReadDefinition(font);
            TmpFontRepair.KeepAtlasesAlive(font);
            font.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var installed = TmpFallbackRegistry.InstallRemote(font, mainFontAssets, FindSceneTexts());
            if (!installed)
            {
                _fonts.Remove(prefix);
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Fonte de '{prefix}' inutilizável após reparo: {TmpFontRepair.Describe(font)}");
            }
            else
            {
                FineLocalizationLogger.Log(() => $"[FineLocalization] Fonte de '{prefix}' instalada: {TmpFontRepair.Describe(font)}");
            }

            // Unload(false) libera o container do bundle mantendo os assets já carregados.
            bundle.Unload(false);
            onComplete(installed);
        }

        // ------------------------------------------------------------- Resolução

        private RemoteFontBundleConfig FindConfig(string language)
        {
            RemoteFontBundleConfig best = null;
            var bestScore = -1;

            for (int i = 0; i < bundles.Count; i++)
            {
                var config = bundles[i];
                if (config == null || string.IsNullOrWhiteSpace(config.languagePrefix))
                    continue;

                var score = LanguageCode.MatchScore(language, config.languagePrefix);
                if (score > bestScore)
                {
                    best = config;
                    bestScore = score;
                }
            }

            return bestScore >= 0 ? best : null;
        }

        private string BuildBundleUrl(string prefix)
        {
            if (string.IsNullOrWhiteSpace(baseBundleUrl))
                return string.Empty;

            var url = baseBundleUrl.Trim();

            if (!useGlobalLanguage)
            {
                var segment = string.IsNullOrWhiteSpace(gameId) ? GetDefaultGameId() : gameId.Trim();
                if (!string.IsNullOrEmpty(segment))
                    url = Combine(url, segment);
            }

            return Combine(url, "font_" + prefix + bundleFileExtension);
        }

        private static string Combine(string baseUrl, string segment)
        {
            return baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl + segment : baseUrl + "/" + segment;
        }

        /// <summary>Fonte remota do idioma atualmente aplicado, ou null.</summary>
        private TMP_FontAsset CurrentRemoteFont()
        {
            var config = FindConfig(LocalizationManager.Language);
            return config != null && _fonts.TryGetValue(LanguageCode.Normalize(config.languagePrefix), out var font)
                ? font
                : null;
        }

        private bool IsManagedFont(TMP_FontAsset font)
        {
            if (font == null)
                return false;

            foreach (var loaded in _fonts.Values)
            {
                if (loaded == font)
                    return true;
            }

            for (int i = 0; i < bundles.Count; i++)
            {
                var configured = bundles[i]?.fontAssetName;
                if (!string.IsNullOrWhiteSpace(configured) &&
                    font.name.Equals(configured.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void HandleLocalizationChanged()
        {
            // O RuntimeLocaleDownloader já preparou a fonte antes de notificar; reagir aqui
            // dispararia um segundo ciclo de download e rebuild para o mesmo idioma.
            if (_ignoreNextLocalizationChanged)
            {
                _ignoreNextLocalizationChanged = false;
                return;
            }

            StartCoroutine(EnsureAndRebuild(LocalizationManager.Language));
        }

        private IEnumerator EnsureOnEnable()
        {
            yield return null;

            // Com um RuntimeLocaleDownloader na cena, ele é o dono do pipeline e chama o loader
            // na ordem certa (fonte antes dos textos). Sem ele, o loader se vira sozinho.
            if (FindDownloader() != null)
                yield break;

            yield return EnsureAndRebuild(LocalizationManager.Language);
        }

        /// <summary>Fluxo autônomo, para quando não há um RuntimeLocaleDownloader comandando.</summary>
        private IEnumerator EnsureAndRebuild(string language)
        {
            var ready = false;
            yield return EnsureFontForLanguage(language, ok => ready = ok);

            if (ready)
                yield return RebuildCurrentTexts();
        }

        private void Complete(string language, bool success, Action<bool> onComplete, bool notify)
        {
            LastLanguage = language;
            IsReady = success;

            if (notify)
            {
                _legacyComplete?.Invoke(success);
                _legacyCompleteWithLanguage?.Invoke(language, success);
            }

            onComplete?.Invoke(success);
        }

        private static TMP_Text[] FindSceneTexts()
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
#endif
        }

        private static RuntimeLocaleDownloader FindDownloader()
        {
#if UNITY_2022_2_OR_NEWER
            return FindFirstObjectByType<RuntimeLocaleDownloader>(FindObjectsInactive.Include);
#else
            return FindObjectOfType<RuntimeLocaleDownloader>(true);
#endif
        }

        private static TMP_FontAsset FirstFontAsset(UnityEngine.Object[] assets, string preferredName)
        {
            TMP_FontAsset first = null;

            for (int i = 0; assets != null && i < assets.Length; i++)
            {
                if (assets[i] is not TMP_FontAsset font)
                    continue;

                first ??= font;

                if (!string.IsNullOrWhiteSpace(preferredName) &&
                    font.name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                    return font;
            }

            return first;
        }

        private static Material BestMaterial(UnityEngine.Object[] assets, TMP_FontAsset font)
        {
            Material first = null;
            Material named = null;

            for (int i = 0; assets != null && i < assets.Length; i++)
            {
                if (assets[i] is not Material material)
                    continue;

                first ??= material;

                if (font != null && material.name.IndexOf(font.name, StringComparison.OrdinalIgnoreCase) >= 0)
                    named = material;
            }

            return named != null ? named : first;
        }

        // ------------------------------------------------------- Compatibilidade v2

        private static Action<bool> _legacyComplete = _ => { };
        private static Action<string, bool> _legacyCompleteWithLanguage = (_, _) => { };

        [Obsolete("Use RemoteFontBundleLoader.OnFontReady.")]
        public static event Action<bool> OnDownloadRemoteFontComplete
        {
            add => _legacyComplete += value;
            remove => _legacyComplete -= value;
        }

        /// <summary>Dispara ao fim de um download de fonte remota, com o idioma e o resultado.</summary>
        public static event Action<string, bool> OnFontReady
        {
            add => _legacyCompleteWithLanguage += value;
            remove => _legacyCompleteWithLanguage -= value;
        }

        [Obsolete("Use RemoteFontBundleLoader.OnFontReady.")]
        public static event Action<string, bool> OnRemoteFontDownloadComplete
        {
            add => _legacyCompleteWithLanguage += value;
            remove => _legacyCompleteWithLanguage -= value;
        }

        [Obsolete("Use RemoteFontBundleLoader.IsReady.")]
        public static bool IsRemoteFontReady => IsReady;

        [Obsolete("Use RemoteFontBundleLoader.IsReady.")]
        public static bool LastRemoteFontSucceeded => IsReady;

        [Obsolete("Use RemoteFontBundleLoader.LastLanguage.")]
        public static string LastRemoteFontLanguage => LastLanguage;

        [Obsolete("Use LanguageCode.IsSameOrRoot.")]
        public static bool IsSameLanguageOrRoot(string a, string b) => LanguageCode.IsSameOrRoot(a, b);

        [Obsolete("Use CanServeLanguage.")]
        public bool ShouldLoadRemoteFontForLanguage(string language) => CanServeLanguage(language);
    }
}
