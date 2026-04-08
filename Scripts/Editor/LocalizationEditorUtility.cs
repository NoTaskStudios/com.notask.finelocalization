using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using com.notask.finelocalization.Scripts.Runtime.Utils;
using FineLocalization.Runtime;
using FineLocalization.Utils;
using Newtonsoft.Json;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FineLocalization.Editor
{
    public static class LocalizationEditorUtility
    {
        public static void DownloadGoogleSheets(this LocalizationSettings targetSettings,
            Action callback = null)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(DownloadGoogleSheetsCoroutine(targetSettings,callback));
        }

        public static IEnumerator DownloadGoogleSheetsCoroutine(this LocalizationSettings targetSettings,
            Action callback = null, bool silent = false)
        {
            if (targetSettings.Sources.Count == 0)
            {
                EditorUtility.DisplayDialog("[FineLocalization] Error", "No Table Ids configured.", "OK");
                yield break;
            }

            if (targetSettings.SaveFolder == null)
            {
                EditorUtility.DisplayDialog("[FineLocalization] Error", "Save Folder is not set.", "OK");
                yield break;
            }

            if ((DateTime.UtcNow - LocalizationSettings.Timestamp).TotalSeconds < 2)
            {
                if (EditorUtility.DisplayDialog("[FineLocalization] Message", "Too many requests! Try again later.", "OK"))
                    yield break;
            }

            LocalizationSettings.Timestamp = DateTime.UtcNow;

            if (!silent)
                ClearSaveFolder();

            var allSheets = targetSettings.Sources.SelectMany(s => s.Sheets).ToList();
            var total = allSheets.Count;
            int current = 0;

            foreach (var source in targetSettings.Sources)
            {
                foreach (var sheet in source.Sheets)
                {
                    current++;
                    var progress = (float)current / total;
                    var url = string.Format(LocalizationSettings.UrlPattern, source.TableId, sheet.Id);

                    //Debug.log($"Downloading <color=grey>{url}</color>");

                    var request = UnityWebRequest.Get(url);

                    if (EditorUtility.DisplayCancelableProgressBar("Downloading sheets...",
                            $"[{(int)(progress * 100)}%] Downloading {sheet.Name}...", progress))
                    {
                        EditorUtility.ClearProgressBar();
                        yield break;
                    }

                    yield return request.SendWebRequest();

                    var error = request.error ?? (request.downloadHandler.text.Contains("signin/identifier") ? "Access denied to document." : null);

                    if (string.IsNullOrEmpty(error))
                    {
                        var path = Path.Combine(AssetDatabase.GetAssetPath(targetSettings.SaveFolder), sheet.Name + ".csv");
                        File.WriteAllBytes(path, request.downloadHandler.data);
                        AssetDatabase.Refresh();
                        sheet.TextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                        EditorUtility.SetDirty(targetSettings);
                        //Debug.log($"[FineLocalization] Sheet <color=yellow>{sheet.Name}</color> saved to <color=grey>{path}</color>");
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
                var files = Directory.GetFiles(AssetDatabase.GetAssetPath(targetSettings.SaveFolder));
                foreach (var file in files)
                    File.Delete(file);
            }
        }

        public static void ResolveGoogleSheets(this LocalizationSettings settings)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(settings.ResolveGoogleSheetsCoroutine());
        }

        public static IEnumerator ResolveGoogleSheetsCoroutine(this LocalizationSettings settings)
        {
            foreach (var source in settings.Sources)
            {
                if (string.IsNullOrEmpty(source.TableId))
                {
                    //Debug.logWarning("[FineLocalization] Skipped empty TableId.");
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

        private static string GetInternalError(UnityWebRequest request)
        {
            var matches = Regex.Matches(request.downloadHandler.text, @">(?<Message>.+?)<\/div>");
            if (matches.Count == 0 && !request.downloadHandler.text.Contains("Google Script ERROR:")) return null;
            return matches.Count > 0 ? matches[1].Groups["Message"].Value.Replace("quot;", "") : request.downloadHandler.text;
        }
    }
}
