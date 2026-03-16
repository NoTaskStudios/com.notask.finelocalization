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

        private static string PersistentCsvDir =>
            Path.Combine(Application.persistentDataPath, "FineLocalization/Localization");

        private const string UrlPattern =
            "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";

        public static event Action<bool> OnDownloadLocalizationComplete = (success) => { };
        private readonly Dictionary<string, string> _csvData = new();
        
        private void Start()
        {
            if (downloadOnStart)
            {
                DownloadSheetsWithCallback((loaded, map) =>
                {
                    if (loaded) LocalizationManager.LoadFromCsvMap(map);
                });
            }
        }

        /// <summary>
        /// Baixa todos os sheets configurados em runtime (corrotina direta).
        /// </summary>
        public void DownloadSheets()
        {
            StartCoroutine(DownloadSheetsRuntime());
        }

        public IEnumerator DownloadSheetsRuntime()
        {
            var activeSources = LocalizationSettings.Instance.GetActiveSources();
            for (int s = 0; s < activeSources.Count; s++)
            {
                var source = activeSources[s];
                if (string.IsNullOrEmpty(source.TableId) || source.Sheets.Count == 0)
                {
                    Debug.LogError("[FineLocalization] Table Id ou Sheets estão vazios!");
                    OnDownloadLocalizationComplete?.Invoke(false);
                    yield break;
                }

                var allSuccess = true;

                for (var i = 0; i < source.Sheets.Count; i++)
                {
                    var sheet = source.Sheets[i];
                    var url = string.Format(UrlPattern, source.TableId, sheet.Id);

                    Debug.Log($"[FineLocalization] Baixando: {sheet.Name} - {url}");

                    using (var request = UnityWebRequest.Get(url))
                    {
                        request.downloadHandler = new DownloadHandlerBuffer();
                        yield return request.SendWebRequest();

                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            var csvContent = Encoding.UTF8.GetString(request.downloadHandler.data);

                            // Se cair em página de login, o arquivo não está público
                            if (csvContent.Contains("signin/identifier"))
                            {
                                Debug.LogError($"[FineLocalization] Acesso negado ao documento: {sheet.Name}");
                                allSuccess = false;
                                continue;
                            }

                            _csvData[sheet.Name] = csvContent;

                            Debug.Log($"[FineLocalization] Sheet {sheet.Name} baixado com sucesso!");
                        }
                        else
                        {
                            Debug.LogError($"[FineLocalization] Erro ao baixar {sheet.Name}: {request.error}");
                            allSuccess = false;
                        }
                    }

                    yield return new WaitForSeconds(0.1f);
                }

                OnDownloadLocalizationComplete?.Invoke(allSuccess);

                if (!allSuccess) yield break;

                Debug.Log("[FineLocalization] Todos os sheets foram baixados com sucesso!");

                // Atualiza o LocalizationManager para usar os CSVs recém-baixados
                LocalizationManager.LoadFromCsvMap(new Dictionary<string, string>(_csvData));
            }
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
                Debug.LogError($"[FineLocalization] CSV salvo em: {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FineLocalization] Erro ao salvar CSV {fileName}: {e.Message}");
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
                _csvData[sheetName] = content; // cache
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
                Debug.LogError($"[FineLocalization] Erro ao limpar dados: {e.Message}");
            }
        }

        /// <summary>
        /// Método para recarregar a localização manualmente (se desejar).
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
            callback?.Invoke(_csvData.Count > 0, new Dictionary<string, string>(_csvData));
        }
    }
}
 