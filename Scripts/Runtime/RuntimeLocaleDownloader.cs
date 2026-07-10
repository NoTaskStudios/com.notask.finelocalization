using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FineLocalization.Runtime;
using FineLocalization.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace FineLocalization.Scripts.Runtime
{
    public class RuntimeLocaleDownloader : MonoBehaviour
    {
        [Header("Runtime Settings")]
        [SerializeField] private bool downloadOnStart = true;
        [SerializeField] private bool useLocalSheet = false;
        [Tooltip("WebGL browsers block Google Sheets export redirects because they do not include CORS headers. Keep this disabled unless your deployment confirms direct Google downloads work.")]
        [SerializeField] private bool allowDirectGoogleDownloadInWebGL = false;
        [Tooltip("Optional URL pattern for a CORS-enabled proxy/CDN. Use {0} for TableId and {1} for gid. Leave empty to use Google Sheets export.")]
        [SerializeField] private string csvUrlPatternOverride;

        [Header("Retry Settings")]
        [SerializeField] private int maxDownloadAttempts = 3;
        [SerializeField] private int requestTimeoutSeconds = 10;
        [SerializeField] private float retryDelaySeconds = 1f;
        [SerializeField] private float delayBetweenSheets = 0.1f;

        [Header("Remote Font Integration")]
        [Tooltip("Opcional. Se definido, carrega o fallback de fonte remoto antes de atualizar os textos.")]
        [SerializeField] private RemoteFontBundleLoader remoteFontBundleLoader;

        [Tooltip("Baixa e aplica o fallback remoto do idioma resolvido antes de disparar OnLocalizationChanged.")]
        [SerializeField] private bool loadRemoteFontBeforeApplyingLocalization = true;

        [Header("Initial Language")]
        [Tooltip("Idioma inicial opcional para aplicar antes de liberar o evento de localização pronta. Ex: ja-jp")]
        [SerializeField] private string initialLanguageOverride;

        [Tooltip("Quando não há Initial Language nem lang na URL, aguarda SetRequestedLanguage antes de aplicar en-us.")]
        [SerializeField] private bool waitForExplicitRequestedLanguage = true;

        [Tooltip("Tempo máximo de espera por SetRequestedLanguage. Use 0 para aguardar indefinidamente.")]
        [SerializeField] private float requestedLanguageWaitTimeoutSeconds = 0f;

        [Tooltip("Compatibilidade: quando o jogo troca LocalizationManager.Language diretamente, usa esse idioma como requested language e libera o download.")]
        [SerializeField] private bool acceptLocalizationManagerLanguageAsRequested = true;

        private static string PersistentCsvDir =>
            Path.Combine(Application.persistentDataPath, "FineLocalization/Resources/Localization");

        private const string UrlPattern =
            "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";

        private static readonly string[] LanguageQueryKeys =
        {
            "lang", "language", "locale", "culture", "lng"
        };

        private static readonly HashSet<string> DefaultLatinScriptPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "en", "es", "pt", "fr", "de", "it", "nl", "ca", "gl", "eu", "oc", "rm",
            "sv", "no", "nb", "nn", "da", "fi", "is", "fo",
            "pl", "cs", "sk", "ro", "hu", "sl", "hr", "bs", "sq", "lt", "lv", "et",
            "tr", "az", "uz", "tk", "kk",
            "id", "ba", "ms", "vi", "tl", "fil",   // "ba" cobre o código custom "ba-id" (Bahasa Indonesia); "id" cobre id/id-id
            "ga", "cy", "gd", "br", "kw",
            "sw", "af", "zu", "xh", "yo", "ig", "ha", "so", "rw", "mg", "st", "sn", "ny",
            "lb", "fy", "mt", "ku", "ht", "qu", "gn"
        };

        private static Action<bool> _onDownloadLocalizationComplete = _ => { };
        private static Action<bool> _onAllSheetsDownloadedComplete = _ => { };

        public static event Action<bool> OnDownloadLocalizationComplete
        {
            add
            {
                _onDownloadLocalizationComplete += value;
                if (IsLocalizationReady)
                    value?.Invoke(LastLocalizationSucceeded);
            }
            remove => _onDownloadLocalizationComplete -= value;
        }

        public static event Action<bool> OnAllSheetsDownloadedComplete
        {
            add
            {
                _onAllSheetsDownloadedComplete += value;
                if (IsLocalizationReady)
                    value?.Invoke(LastLocalizationSucceeded);
            }
            remove => _onAllSheetsDownloadedComplete -= value;
        }

        private static event Action OnRequestedLanguageChanged = () => { };

        /// <summary>
        /// True once localization has been wired up at least once (either via Google Sheets
        /// download, persisted CSVs on disk, or bundled TextAssets). Late subscribers can read
        /// this flag and skip waiting for the event if they registered after it fired.
        /// </summary>
        public static bool IsLocalizationReady { get; private set; }

        /// <summary>
        /// True when the last completed cycle ended in success. Useful for late subscribers that
        /// want to mirror the last <see cref="OnDownloadLocalizationComplete"/> result.
        /// </summary>
        public static bool LastLocalizationSucceeded { get; private set; }

        /// <summary>
        /// Optional runtime language requested by the host game before the CSV download finishes.
        /// Set this from URL/query parameters before waiting for OnDownloadLocalizationComplete.
        /// </summary>
        public static string RequestedLanguage { get; private set; }
        public static bool HasExplicitRequestedLanguage { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _onDownloadLocalizationComplete = _ => { };
            _onAllSheetsDownloadedComplete = _ => { };
            OnRequestedLanguageChanged = () => { };
            IsLocalizationReady = false;
            LastLocalizationSucceeded = false;
            RequestedLanguage = null;
            HasExplicitRequestedLanguage = false;
        }

        public static void SetRequestedLanguage(string language)
        {
            RequestedLanguage = NormalizeLanguageCandidate(language);
            HasExplicitRequestedLanguage = !string.IsNullOrWhiteSpace(RequestedLanguage);
            FineLocalizationLogger.Log(() => $"[FineLocalization] Requested runtime language: '{RequestedLanguage}'.");
            OnRequestedLanguageChanged();
        }

        private readonly Dictionary<string, string> _csvData = new();
        private bool _downloadRoutineRunning;
        private bool _downloadCompleted;
        private string _activeRequestedLanguage;
        private string _preloadedRemoteFontLanguage;
        private bool _remoteFontLoaderMissingLogged;

        private void OnEnable()
        {
            EnsureRemoteFontBundleLoaderReference();
            OnRequestedLanguageChanged += HandleRequestedLanguageChanged;
            LocalizationManager.OnLocalizationChanged += HandleLocalizationLanguageChanged;
        }

        private void OnDisable()
        {
            OnRequestedLanguageChanged -= HandleRequestedLanguageChanged;
            LocalizationManager.OnLocalizationChanged -= HandleLocalizationLanguageChanged;
        }

        private void Start()
        {
            if (downloadOnStart)
            {
                StartDownloadIfNeeded();
            }
            else
            {
                if(useLocalSheet)
                    StartCoroutine(NotifyLocalizationAlreadyReady());
                else
                    FineLocalizationLogger.LogWarning("[FineLocalization] Certifique chamar manualmente a atualização da planilha");
                    //FineLocalizationLogger.LogWarning("[FineLocalization] Make sure the local spreadsheet is up to date.");
            }
        }

        private void HandleRequestedLanguageChanged()
        {
            if (!downloadOnStart || _downloadRoutineRunning)
                return;

            // Suporta escolha de idioma EXTERNA a qualquer momento. Mesmo após um ciclo já
            // concluído com sucesso, se o idioma pedido mudou em relação ao aplicado, reinicia
            // o pipeline (CSV + fonte remota) para o novo idioma. Sem isso, uma segunda chamada
            // de SetRequestedLanguage era ignorada e a troca externa não acontecia.
            if (_downloadCompleted && LastLocalizationSucceeded)
            {
                var requested = ResolveRequestedLanguageCandidate();
                var applied = NormalizeLanguageCandidate(LocalizationManager.Language);
                if (string.IsNullOrWhiteSpace(requested) ||
                    string.Equals(requested, applied, StringComparison.OrdinalIgnoreCase))
                    return;

                _downloadCompleted = false;
            }

            StartDownloadIfNeeded();
        }

        private void HandleLocalizationLanguageChanged()
        {
            var language = NormalizeLanguageCandidate(LocalizationManager.Language);
            if (ShouldForceDefaultWithoutRemoteFont(language))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] Idioma '{language}' requer fonte remota, mas RemoteFontBundleLoader não está configurado. Aplicando '{LocalizationManager.DefaultLanguage}'."
                );
                LocalizationManager.Language = LocalizationManager.DefaultLanguage;
                return;
            }

            if (!acceptLocalizationManagerLanguageAsRequested || HasExplicitRequestedLanguage)
                return;

            if (!IsNonDefaultLanguage(language))
                return;

            RequestedLanguage = language;
            HasExplicitRequestedLanguage = true;
            FineLocalizationLogger.Log(() => $"[FineLocalization] Requested runtime language from LocalizationManager.Language: '{RequestedLanguage}'.");
            OnRequestedLanguageChanged();
        }

        private void StartDownloadIfNeeded()
        {
            if (_downloadRoutineRunning)
                return;

            StartCoroutine(DownloadSheetsRuntime());
        }
        /// <summary>
        /// Quando o usuário marcou <see cref="downloadOnStart"/> = false, ainda precisamos
        /// notificar os componentes legados que se inscrevem em
        /// <see cref="OnDownloadLocalizationComplete"/> / <see cref="OnAllSheetsDownloadedComplete"/>.
        /// Esperamos 1 frame para garantir que todos os Awake/Start tiveram chance de se inscrever
        /// antes do evento ser disparado.
        /// </summary>
        private IEnumerator NotifyLocalizationAlreadyReady()
        {
            yield return null;
            yield return WaitForRequestedLanguageIfNeeded();
            yield return UseBundledCsvs();
        }
        /// <summary>
        /// Baixa todos os sheets configurados em runtime.
        /// </summary>
        public void DownloadSheets()
        {
            _downloadCompleted = false;
            StartDownloadIfNeeded();
        }

        public IEnumerator DownloadSheetsRuntime()
        {
            if (_downloadRoutineRunning)
                yield break;

            _downloadRoutineRunning = true;
            _downloadCompleted = false;

            LocalizationManager.RuntimeCsvResolver = GetCsvContent;
            LocalizationManager.RuntimeCsvPersistenceHook = PersistCsvContent;
            _csvData.Clear();

            yield return WaitForRequestedLanguageIfNeeded();

            _activeRequestedLanguage = ResolveRequestedLanguageCandidate();
            if (string.IsNullOrWhiteSpace(_activeRequestedLanguage))
            {
                FineLocalizationLogger.LogWarning("[FineLocalization] Nenhum idioma runtime foi informado. Localization não será aplicada.");
                CompleteDownload(false);
                yield break;
            }

            if (ShouldUseBundledCsvs())
            {
                yield return UseBundledCsvs();
                yield break;
            }

            var activeSources = LocalizationSettings.Instance.GetActiveSources();
            var allSourcesSuccess = true;

            for (var s = 0; s < activeSources.Count; s++)
            {
                var source = activeSources[s];

                if (string.IsNullOrEmpty(source.TableId) || source.Sheets.Count == 0)
                {
                    FineLocalizationLogger.LogWarning("[FineLocalization] TableId ou Sheets estão vazios.");

                    CompleteDownload(false);
                    yield break;
                }

                for (var i = 0; i < source.Sheets.Count; i++)
                {
                    var sheet = source.Sheets[i];
                    var url = BuildCsvUrl(source.TableId, sheet.Id);

                    var downloaded = false;
                    string csvContent = null;

                    yield return StartCoroutine(
                        DownloadCsvWithRetry(
                            url,
                            sheet.Name,
                            (success, content) =>
                            {
                                downloaded = success;
                                csvContent = content;
                            }
                        )
                    );

                    if (!downloaded || string.IsNullOrWhiteSpace(csvContent))
                    {
                        allSourcesSuccess = false;
                        continue;
                    }

                    _csvData[sheet.Name] = csvContent;
                    yield return SaveCsvToDisk(sheet.Name, csvContent);

                    if (delayBetweenSheets > 0f)
                        yield return new WaitForSecondsRealtime(delayBetweenSheets);
                }

            }

            var fullSuccess = allSourcesSuccess && _csvData.Count > 0;

            if (fullSuccess)
            {
                var applySuccess = false;
                yield return ApplyLocalizationWithOptionalRemoteFont(_csvData, success => applySuccess = success);
                fullSuccess = applySuccess;
            }

            CompleteDownload(fullSuccess);
        }

        private bool ShouldUseBundledCsvs()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return !allowDirectGoogleDownloadInWebGL &&
                   string.IsNullOrWhiteSpace(csvUrlPatternOverride);
