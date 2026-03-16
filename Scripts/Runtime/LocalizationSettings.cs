using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FineLocalization.Utils;
using Newtonsoft.Json;

using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using com.notask.finelocalization.Scripts.Runtime.Utils;
using UnityEditor;
using Unity.EditorCoroutines.Editor;
#endif

namespace FineLocalization.Runtime
{
    [CreateAssetMenu(fileName = "LocalizationSettings", menuName = "◆ Simple Localization/Settings")]
    public class LocalizationSettings : ScriptableObject
    {
        public enum LocalizationMode { Production, Development }
        public LocalizationMode Mode = LocalizationMode.Production;
        public List<LocalizationSource> GetActiveSources() => 
        Mode == LocalizationMode.Development ? DevSources : Sources;
        public List<LocalizationSource> Sources = new();
        public List<LocalizationSource> DevSources = new();
        public UnityEngine.Object SaveFolder;
        public int skip = 0;
        
        public static string UrlPattern = "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";
        public static DateTime Timestamp;

        private static LocalizationSettings _instance;
        public static LocalizationSettings Instance
        {
            get
            {
                if (_instance == null) _instance = LoadSettings();
                return _instance;
            }
        }

        public static event Action OnRunEditor = () => { };

        private static LocalizationSettings LoadSettings()
        {
            const string folderPath = "Assets/FineLocalization/Resources";
            const string settingsPath = folderPath + "/LocalizationSettings.asset";
            const string localizationFolderPath = folderPath + "/Localization";

#if UNITY_EDITOR
            // Garante a existência das pastas
            if (!AssetDatabase.IsValidFolder("Assets/FineLocalization"))
                AssetDatabase.CreateFolder("Assets", "FineLocalization");

            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets/FineLocalization", "Resources");

            if (!AssetDatabase.IsValidFolder(localizationFolderPath))
                AssetDatabase.CreateFolder(folderPath, "Localization");
#endif

            // Tenta carregar o asset
            var settings = Resources.Load<LocalizationSettings>("LocalizationSettings");

#if UNITY_EDITOR
            if (settings == null)
            {
                settings = CreateInstance<LocalizationSettings>();

                // Cria o asset fora do package
                AssetDatabase.CreateAsset(settings, settingsPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[FineLocalization] Criado novo LocalizationSettings em: {settingsPath}");
            }

            // Garante que a pasta SaveFolder aponte corretamente
            if (settings.SaveFolder == null)
            {
                settings.SaveFolder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(localizationFolderPath);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
#else
    if (settings == null)
        throw new Exception($"Localization settings não encontrado em: {settingsPath}");
#endif

            return settings;
        }


#if UNITY_EDITOR

        public void Reset()
        {
            Sources = new List<LocalizationSource>
            {
                new LocalizationSource
                {
                    TableId = Constants.ExampleTableId,
                    Sheets = Constants.ExampleSheets.Select(i => new Sheet { Name = i.Key, Id = i.Value }).ToList()
                }
            };

            SaveFolder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(@"Assets/FineLocalization/Resources/Localization");
        }

        public void DisplayMode()
        {
            var prev = Mode;

            var color = Mode == LocalizationMode.Development
                ? new Color(1f, 0.85f, 0.3f)
                : new Color(0.4f, 1f, 0.5f);

            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = color;

            Mode = (LocalizationMode) EditorGUILayout.EnumPopup("🌐 FineLocalization Mode", Mode);

            GUI.backgroundColor = originalColor;

            if (prev != Mode)
            {
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssets();
                Debug.Log($"[FineLocalization] Mode alterado para: {Mode}");
            }
        }


        public void DownloadGoogleSheets(Action callback = null)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(DownloadGoogleSheetsCoroutine(callback));
        }

        public IEnumerator DownloadGoogleSheetsCoroutine(Action callback = null, bool silent = false)
        {
            if (Sources.Count == 0)
            {
                EditorUtility.DisplayDialog("[FineLocalization] Error", "No Table Ids configured.", "OK");
                yield break;
            }

            if (SaveFolder == null)
            {
                EditorUtility.DisplayDialog("[FineLocalization] Error", "Save Folder is not set.", "OK");
                yield break;
            }

            if ((DateTime.UtcNow - Timestamp).TotalSeconds < 2)
            {
                if (EditorUtility.DisplayDialog("[FineLocalization] Message", "Too many requests! Try again later.", "OK"))
                    yield break;
            }

            Timestamp = DateTime.UtcNow;

            if (!silent)
                ClearSaveFolder();

            var allSheets = Sources.SelectMany(s => s.Sheets).ToList();
            var total = allSheets.Count;
            int current = 0;

            foreach (var source in Sources)
            {
                foreach (var sheet in source.Sheets)
                {
                    current++;
                    var progress = (float)current / total;
                    var url = string.Format(UrlPattern, source.TableId, sheet.Id);

                    Debug.Log($"Downloading <color=grey>{url}</color>");

                    var request = UnityWebRequest.Get(url);

                    if (EditorUtility.DisplayCancelableProgressBar("Downloading sheets...",
                            $"[{(int)(progress * 100)}%] Downloading {sheet.Name}...", progress))
                        yield break;

                    yield return request.SendWebRequest();

                    var error = request.error ?? (request.downloadHandler.text.Contains("signin/identifier") ? "Access denied to document." : null);

                    if (string.IsNullOrEmpty(error))
                    {
                        var path = Path.Combine(AssetDatabase.GetAssetPath(SaveFolder), sheet.Name + ".csv");
                        File.WriteAllBytes(path, request.downloadHandler.data);
                        AssetDatabase.Refresh();
                        sheet.TextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                        EditorUtility.SetDirty(this);
                        Debug.Log($"[FineLocalization] Sheet <color=yellow>{sheet.Name}</color> saved to <color=grey>{path}</color>");
                    }
                    else
                    {
                        EditorUtility.ClearProgressBar();
                        EditorUtility.DisplayDialog("[FineLocalization] Error", error.Contains("404") ? "Invalid TableId." : error, "OK");
                        yield break;
                    }
                }
            }

            yield return null;
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
            callback?.Invoke();

            if (!silent)
                EditorUtility.DisplayDialog("[FineLocalization] Message", $"{total} localization sheets downloaded!", "OK");

            void ClearSaveFolder()
            {
                var files = Directory.GetFiles(AssetDatabase.GetAssetPath(SaveFolder));
                foreach (var file in files)
                    File.Delete(file);
            }
        }

        public void ResolveGoogleSheets()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(ResolveGoogleSheetsCoroutine());
        }

