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

        private static string PersistentCsvDir =>
            Path.Combine(Application.persistentDataPath, "FineLocalization/Resources/Localization");

        private const string UrlPattern =
            "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";

        private static readonly string[] LanguageQueryKeys =
        {
            "lang", "language", "locale", "culture", "lng"
        };

        public static event Action<bool> OnDownloadLocalizationComplete = _ => { };
        public static event Action<bool> OnAllSheetsDownloadedComplete = _ => { };

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
        public static string RequestedLanguage { get; set; }

        public static void SetRequestedLanguage(string language)
        {
            RequestedLanguage = language;
        }

        private readonly Dictionary<string, string> _csvData = new();

        private void Start()
        {
            if (downloadOnStart)
            {
                StartCoroutine(DownloadSheetsRuntime());
            }
            else
            {
                StartCoroutine(NotifyLocalizationAlreadyReady());
            }
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
            yield return UseBundledCsvs();
        }
        /// <summary>
        /// Baixa todos os sheets configurados em runtime.
        /// </summary>
        public void DownloadSheets()
        {
            StartCoroutine(DownloadSheetsRuntime());
        }

        public IEnumerator DownloadSheetsRuntime()
        {
            LocalizationManager.RuntimeCsvResolver = GetCsvContent;
            LocalizationManager.RuntimeCsvPersistenceHook = PersistCsvContent;
            _csvData.Clear();

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

                    OnDownloadLocalizationComplete?.Invoke(false);
                    OnAllSheetsDownloadedComplete?.Invoke(false);
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
                yield return ApplyLocalizationWithOptionalRemoteFont(_csvData);

            MarkReady(fullSuccess);
            OnDownloadLocalizationComplete?.Invoke(fullSuccess);
            OnAllSheetsDownloadedComplete?.Invoke(fullSuccess);
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

            var targetLanguage = ResolveRequestedLanguageCandidate();

            LocalizationManager.ReloadAll(targetLanguage, false);
            targetLanguage = LocalizationManager.Language;

            var shouldLoadRemoteFont = ShouldLoadRemoteFont(targetLanguage);
            if (shouldLoadRemoteFont)
            {
                yield return remoteFontBundleLoader.EnsureFontForLanguage(targetLanguage);
            }

            IgnoreNextRemoteFontLocalizationEvent();
            LocalizationManager.Refresh();

            if (shouldLoadRemoteFont)
                yield return remoteFontBundleLoader.RebuildCurrentTexts();

            FineLocalizationLogger.Log(
                "[FineLocalization] Using bundled CSV TextAssets (downloadOnStart = false or WebGL CORS fallback)."
            );

            MarkReady(true);
            OnDownloadLocalizationComplete?.Invoke(true);
            OnAllSheetsDownloadedComplete?.Invoke(true);
        }

        private IEnumerator ApplyLocalizationWithOptionalRemoteFont(Dictionary<string, string> csvData)
        {
            var targetLanguage = ResolveRequestedLanguageCandidate();

            LocalizationManager.LoadFromCsvMap(csvData, targetLanguage, false);
            targetLanguage = LocalizationManager.Language;
            FineLocalizationLogger.Log(() => $"[FineLocalization] Runtime language resolved: requested='{ResolveRequestedLanguageCandidate()}', applied='{targetLanguage}'.");

            var shouldLoadRemoteFont = ShouldLoadRemoteFont(targetLanguage);
            if (shouldLoadRemoteFont)
            {
                yield return remoteFontBundleLoader.EnsureFontForLanguage(targetLanguage);
            }

            IgnoreNextRemoteFontLocalizationEvent();
            LocalizationManager.Refresh();

            if (shouldLoadRemoteFont)
                yield return remoteFontBundleLoader.RebuildCurrentTexts();
        }

        private bool ShouldLoadRemoteFont(string language)
        {
            return loadRemoteFontBeforeApplyingLocalization &&
                   remoteFontBundleLoader != null &&
                   remoteFontBundleLoader.ShouldLoadRemoteFontForLanguage(language);
        }

        private void IgnoreNextRemoteFontLocalizationEvent()
        {
            if (loadRemoteFontBeforeApplyingLocalization && remoteFontBundleLoader != null)
                remoteFontBundleLoader.IgnoreNextLocalizationChanged();
        }

        private string ResolveRequestedLanguageCandidate()
        {
            var requestedLanguage = !string.IsNullOrWhiteSpace(RequestedLanguage)
                ? RequestedLanguage
                : initialLanguageOverride;

            if (string.IsNullOrWhiteSpace(requestedLanguage))
                requestedLanguage = TryResolveLanguageFromLaunchUrl();

            if (string.IsNullOrWhiteSpace(requestedLanguage))
                requestedLanguage = LocalizationManager.Language;

            return requestedLanguage.Trim().Trim('\uFEFF').Replace('_', '-').ToLowerInvariant();
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
                        FineLocalizationLogger.LogWarning(
                            () => $"[FineLocalization] Falha ao baixar {sheetName}. " +
                                  $"Tentativa {attempt}/{attempts}. Erro: {request.error}"
                        );
                    }
                }

                if (attempt < attempts && retryDelaySeconds > 0f)
                    yield return new WaitForSecondsRealtime(retryDelaySeconds);
            }

            FineLocalizationLogger.LogWarning(
                () => $"[FineLocalization] Não foi possível baixar {sheetName} após {attempts} tentativa(s)."
            );

            onComplete?.Invoke(false, null);
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
