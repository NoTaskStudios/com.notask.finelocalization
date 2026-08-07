using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FineLocalization.Runtime;
using UnityEngine;
using UnityEngine.Networking;

namespace FineLocalization.Scripts.Runtime
{
    /// <summary>
    /// Orquestra a localização em runtime: carrega os CSVs, resolve o idioma e garante a fonte
    /// antes de atualizar os textos. Coloque um único componente destes num GameObject
    /// persistente da cena inicial.
    ///
    /// A API pública do pacote é <see cref="Localization"/> — este componente é a configuração dela.
    ///
    /// <para><b>Princípio da v3:</b> baixar a planilha NÃO depende do idioma. Um CSV do
    /// FineLocalization contém todas as colunas de idioma, então o download começa
    /// imediatamente e o idioma é resolvido depois, quando os dados já estão em memória.
    /// Trocar de idioma mais tarde é instantâneo — não re-baixa nem re-parseia nada.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RuntimeLocaleDownloader : MonoBehaviour
    {
        /// <summary>De onde vêm os CSVs.</summary>
        public enum CsvSource
        {
            /// <summary>WebGL sem proxy usa os CSVs embutidos (o Google Sheets bloqueia por CORS); nas demais plataformas baixa.</summary>
            Auto = 0,

            /// <summary>Sempre baixa. Em WebGL exige <c>Csv Url Override</c> apontando para um CDN/proxy com CORS.</summary>
            Remote = 1,

            /// <summary>Nunca baixa. Usa os TextAssets do build e o CSV persistido em disco.</summary>
            Bundled = 2
        }

        [Serializable]
        public class NetworkOptions
        {
            [Tooltip("Tentativas por planilha antes de desistir.")]
            [Min(1)] public int maxAttempts = 3;

            [Tooltip("Timeout de cada request HTTP, em segundos.")]
            [Min(1)] public int timeoutSeconds = 10;

            [Tooltip("Espera entre tentativas, em segundos.")]
            [Min(0f)] public float retryDelaySeconds = 1f;
        }

        [Header("Planilha")]
        [Tooltip("Auto: WebGL usa os CSVs embutidos (CORS), demais plataformas baixam. Definir Csv Url Override liga o download também em WebGL.")]
        [SerializeField] private CsvSource source = CsvSource.Auto;

        [Tooltip("Opcional. URL de um proxy/CDN com CORS habilitado. Use {0} para o TableId e {1} para o gid. Vazio = export direto do Google Sheets.")]
        [SerializeField] private string csvUrlOverride;

        [Header("Idioma")]
        [Tooltip("Força um idioma no boot, ignorando URL e sistema. Vazio = usa a cadeia de resolução. Ex: ja-jp")]
        [SerializeField] private string startupLanguage;

        [Tooltip("Lê o idioma da query string da URL (?lang=, ?locale=, ?culture=...). Relevante em WebGL.")]
        [SerializeField] private bool useUrlQueryLanguage = true;

        [Tooltip("Usa o idioma do sistema operacional / navegador quando nada mais definir um.")]
        [SerializeField] private bool useSystemLanguage;

        [Tooltip("Idioma final quando nenhuma outra fonte resolve, e destino de segurança quando a fonte remota falha.")]
        [SerializeField] private string fallbackLanguage = LocalizationManager.DefaultLanguage;

        [Header("Fontes")]
        [Tooltip("Opcional. Baixa a fonte do idioma antes de atualizar os textos. Vazio = procura um na cena.")]
        [SerializeField] private RemoteFontBundleLoader fontLoader;

        [Header("Rede")]
        [SerializeField] private NetworkOptions network = new();

        private const string GoogleExportUrl = "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";

        private static string PersistentCsvDir =>
            Path.Combine(Application.persistentDataPath, "FineLocalization/Resources/Localization");

        private readonly Dictionary<string, string> _csv = new();
        private string _queuedLanguage;
        private Action<bool> _queuedCallback;
        private bool _busy;
        private bool _dictionaryLoaded;
        private bool _remoteFontInstalled;
        private bool _fontLoaderMissingLogged;

        private void Awake()
        {
            Localization.Attach(this);
        }

        private void OnDestroy()
        {
            Localization.Detach(this);
        }

        private void Start()
        {
            StartCoroutine(Boot());
        }

        // ------------------------------------------------------------------ API

        /// <summary>
        /// Aplica um idioma. Se um ciclo já estiver rodando, o pedido entra na fila e o mais
        /// recente vence — chamadas rápidas em sequência não disputam entre si.
        /// </summary>
        public void ApplyLanguage(string language, Action<bool> onApplied = null)
        {
            _queuedLanguage = LanguageCode.Normalize(language);
            _queuedCallback = onApplied;

            if (_busy || !isActiveAndEnabled)
                return;

            StartCoroutine(DrainQueue());
        }

        /// <summary>Recarrega os CSVs (re-baixando quando a origem for remota) no idioma atual.</summary>
        public void Reload(Action<bool> onComplete = null)
        {
            if (_busy)
            {
                onComplete?.Invoke(false);
                return;
            }

            StartCoroutine(ReloadRoutine(onComplete));
        }

        /// <summary>CSV de uma planilha, da memória ou do disco persistente. Null se não existir.</summary>
        public string GetCsvContent(string sheetName)
        {
            if (_csv.TryGetValue(sheetName, out var cached))
                return cached;

            var path = CsvPathFor(sheetName);
            if (!File.Exists(path))
                return null;

            var content = File.ReadAllText(path, Encoding.UTF8);
            _csv[sheetName] = content;
            return content;
        }

        /// <summary>True quando a planilha existe em memória ou em disco.</summary>
        public bool HasSheet(string sheetName) => _csv.ContainsKey(sheetName) || File.Exists(CsvPathFor(sheetName));

        /// <summary>Apaga os CSVs baixados da memória e do disco persistente.</summary>
        public void ClearDownloadedData()
        {
            _csv.Clear();

            try
            {
                if (Directory.Exists(PersistentCsvDir))
                    Directory.Delete(PersistentCsvDir, true);
            }
            catch (Exception e)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Erro ao limpar CSVs persistidos: {e.Message}");
            }
        }