#else
            return false;
#endif
        }

        /// <summary>
        /// Pula o download remoto e usa os CSVs já presentes no projeto (TextAsset bundled
        /// ou CSV persistido no disco/IndexedDB). Sempre dispara os mesmos eventos do fluxo
        /// de download bem-sucedido, garantindo compatibilidade com projetos antigos.
        /// </summary>
        private IEnumerator UseBundledCsvs()
        {
            LocalizationManager.RuntimeCsvResolver = GetCsvContent;
            LocalizationManager.RuntimeCsvPersistenceHook = PersistCsvContent;
            yield return WaitForRequestedLanguageIfNeeded();

            if (string.IsNullOrWhiteSpace(_activeRequestedLanguage))
                _activeRequestedLanguage = ResolveRequestedLanguageCandidate();

            if (string.IsNullOrWhiteSpace(_activeRequestedLanguage))
            {
                FineLocalizationLogger.LogWarning("[FineLocalization] Nenhum idioma runtime foi informado. Localization nao sera aplicada.");
                CompleteDownload(false);
                yield break;
            }

            var targetLanguage = _activeRequestedLanguage;
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                FineLocalizationLogger.LogWarning("[FineLocalization] Nenhum idioma runtime foi informado. Localization não será aplicada como en-us automaticamente.");
                CompleteDownload(false);
                yield break;
            }

            EnsureLanguageCanRenderWithoutRemoteFont(ref targetLanguage);

            var needsRemoteFont = ShouldLoadRemoteFont(targetLanguage);
            var shouldLoadRemoteFont = needsRemoteFont && !IsRemoteFontPreloaded(targetLanguage);
            if (shouldLoadRemoteFont)
            {
                var fontReady = false;
                yield return WaitForRemoteFontIfNeeded(targetLanguage, success => fontReady = success);
                if (!fontReady)
                {
                    ApplyDefaultLanguageAfterFontFailure();
                    targetLanguage = LocalizationManager.DefaultLanguage;
                    needsRemoteFont = false;
                    shouldLoadRemoteFont = false;
                }
            }

            LocalizationManager.ReloadAll(targetLanguage, false);
            if (!TryResolveAppliedLanguage(targetLanguage, out targetLanguage))
            {
                CompleteDownload(false);
                yield break;
            }

            IgnoreNextRemoteFontLocalizationEvent();
            LocalizationManager.Refresh();

            if (needsRemoteFont && !string.Equals(targetLanguage, LocalizationManager.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                yield return remoteFontBundleLoader.RebuildCurrentTexts();

            FineLocalizationLogger.Log(
                "[FineLocalization] Using bundled CSV TextAssets (downloadOnStart = false or WebGL CORS fallback)."
            );

            CompleteDownload(true);
        }

        private IEnumerator ApplyLocalizationWithOptionalRemoteFont(Dictionary<string, string> csvData, Action<bool> onComplete)
        {
            var targetLanguage = string.IsNullOrWhiteSpace(_activeRequestedLanguage)
                ? ResolveRequestedLanguageCandidate()
                : _activeRequestedLanguage;
            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                FineLocalizationLogger.LogWarning("[FineLocalization] Nenhum idioma runtime foi informado. Localization não será aplicada como en-us automaticamente.");
                onComplete?.Invoke(false);
                yield break;
            }

            var requestedLanguage = targetLanguage;
            EnsureLanguageCanRenderWithoutRemoteFont(ref targetLanguage);
            if (!string.Equals(requestedLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase))
                requestedLanguage = targetLanguage;

            var needsRemoteFont = ShouldLoadRemoteFont(targetLanguage);
            var shouldLoadRemoteFont = needsRemoteFont && !IsRemoteFontPreloaded(targetLanguage);
            if (shouldLoadRemoteFont)
            {
                var fontReady = false;
                yield return WaitForRemoteFontIfNeeded(targetLanguage, success => fontReady = success);
                if (!fontReady)
                {
                    ApplyDefaultLanguageAfterFontFailure();
                    targetLanguage = LocalizationManager.DefaultLanguage;
                    requestedLanguage = targetLanguage;
                    needsRemoteFont = false;
                    shouldLoadRemoteFont = false;
                }
            }

            LocalizationManager.LoadFromCsvMap(csvData, targetLanguage, false);
            if (!TryResolveAppliedLanguage(requestedLanguage, out targetLanguage))
            {
                onComplete?.Invoke(false);
                yield break;
            }

            FineLocalizationLogger.Log(() => $"[FineLocalization] Runtime language resolved: requested='{requestedLanguage}', applied='{targetLanguage}'.");

            IgnoreNextRemoteFontLocalizationEvent();
            LocalizationManager.Refresh();

            if (needsRemoteFont && !string.Equals(targetLanguage, LocalizationManager.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                yield return remoteFontBundleLoader.RebuildCurrentTexts();

            onComplete?.Invoke(true);
        }

        private IEnumerator WaitForRemoteFontIfNeeded(string targetLanguage, Action<bool> onComplete)
        {
            if (!ShouldLoadRemoteFont(targetLanguage))
            {
                onComplete?.Invoke(true);
                yield break;
            }

            var normalizedLanguage = NormalizeLanguageCandidate(targetLanguage);
            if (RemoteFontBundleLoader.IsRemoteFontReady &&
                RemoteFontBundleLoader.IsSameLanguageOrRoot(RemoteFontBundleLoader.LastRemoteFontLanguage, normalizedLanguage))
            {
                _preloadedRemoteFontLanguage = normalizedLanguage;
                onComplete?.Invoke(true);
                yield break;
            }

            var fontLoaded = false;
            yield return remoteFontBundleLoader.EnsureFontForLanguage(normalizedLanguage, success => fontLoaded = success);

            if (fontLoaded && RemoteFontBundleLoader.IsRemoteFontReady)
                _preloadedRemoteFontLanguage = normalizedLanguage;

            onComplete?.Invoke(fontLoaded && RemoteFontBundleLoader.IsRemoteFontReady);
        }

        private bool IsRemoteFontPreloaded(string language)
        {
            return !string.IsNullOrWhiteSpace(language) &&
                   RemoteFontBundleLoader.IsSameLanguageOrRoot(_preloadedRemoteFontLanguage, NormalizeLanguageCandidate(language));
        }

        private static bool TryResolveAppliedLanguage(string requestedLanguage, out string appliedLanguage)
        {
            appliedLanguage = LocalizationManager.Language;

            if (string.IsNullOrWhiteSpace(requestedLanguage))
                return false;

            // Resolve o pedido do mesmo jeito que o LocalizationManager (en -> en-us, pt -> pt-br,
            // fallbacks regionais/por prefixo). Se a chave resolvida existe nos dados carregados, o
            // idioma FOI encontrado — mesmo quando resolve para o default (ex.: lang=en -> en-us).
            var resolvedLanguage = LanguageReader.GetLanguageKey(requestedLanguage);
            if (LocalizationManager.Dictionary != null && LocalizationManager.Dictionary.ContainsKey(resolvedLanguage))
                return true;

            // Idioma pedido não existe na planilha. O ReloadAll/LoadFromCsvMap anterior já resolveu
            // para o DefaultLanguage (en-us); seguimos o fluxo aplicando esse fallback em vez de travar,
            // para que os textos atualizem em inglês quando o idioma pedido não está na planilha.
            // Cópia local: um parâmetro 'out' não pode ser capturado dentro da lambda do log (CS1628).
            var fallbackLanguage = appliedLanguage;
            FineLocalizationLogger.LogWarning(
                () => $"[FineLocalization] Idioma solicitado '{requestedLanguage}' não encontrado nos CSVs. Aplicando fallback '{fallbackLanguage}'."
            );
            return true;
        }

        private static void ApplyDefaultLanguageAfterFontFailure()
        {
            FineLocalizationLogger.LogWarning("[FineLocalization] Falha ao carregar fonte remota. Aplicando en-us como fallback.");
            LocalizationManager.ReloadAll(LocalizationManager.DefaultLanguage, false);
        }

        private bool ShouldLoadRemoteFont(string language)
        {
            EnsureRemoteFontBundleLoaderReference();
            return loadRemoteFontBeforeApplyingLocalization &&
                   remoteFontBundleLoader != null &&
                   remoteFontBundleLoader.ShouldLoadRemoteFontForLanguage(language);
        }

        private void EnsureRemoteFontBundleLoaderReference()
        {
            if (remoteFontBundleLoader == null)
            {
                // Inclui objetos INATIVOS na busca. Na versão antiga o RuntimeLocaleDownloader
                // era quem achava e inicializava o loader; FindFirstObjectByType() padrão só
                // retorna objetos ativos, então um loader inativo ficava invisível e nunca era
                // comandado (nem se auto-inicializava via OnEnable).
#if UNITY_2022_2_OR_NEWER
                remoteFontBundleLoader = FindFirstObjectByType<RemoteFontBundleLoader>(FindObjectsInactive.Include);
#else
                remoteFontBundleLoader = FindObjectOfType<RemoteFontBundleLoader>(true);
#endif
            }

            if (remoteFontBundleLoader == null)
            {
                if (!_remoteFontLoaderMissingLogged)
                {
                    _remoteFontLoaderMissingLogged = true;
                    FineLocalizationLogger.LogWarning(
                        "[FineLocalization] RemoteFontBundleLoader não encontrado na cena. Fontes remotas não serão baixadas. " +
                        "Adicione o componente a um GameObject ou atribua a referência no inspector."
                    );
                }
                return;
            }

            _remoteFontLoaderMissingLogged = false;

            // O RuntimeLocaleDownloader é o responsável por inicializar e comandar o loader.
            // Um loader inativo nunca roda OnEnable nem consegue iniciar coroutines
            // (StartCoroutine lança exceção em GameObject inativo), então o download da fonte
            // jamais aconteceria. Garantimos que ele esteja ativo antes de dirigi-lo.
            var loaderGameObject = remoteFontBundleLoader.gameObject;
            if (!loaderGameObject.activeSelf)
            {
                FineLocalizationLogger.Log(
                    () => $"[FineLocalization] Ativando RemoteFontBundleLoader '{loaderGameObject.name}' (estava inativo) para inicializá-lo."
                );
                loaderGameObject.SetActive(true);
            }

            if (!remoteFontBundleLoader.enabled)
                remoteFontBundleLoader.enabled = true;

            if (!loaderGameObject.activeInHierarchy)
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] RemoteFontBundleLoader '{loaderGameObject.name}' continua inativo na hierarquia " +
                          "(algum GameObject pai está desativado). Ative o objeto pai para permitir o download da fonte."
                );
            }
        }

        private void EnsureLanguageCanRenderWithoutRemoteFont(ref string targetLanguage)
        {
            var normalizedLanguage = NormalizeLanguageCandidate(targetLanguage);
            if (!ShouldForceDefaultWithoutRemoteFont(normalizedLanguage))
            {
                targetLanguage = normalizedLanguage;
                return;
            }

            FineLocalizationLogger.LogWarning(
                () => $"[FineLocalization] Idioma '{normalizedLanguage}' requer fonte remota, mas RemoteFontBundleLoader não está configurado. Aplicando '{LocalizationManager.DefaultLanguage}'."
            );
            targetLanguage = LocalizationManager.DefaultLanguage;
        }

        private bool ShouldForceDefaultWithoutRemoteFont(string language)
        {
            EnsureRemoteFontBundleLoaderReference();

            var normalizedLanguage = NormalizeLanguageCandidate(language);
            if (string.IsNullOrWhiteSpace(normalizedLanguage) ||
                string.Equals(normalizedLanguage, LocalizationManager.DefaultLanguage, StringComparison.OrdinalIgnoreCase) ||
                IsLatinScriptLanguage(normalizedLanguage))
            {
                return false;
            }

            return !loadRemoteFontBeforeApplyingLocalization ||
                   remoteFontBundleLoader == null ||
                   !remoteFontBundleLoader.ShouldLoadRemoteFontForLanguage(normalizedLanguage);
        }

        private void IgnoreNextRemoteFontLocalizationEvent()
        {
            if (loadRemoteFontBeforeApplyingLocalization && remoteFontBundleLoader != null)
                remoteFontBundleLoader.IgnoreNextLocalizationChanged();
        }

        private string ResolveRequestedLanguageCandidate()
        {
            var requestedLanguage = HasExplicitRequestedLanguage && !string.IsNullOrWhiteSpace(RequestedLanguage)
                ? RequestedLanguage
                : initialLanguageOverride;

            if (string.IsNullOrWhiteSpace(requestedLanguage))
                requestedLanguage = TryResolveLanguageFromLaunchUrl();

            if (string.IsNullOrWhiteSpace(requestedLanguage) &&
                acceptLocalizationManagerLanguageAsRequested &&
                IsNonDefaultLanguage(LocalizationManager.Language))
            {
                requestedLanguage = LocalizationManager.Language;
            }

            return NormalizeLanguageCandidate(requestedLanguage);
        }

        private IEnumerator WaitForRequestedLanguageIfNeeded()
        {
            if (!waitForExplicitRequestedLanguage)
                yield break;

            if (HasExplicitRequestedLanguage ||
                !string.IsNullOrWhiteSpace(initialLanguageOverride) ||
                !string.IsNullOrWhiteSpace(TryResolveLanguageFromLaunchUrl()) ||
                (acceptLocalizationManagerLanguageAsRequested && IsNonDefaultLanguage(LocalizationManager.Language)))
            {
                yield break;
            }

            var timeout = Mathf.Max(0f, requestedLanguageWaitTimeoutSeconds);
            var start = Time.realtimeSinceStartup;

            // Torna VISÍVEL que o download está parado aguardando a escolha de idioma externa.
            // Sem isso, o downloader fica em silêncio no while e parece que "nada acontece".
            FineLocalizationLogger.Log(() =>
                $"[FineLocalization] Aguardando idioma externo (RuntimeLocaleDownloader.SetRequestedLanguage) " +
                $"antes de baixar a localização. Timeout: {(timeout <= 0f ? "infinito" : timeout + "s")}.");

            if (timeout <= 0f)
            {
                while (!HasExplicitRequestedLanguage)
                    yield return null;

                yield break;
            }

            while (!HasExplicitRequestedLanguage && Time.realtimeSinceStartup - start < timeout)
                yield return null;
        }

        private static string NormalizeLanguageCandidate(string language)
        {
            return string.IsNullOrWhiteSpace(language)
                ? string.Empty
                : language.Trim().Trim('\uFEFF').Replace('_', '-').ToLowerInvariant();
        }

        private static bool IsNonDefaultLanguage(string language)
        {
            var normalized = NormalizeLanguageCandidate(language);
            return !string.IsNullOrWhiteSpace(normalized) &&
                   !string.Equals(normalized, LocalizationManager.DefaultLanguage, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLatinScriptLanguage(string language)
        {
            var normalized = NormalizeLanguageCandidate(language);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            var dashIndex = normalized.IndexOf('-');
            var rootPrefix = dashIndex >= 0 ? normalized.Substring(0, dashIndex) : normalized;
            return DefaultLatinScriptPrefixes.Contains(rootPrefix);
        }

        private static string TryResolveLanguageFromLaunchUrl()
        {
            var url = Application.absoluteURL;
            if (string.IsNullOrWhiteSpace(url))
                return null;

            var queryStart = url.IndexOf('?');
            if (queryStart < 0)
                return null;

            var queryEnd = url.IndexOf('#', queryStart + 1);
            var query = queryEnd >= 0
                ? url.Substring(queryStart + 1, queryEnd - queryStart - 1)
                : url.Substring(queryStart + 1);

            if (string.IsNullOrWhiteSpace(query))
                return null;

            var pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                if (string.IsNullOrWhiteSpace(pair))
                    continue;

                var equalsIndex = pair.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                var key = DecodeQueryPart(pair.Substring(0, equalsIndex));
                if (!IsLanguageQueryKey(key))
                    continue;

                var value = DecodeQueryPart(pair.Substring(equalsIndex + 1));
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static bool IsLanguageQueryKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            for (int i = 0; i < LanguageQueryKeys.Length; i++)
            {
                if (string.Equals(key, LanguageQueryKeys[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string DecodeQueryPart(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return Uri.UnescapeDataString(value.Replace("+", " "));
        }

        private static void MarkReady(bool success)
        {
            IsLocalizationReady = true;
            LastLocalizationSucceeded = success;
        }

        private void CompleteDownload(bool success)
        {
            MarkReady(success);
            _downloadRoutineRunning = false;
            _downloadCompleted = true;
            _activeRequestedLanguage = null;
            _preloadedRemoteFontLanguage = null;
            _onDownloadLocalizationComplete?.Invoke(success);
            _onAllSheetsDownloadedComplete?.Invoke(success);
        }

        private string BuildCsvUrl(string tableId, long sheetId)
        {
            var pattern = string.IsNullOrWhiteSpace(csvUrlPatternOverride)
                ? UrlPattern
                : csvUrlPatternOverride;

            return string.Format(pattern, tableId, sheetId);
        }

        private IEnumerator DownloadCsvWithRetry(
            string url,
            string sheetName,
            Action<bool, string> onComplete)
        {
            var attempts = Mathf.Max(1, maxDownloadAttempts);
            string lastFailureDetails = null;

            FineLocalizationLogger.Log(
                () => $"[FineLocalization] Baixando '{sheetName}' (tentativas: {attempts}, timeout: {requestTimeoutSeconds}s): {url}"
            );

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                using (var request = UnityWebRequest.Get(url))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = Mathf.Max(1, requestTimeoutSeconds);

                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var csvContent = Encoding.UTF8.GetString(request.downloadHandler.data);

                        // Se cair em página de login, o arquivo não está público.
                        if (csvContent.Contains("signin/identifier"))
                        {
                            lastFailureDetails =
                                "A planilha não está pública (o Google devolveu a página de login). " +
                                "Compartilhe como 'Qualquer pessoa com o link pode ver'.";
                            FineLocalizationLogger.LogWarning(
                                () => $"[FineLocalization] Acesso negado ao documento: {sheetName}. " +
                                      $"Tentativa {attempt}/{attempts}."
                            );
                        }
                        else
                        {
                            onComplete?.Invoke(true, csvContent);
                            yield break;
                        }
                    }
                    else
                    {
                        lastFailureDetails = DescribeRequestFailure(request, url);
                        var details = lastFailureDetails;
                        FineLocalizationLogger.LogWarning(
                            () => $"[FineLocalization] Falha ao baixar {sheetName}. " +
                                  $"Tentativa {attempt}/{attempts}. {details}"
                        );
                    }
                }

                if (attempt < attempts && retryDelaySeconds > 0f)
                    yield return new WaitForSecondsRealtime(retryDelaySeconds);
            }

            // Erro direto no Debug (sem passar pelo gate de EnableLogs): uma planilha que não
            // baixa deixa o jogo sem tradução em build, e com EnableLogs desligado (default)
            // a falha seria invisível — impossível de diagnosticar em produção.
            Debug.LogError(
                $"[FineLocalization] Não foi possível baixar a planilha '{sheetName}' após {attempts} tentativa(s). {lastFailureDetails}"
            );

            onComplete?.Invoke(false, null);
        }

        private static string DescribeRequestFailure(UnityWebRequest request, string url)
        {
            var description =
                $"Resultado: {request.result}, HTTP: {request.responseCode}, Erro: '{request.error}', URL: {url}";

#if UNITY_WEBGL && !UNITY_EDITOR
            // O navegador esconde o motivo real de um bloqueio CORS da Unity: a request volta
            // como ConnectionError com responseCode 0 e erro genérico ("Unknown Error"). Essa
            // assinatura é o único jeito de inferir CORS aqui — a mensagem verdadeira
            // ("blocked by CORS policy") só aparece no console do navegador (F12).
            if (request.responseCode == 0 &&
                (request.result == UnityWebRequest.Result.ConnectionError ||
                 request.result == UnityWebRequest.Result.ProtocolError))
            {
                description +=
                    " | Provável bloqueio de CORS (ou falha de rede): confirme no console do navegador (F12) se há " +
                    "'blocked by CORS policy'. Se houver, configure csvUrlPatternOverride com um proxy/CDN com CORS " +
                    "habilitado, ou mantenha allowDirectGoogleDownloadInWebGL desativado para usar os CSVs empacotados.";
            }
#endif

            return description;
        }

        private void PersistCsvContent(string sheetName, string content)
        {
            StartCoroutine(SaveCsvToDisk(sheetName, content));
        }

        /// <summary>
        /// Salva o CSV na pasta de dados persistente (IndexedDB no WebGL).
        /// </summary>
        private IEnumerator SaveCsvToDisk(string fileName, string content)
        {
            try
            {
                if (!Directory.Exists(PersistentCsvDir))
                    Directory.CreateDirectory(PersistentCsvDir);

                var filePath = Path.Combine(PersistentCsvDir, fileName + ".csv");
                File.WriteAllText(filePath, content, Encoding.UTF8);
            }
            catch (Exception e)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Erro ao salvar CSV {fileName}: {e.Message}");
            }

            yield return null;
        }

        /// <summary>
        /// Tenta obter CSV do cache de memória; se não houver, lê do disco persistente.
        /// </summary>
        public string GetCsvContent(string sheetName)
        {
            if (_csvData.TryGetValue(sheetName, out var mem))
                return mem;

            var filePath = Path.Combine(PersistentCsvDir, sheetName + ".csv");

            if (File.Exists(filePath))
            {
                var content = File.ReadAllText(filePath, Encoding.UTF8);
                _csvData[sheetName] = content;
                return content;
            }

            return null;
        }

        /// <summary>
        /// Verifica se um sheet específico existe (memória ou disco persistente).
        /// </summary>
        public bool HasSheet(string sheetName)
        {
            return _csvData.ContainsKey(sheetName)
                   || File.Exists(Path.Combine(PersistentCsvDir, sheetName + ".csv"));
        }

        /// <summary>
        /// Limpa todos os dados baixados/persistidos.
        /// </summary>
        public void ClearDownloadedData()
        {
            _csvData.Clear();

            try
            {
                if (Directory.Exists(PersistentCsvDir))
                    Directory.Delete(PersistentCsvDir, true);
            }
            catch (Exception e)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Erro ao limpar dados: {e.Message}");
            }
        }

        /// <summary>
        /// Método para recarregar a localização manualmente.
        /// </summary>
        private void ReloadLocalization()
        {
            LocalizationManager.ReloadAll();
        }

        /// <summary>
        /// API com callback para receber o mapa de CSVs baixados.
        /// </summary>
        public void DownloadSheetsWithCallback(Action<bool, Dictionary<string, string>> callback)
        {
            StartCoroutine(DownloadWithCallbackCoroutine(callback));
        }

        private IEnumerator DownloadWithCallbackCoroutine(Action<bool, Dictionary<string, string>> callback)
        {
            yield return StartCoroutine(DownloadSheetsRuntime());

            callback?.Invoke(_csvData.Count > 0, _csvData);
        }
    }
}
