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
        [Tooltip("Opcional. Se definido, carrega o fallback de fonte remoto antes de aplicar os CSVs baixados.")]
        [SerializeField] private RemoteFontBundleLoader remoteFontBundleLoader;

        [SerializeField] private bool loadRemoteFontBeforeApplyingLocalization = true;

        private static string PersistentCsvDir =>
            Path.Combine(Application.persistentDataPath, "FineLocalization/Resources/Localization");

        private const string UrlPattern =
            "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";

        public static event Action<bool> OnDownloadLocalizationComplete = success => { };
        public static event Action<bool> OnAllSheetsDownloadedComplete;

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
        private IEnumerator NotifyLocalizationAlreadyReady()
        {
            yield return null;

            UseBundledCsvs();
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

            if (ShouldUseBundledCsvs())
            {
                UseBundledCsvs();
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

                var sourceSuccess = true;

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
                        sourceSuccess = false;
                        allSourcesSuccess = false;
                        continue;
                    }

                    _csvData[sheet.Name] = csvContent;
                    yield return SaveCsvToDisk(sheet.Name, csvContent);

                    if (delayBetweenSheets > 0f)
                        yield return new WaitForSecondsRealtime(delayBetweenSheets);
                }

                OnDownloadLocalizationComplete?.Invoke(sourceSuccess);

                if (sourceSuccess)
                {
                    if (loadRemoteFontBeforeApplyingLocalization && remoteFontBundleLoader != null)
                    {
                        yield return remoteFontBundleLoader.EnsureFontForLanguage(LocalizationManager.Language);
                    }

                    LocalizationManager.LoadFromCsvMap(new Dictionary<string, string>(_csvData));
                }
            }

            OnAllSheetsDownloadedComplete?.Invoke(allSourcesSuccess && _csvData.Count > 0);
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

        private void UseBundledCsvs()
        {
            LocalizationManager.RuntimeCsvResolver = GetCsvContent;
            LocalizationManager.RuntimeCsvPersistenceHook = PersistCsvContent;
            LocalizationManager.ReloadAll();

            FineLocalizationLogger.Log(
                "[FineLocalization] Runtime Google Sheets download skipped on WebGL. Using bundled CSV TextAssets."
            );

            OnDownloadLocalizationComplete?.Invoke(true);
            OnAllSheetsDownloadedComplete?.Invoke(true);
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

            callback?.Invoke(
                _csvData.Count > 0,
                new Dictionary<string, string>(_csvData)
            );
        }
    }
}