        internal void ConsumePendingLanguage()
        {
            if (Localization.TryTakePendingLanguage(out var language, out var callback))
                ApplyLanguage(language, callback);
        }

        // -------------------------------------------------------------- Pipeline

        private IEnumerator Boot()
        {
            _busy = true;

            LocalizationManager.RuntimeCsvResolver = GetCsvContent;
            LocalizationManager.RuntimeCsvPersistenceHook = PersistCsvContent;

            // 1. Dados. Independe do idioma — o CSV traz todas as colunas.
            var dataOk = true;
            if (UsesRemoteCsv)
                yield return DownloadAllSheets(ok => dataOk = ok);

            // 2. Idioma. Um pedido explícito do jogo, feito antes daqui, tem prioridade.
            if (!TakeQueued(out var language, out var callback))
            {
                Localization.TryTakePendingLanguage(out language, out callback);
                if (string.IsNullOrEmpty(language))
                    language = ResolveStartupLanguage();
            }

            // 3. Fonte + dicionário + refresh, numa única passada.
            var applyOk = true;
            yield return ApplyLanguageRoutine(language, ok => applyOk = ok);

            var success = dataOk && applyOk;
            Localization.MarkReady(success);
            NotifyLegacyComplete(success);
            callback?.Invoke(applyOk);

            _busy = false;

            if (!string.IsNullOrEmpty(_queuedLanguage))
                yield return DrainQueue();
        }

        private IEnumerator ReloadRoutine(Action<bool> onComplete)
        {
            _busy = true;

            var dataOk = true;
            if (UsesRemoteCsv)
            {
                _csv.Clear();
                yield return DownloadAllSheets(ok => dataOk = ok);
            }

            _dictionaryLoaded = false; // força o re-parse dos CSVs na próxima aplicação

            var applyOk = true;
            yield return ApplyLanguageRoutine(LocalizationManager.Language, ok => applyOk = ok);

            _busy = false;
            onComplete?.Invoke(dataOk && applyOk);
        }

        private IEnumerator DrainQueue()
        {
            _busy = true;

            while (TakeQueued(out var language, out var callback))
            {
                var ok = true;
                yield return ApplyLanguageRoutine(language, r => ok = r);
                callback?.Invoke(ok);
            }

            _busy = false;
        }