        private IEnumerator ResolveGoogleSheetsCoroutine()
        {
            foreach (var source in Sources)
            {
                if (string.IsNullOrEmpty(source.TableId))
                {
                    Debug.LogWarning("[FineLocalization] Skipped empty TableId.");
                    continue;
                }

                var url = $"{Constants.SheetResolverUrl}?tableUrl={source.TableId}";
                using var request = UnityWebRequest.Get(url);

                EditorUtility.DisplayProgressBar("[FineLocalization] Resolving sheets...", $"Resolving {source.TableId}...", 1);
                yield return request.SendWebRequest();
                EditorUtility.ClearProgressBar();

                if (request.error != null)
                {
                    EditorUtility.DisplayDialog("[FineLocalization] Error", request.error, "OK");
                    continue;
                }

                var error = GetInternalError(request);
                if (error != null)
                {
                    EditorUtility.DisplayDialog("[FineLocalization] Error", "Sheet not found or permission denied.", "OK");
                    continue;
                }

                var sheetsDict = JsonConvert.DeserializeObject<Dictionary<string, long>>(request.downloadHandler.text);
                if (sheetsDict == null) continue;

                source.Sheets.Clear();
                foreach (var item in sheetsDict)
                {
                    source.Sheets.Add(new Sheet { Id = item.Value, Name = item.Key });
                }

                EditorUtility.DisplayDialog("[FineLocalization] Message", $"[{source.TableId}] Sheets resolved: {string.Join(", ", source.Sheets.Select(i => i.Name))}.", "OK");
            }
        }

        public static string GetInternalError(UnityWebRequest request)
        {
            var matches = Regex.Matches(request.downloadHandler.text, @">(?<Message>.+?)<\/div>");
            if (matches.Count == 0 && !request.downloadHandler.text.Contains("Google Script ERROR:")) return null;
            return matches.Count > 0 ? matches[1].Groups["Message"].Value.Replace("quot;", "") : request.downloadHandler.text;
        }

        public void DisplayHelp()
        {
            EditorGUILayout.HelpBox("1. Add Table Id(s) and Save Folder\n2. Press Resolve Sheets\n3. Press Download Sheets", MessageType.None);
        }

        public void DisplayButtons()
        {
            var buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fixedHeight = 30 };

            if (GUILayout.Button("↺ Resolve Sheets", buttonStyle)) ResolveGoogleSheets();
            if (GUILayout.Button("▼ Download Sheets", buttonStyle)) DownloadGoogleSheets();
            if (GUILayout.Button("❖ Open Editor", buttonStyle)) OnRunEditor();
        }

        public void DisplayWarnings()
        {
            if (Sources == null || Sources.Count == 0)
            {
                EditorGUILayout.HelpBox("No Table Ids configured.", MessageType.Warning);
            }
            else if (SaveFolder == null)
            {
                EditorGUILayout.HelpBox("Save Folder is not set.", MessageType.Warning);
            }
            else if (Sources.Any(s => s.Sheets.Count == 0))
            {
                EditorGUILayout.HelpBox("Some sources have no resolved sheets.", MessageType.Warning);
            }
            else if (Sources.Any(s => s.Sheets.Any(sh => sh.TextAsset == null)))
            {
                EditorGUILayout.HelpBox("Some sheets are not downloaded.", MessageType.Warning);
            }
        }

#endif
    }
}