        /// <summary>
        /// Aplica um idioma numa passada só: garante a fonte, carrega o dicionário e dispara
        /// um único <c>OnLocalizationChanged</c>. Cai no fallback quando o idioma exige uma
        /// fonte remota que não está disponível — melhor inglês legível que caixas vazias.
        /// </summary>
        private IEnumerator ApplyLanguageRoutine(string requested, Action<bool> onApplied)
        {
            var target = LanguageCode.Normalize(requested);
            if (string.IsNullOrEmpty(target))
                target = ResolveStartupLanguage();

            var loader = ResolveFontLoader();
            var success = true;

            if (loader != null)
            {
                // Sempre passa pelo loader, mesmo em idioma Latin: é ele quem desinstala a fonte
                // remota do idioma anterior. Sem isso, trocar ja-jp → en-us deixaria o atlas
                // japonês pendurado na árvore de fallback consumindo memória.
                var fontOk = false;
                yield return loader.EnsureFontForLanguage(target, ok => fontOk = ok);

                if (!fontOk)
                {
                    FineLocalizationLogger.LogWarning(
                        () => $"[FineLocalization] Sem fonte utilizável para '{target}'. Aplicando '{Fallback}'."
                    );
                    target = Fallback;
                    success = false;
                    yield return loader.EnsureFontForLanguage(target);
                }
            }
            else if (NeedsRemoteFont(target))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] '{target}' precisa de fonte remota e não há RemoteFontBundleLoader na cena. Aplicando '{Fallback}'."
                );
                target = Fallback;
                success = false;
            }

            LoadDictionary(target);

            var applied = LocalizationManager.Language;
            if (!LanguageCode.IsSameOrRoot(applied, target))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] Idioma '{target}' não existe nas planilhas. Aplicado '{applied}'."
                );
            }

            // O loader também escuta OnLocalizationChanged; sem isso ele dispararia um segundo
            // ciclo de download/rebuild para o idioma que acabamos de preparar aqui.
            loader?.IgnoreNextLocalizationChanged();
            LocalizationManager.Refresh();

            // Reconstrói quando há fonte remota em jogo agora, ou quando havia até agora pouco —
            // nesse caso os meshes já desenhados carregam materiais que acabaram de ser removidos.
            var servingNow = loader != null && loader.IsServing(applied);
            if (loader != null && (servingNow || _remoteFontInstalled))
                yield return loader.RebuildCurrentTexts();

            _remoteFontInstalled = servingNow;

            FineLocalizationLogger.Log(() => $"[FineLocalization] Idioma aplicado: '{applied}' (pedido: '{requested}').");
            onApplied?.Invoke(success);
        }

        /// <summary>
        /// Primeira aplicação parseia os CSVs; as seguintes só reapontam o idioma. Trocar de
        /// idioma na v2 re-parseava todas as planilhas — aqui é O(1).
        /// </summary>
        private void LoadDictionary(string target)
        {
            if (_dictionaryLoaded && LocalizationManager.Dictionary.Count > 0)
            {
                LocalizationManager.SetLanguage(target, notify: false);
                return;
            }

            LocalizationManager.LoadFromCsvMap(_csv, target, notify: false);
            _dictionaryLoaded = LocalizationManager.Dictionary.Count > 0;
        }

        private bool TakeQueued(out string language, out Action<bool> callback)
        {
            language = _queuedLanguage;
            callback = _queuedCallback;
            _queuedLanguage = null;
            _queuedCallback = null;
            return !string.IsNullOrEmpty(language);
        }

        // -------------------------------------------------------------- Idioma

        private string Fallback
        {
            get
            {
                var normalized = LanguageCode.Normalize(fallbackLanguage);
                return string.IsNullOrEmpty(normalized) ? LocalizationManager.DefaultLanguage : normalized;
            }
        }

        /// <summary>
        /// Cadeia de resolução, da maior para a menor prioridade:
        /// Startup Language → query da URL → idioma do sistema → fallback.
        /// Um pedido explícito via <see cref="Localization.SetLanguage"/> vence todos.
        /// </summary>
        private string ResolveStartupLanguage()
        {
            var explicitLanguage = LanguageCode.Normalize(startupLanguage);
            if (!string.IsNullOrEmpty(explicitLanguage))
                return explicitLanguage;

            if (useUrlQueryLanguage)
            {
                var fromUrl = LanguageCode.FromLaunchUrl();
                if (!string.IsNullOrEmpty(fromUrl))
                    return fromUrl;
            }

            if (useSystemLanguage)
            {
                var fromSystem = LanguageCode.FromSystemLanguage();
                if (!string.IsNullOrEmpty(fromSystem))
                    return fromSystem;
            }

            return Fallback;
        }

        /// <summary>True quando o idioma usa um script que as fontes embutidas não cobrem.</summary>
        private bool NeedsRemoteFont(string language)
        {
            if (string.IsNullOrEmpty(language))
                return false;

            var loader = ResolveFontLoader();
            return loader != null
                ? loader.NeedsRemoteFont(language)
                : !LanguageCode.IsLatinScript(language);
        }

        // -------------------------------------------------------------- Fontes

        private RemoteFontBundleLoader ResolveFontLoader()
        {
            if (fontLoader == null)
            {
#if UNITY_2022_2_OR_NEWER
                fontLoader = FindFirstObjectByType<RemoteFontBundleLoader>(FindObjectsInactive.Include);
#else
                fontLoader = FindObjectOfType<RemoteFontBundleLoader>(true);
#endif
            }

            if (fontLoader == null)
            {
                if (!_fontLoaderMissingLogged)
                {
                    _fontLoaderMissingLogged = true;
                    FineLocalizationLogger.Log(
                        "[FineLocalization] Nenhum RemoteFontBundleLoader na cena. Só idiomas cobertos pelas fontes embutidas serão aplicados."
                    );
                }

                return null;
            }

            // Um loader inativo nunca roda OnEnable nem consegue iniciar coroutines, então o
            // download da fonte simplesmente não aconteceria. Ativamos antes de comandá-lo.
            var go = fontLoader.gameObject;
            if (!go.activeSelf)
                go.SetActive(true);

            if (!fontLoader.enabled)
                fontLoader.enabled = true;

            if (!go.activeInHierarchy)
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] RemoteFontBundleLoader '{go.name}' está sob um pai desativado. Ative-o para permitir o download da fonte."
                );
            }

            return fontLoader;
        }

        // --------------------------------------------------------------- CSVs

        private bool UsesRemoteCsv
        {
            get
            {
                switch (source)
                {
                    case CsvSource.Remote:
                        return true;
                    case CsvSource.Bundled:
                        return false;
                    default:
#if UNITY_WEBGL && !UNITY_EDITOR
                        // O Google Sheets responde ao export com um redirect sem cabeçalho CORS,
                        // que o navegador bloqueia. Só baixamos se houver um proxy configurado.
                        return !string.IsNullOrWhiteSpace(csvUrlOverride);
#else
                        return true;
#endif
                }
            }
        }

        private IEnumerator DownloadAllSheets(Action<bool> onComplete)
        {
            var sources = LocalizationSettings.Instance != null
                ? LocalizationSettings.Instance.GetActiveSources()
                : null;

            if (sources == null || sources.Count == 0)
            {
                FineLocalizationLogger.LogWarning("[FineLocalization] Nenhuma source configurada em LocalizationSettings.");
                onComplete?.Invoke(false);
                yield break;
            }

            var allOk = true;

            foreach (var localizationSource in sources)
            {
                if (localizationSource == null ||
                    string.IsNullOrEmpty(localizationSource.TableId) ||
                    localizationSource.Sheets == null ||
                    localizationSource.Sheets.Count == 0)
                {
                    FineLocalizationLogger.LogWarning("[FineLocalization] Source sem TableId ou sem sheets. Ignorada.");
                    allOk = false;
                    continue;
                }

                foreach (var sheet in localizationSource.Sheets)
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.Name))
                        continue;

                    var url = BuildCsvUrl(localizationSource.TableId, sheet.Id);
                    string content = null;
                    yield return DownloadSheet(url, sheet.Name, csv => content = csv);

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        allOk = false;
                        continue;
                    }

                    _csv[sheet.Name] = content;
                    SaveCsvToDisk(sheet.Name, content);
                }
            }

            // Uma falha de rede não é fatal: Read() cai no CSV persistido em disco e, depois,
            // no TextAsset do build. O jogo abre traduzido com os dados da última sessão.
            onComplete?.Invoke(allOk && _csv.Count > 0);
        }

        private IEnumerator DownloadSheet(string url, string sheetName, Action<string> onComplete)
        {
            var attempts = Mathf.Max(1, network.maxAttempts);
            string lastFailure = null;

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                using (var request = UnityWebRequest.Get(url))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = Mathf.Max(1, network.timeoutSeconds);

                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var csv = Encoding.UTF8.GetString(request.downloadHandler.data);

                        // Página de login: a planilha não está compartilhada publicamente.
                        if (!csv.Contains("signin/identifier"))
                        {
                            onComplete?.Invoke(csv);
                            yield break;
                        }

                        lastFailure = "A planilha não é pública. Compartilhe como 'Qualquer pessoa com o link pode ver'.";
                    }
                    else
                    {
                        lastFailure = DescribeFailure(request, url);
                    }

                    var details = lastFailure;
                    FineLocalizationLogger.LogWarning(
                        () => $"[FineLocalization] Falha ao baixar '{sheetName}' (tentativa {attempt}/{attempts}). {details}"
                    );
                }

                if (attempt < attempts && network.retryDelaySeconds > 0f)
                    yield return new WaitForSecondsRealtime(network.retryDelaySeconds);
            }

            // Debug.LogError direto, sem o gate de EnableLogs: uma planilha que não baixa deixa o
            // jogo sem tradução em produção, onde os logs estão desligados por padrão.
            Debug.LogError($"[FineLocalization] Não foi possível baixar '{sheetName}' após {attempts} tentativa(s). {lastFailure}");
            onComplete?.Invoke(null);
        }

        private string BuildCsvUrl(string tableId, long sheetId)
        {
            var pattern = string.IsNullOrWhiteSpace(csvUrlOverride) ? GoogleExportUrl : csvUrlOverride;
            return string.Format(pattern, tableId, sheetId);
        }

        private static string DescribeFailure(UnityWebRequest request, string url)
        {
            var description = $"Resultado: {request.result}, HTTP: {request.responseCode}, Erro: '{request.error}', URL: {url}";

#if UNITY_WEBGL && !UNITY_EDITOR
            // O navegador esconde o motivo real de um bloqueio CORS: a request volta como
            // ConnectionError com responseCode 0. Essa assinatura é o único jeito de inferir
            // CORS aqui — a mensagem verdadeira só aparece no console do navegador (F12).
            if (request.responseCode == 0 &&
                (request.result == UnityWebRequest.Result.ConnectionError ||
                 request.result == UnityWebRequest.Result.ProtocolError))
            {
                description +=
                    " | Provável bloqueio de CORS: confirme no console do navegador (F12). Configure Csv Url Override " +
                    "com um proxy/CDN com CORS, ou use Csv Source = Bundled.";
            }
#endif

            return description;
        }

        private static string CsvPathFor(string sheetName) => Path.Combine(PersistentCsvDir, sheetName + ".csv");

        private void PersistCsvContent(string sheetName, string content)
        {
            _csv[sheetName] = content;
            SaveCsvToDisk(sheetName, content);
        }

        private static void SaveCsvToDisk(string sheetName, string content)
        {
            try
            {
                if (!Directory.Exists(PersistentCsvDir))
                    Directory.CreateDirectory(PersistentCsvDir);

                File.WriteAllText(CsvPathFor(sheetName), content, Encoding.UTF8);
            }
            catch (Exception e)
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Erro ao salvar o CSV '{sheetName}': {e.Message}");
            }
        }

        // ------------------------------------------------------- Compatibilidade v2

        private static Action<bool> _legacyComplete = _ => { };
        private static string _legacyRequestedLanguage;

        private static void NotifyLegacyComplete(bool success) => _legacyComplete?.Invoke(success);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLegacyState()
        {
            _legacyComplete = _ => { };
            _legacyRequestedLanguage = null;
        }

        [Obsolete("Use Localization.OnReady.")]
        public static event Action<bool> OnDownloadLocalizationComplete
        {
            add
            {
                _legacyComplete += value;
                if (Localization.IsReady)
                    value?.Invoke(Localization.LoadSucceeded);
            }
            remove => _legacyComplete -= value;
        }

        [Obsolete("Use Localization.OnReady.")]
        public static event Action<bool> OnAllSheetsDownloadedComplete
        {
            add
            {
                _legacyComplete += value;
                if (Localization.IsReady)
                    value?.Invoke(Localization.LoadSucceeded);
            }
            remove => _legacyComplete -= value;
        }

        [Obsolete("Use Localization.IsReady.")]
        public static bool IsLocalizationReady => Localization.IsReady;

        [Obsolete("Use Localization.LoadSucceeded.")]
        public static bool LastLocalizationSucceeded => Localization.LoadSucceeded;

        [Obsolete("Use Localization.Language.")]
        public static string RequestedLanguage => _legacyRequestedLanguage;

        [Obsolete("Use Localization.SetLanguage.")]
        public static bool HasExplicitRequestedLanguage => !string.IsNullOrEmpty(_legacyRequestedLanguage);

        [Obsolete("Use Localization.SetLanguage.")]
        public static void SetRequestedLanguage(string language)
        {
            _legacyRequestedLanguage = LanguageCode.Normalize(language);
            Localization.SetLanguage(language);
        }

        [Obsolete("Use Localization.Reload.")]
        public void DownloadSheets() => Reload();

        [Obsolete("Use Localization.Reload.")]
        public void DownloadSheetsWithCallback(Action<bool, Dictionary<string, string>> callback)
        {
            Reload(ok => callback?.Invoke(ok, _csv));
        }
    }
}
