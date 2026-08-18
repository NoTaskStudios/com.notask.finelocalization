using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FineLocalization.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FineLocalization.Editor
{
    /// <summary>
    /// Varre o projeto atrás das keys de localização REALMENTE usadas e cruza com as
    /// keys DEFINIDAS nos CSVs configurados em <see cref="LocalizationSettings"/>.
    ///
    /// Fontes de uso analisadas:
    ///   - Componentes em prefabs e cenas: LocalizedText (LocalizationKey),
    ///     LocalizedDropdown (LocalizationKeys[]), MultiLocale (key), LocaleComponent (key).
    ///   - Código C# em Assets/: chamadas Localize("..."), HasKey("..."), ChangeKey("...").
    ///
    /// Relatório (Console + arquivo .txt na raiz do projeto):
    ///   - NÃO USADAS  -> candidatas a remover da planilha.
    ///   - DUPLICADAS  -> mesma key aparece mais de uma vez no(s) CSV(s).
    ///   - FALTANDO    -> usada no jogo mas sem linha no CSV (typo / key removida).
    ///   - USADAS      -> lista completa com onde cada uma aparece.
    ///
    /// Limitação: keys montadas dinamicamente em código (ex.: "symbol_" + id) não são
    /// detectáveis por análise estática e podem aparecer como "NÃO USADAS".
    /// </summary>
    public static class FineLocalizationKeyUsageReport
    {
        private const string MenuRoot = "Tools/Fine Localization/Diagnostics/";

        [MenuItem(MenuRoot + "Report Used vs Unused (Prefabs + Code)", priority = 100)]
        private static void ReportFast() => Run(scanScenes: false);

        [MenuItem(MenuRoot + "Report Used vs Unused (Prefabs + Code + Scenes)", priority = 101)]
        private static void ReportFull() => Run(scanScenes: true);

        private static void Run(bool scanScenes)
        {
            try
            {
                var settings = LocalizationSettings.Instance;
                if (settings == null)
                {
                    Debug.LogError("[KeyReport] LocalizationSettings.Instance == null. Selecione/aponte o asset de settings antes.");
                    return;
                }

                // ---------- 1) Keys DEFINIDAS (+ ocorrências p/ duplicadas) ----------
                // key -> lista de "sheet:dataRowN"
                var definedOccurrences = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                int keyColumn = Mathf.Max(0, settings.skip);

                var sources = settings.GetActiveSources();
                if (sources == null || sources.Count == 0)
                {
                    Debug.LogError($"[KeyReport] Nenhuma source ativa em LocalizationSettings (Mode={settings.Mode}).");
                    return;
                }

                int sheetsParsed = 0;
                foreach (var source in sources)
                {
                    if (source?.Sheets == null) continue;
                    foreach (var sheet in source.Sheets)
                    {
                        if (sheet == null || string.IsNullOrWhiteSpace(sheet.Name)) continue;

                        var csv = sheet.TextAsset != null ? sheet.TextAsset.text : null;
                        if (string.IsNullOrWhiteSpace(csv))
                        {
                            Debug.LogWarning($"[KeyReport] Sheet '{sheet.Name}' sem TextAsset/CSV (baixe a planilha no Editor). Ignorada.");
                            continue;
                        }

                        // Reaproveita EXATAMENTE o parser do runtime (mesmas regras de aspas/CJK).
                        var lines = LocalizationManager.GetLines(csv);
                        for (int i = 1; i < lines.Count; i++) // linha 0 = header
                        {
                            var cols = LocalizationManager.GetColumns(lines[i]);
                            if (cols.Count <= keyColumn) continue;

                            var key = cols[keyColumn]?.Trim();
                            if (string.IsNullOrEmpty(key)) continue;

                            if (!definedOccurrences.TryGetValue(key, out var occ))
                                definedOccurrences[key] = occ = new List<string>();
                            occ.Add($"{sheet.Name}:dataRow{i}");
                        }
                        sheetsParsed++;
                    }
                }

                var definedKeys = new HashSet<string>(definedOccurrences.Keys, StringComparer.Ordinal);

                // ---------- 2) Keys USADAS ----------
                var used = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); // key -> locais

                void AddUsage(string key, string location)
                {
                    if (string.IsNullOrWhiteSpace(key)) return;
                    key = key.Trim();
                    if (!used.TryGetValue(key, out var set))
                        used[key] = set = new HashSet<string>();
                    set.Add(location);
                }

                ScanPrefabs(AddUsage);
                ScanCode(AddUsage);
                if (scanScenes) ScanScenes(AddUsage);

                var usedKeys = new HashSet<string>(used.Keys, StringComparer.Ordinal);

                // ---------- 3) Cruzamentos ----------
                var unused = definedKeys.Where(k => !usedKeys.Contains(k))
                                        .OrderBy(k => k, StringComparer.Ordinal).ToList();
                var missing = usedKeys.Where(k => !definedKeys.Contains(k))
                                      .OrderBy(k => k, StringComparer.Ordinal).ToList();
                var duplicates = definedOccurrences.Where(kv => kv.Value.Count > 1)
                                                   .OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();

                // ---------- 4) Relatório ----------
                var sb = new StringBuilder();
                sb.AppendLine("===== FineLocalization - Relatorio de Keys =====");
                sb.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}   Mode: {settings.Mode}   keyColumn(skip)={keyColumn}");
                sb.AppendLine($"Sheets lidos: {sheetsParsed}   Cenas escaneadas: {(scanScenes ? "SIM" : "NAO")}");
                sb.AppendLine($"Definidas: {definedKeys.Count} | Usadas: {usedKeys.Count} | Nao usadas: {unused.Count} | Faltando: {missing.Count} | Duplicadas: {duplicates.Count}");
                if (!scanScenes)
                    sb.AppendLine("ATENCAO: cenas NAO foram escaneadas. Keys usadas apenas em cenas aparecerao como 'nao usadas'. Use o comando '... + Scenes' para resultado confiavel.");
                sb.AppendLine();

                sb.AppendLine($"----- NAO USADAS ({unused.Count}) - candidatas a REMOVER da planilha -----");
                foreach (var k in unused) sb.AppendLine("  " + k);
                sb.AppendLine();

                sb.AppendLine($"----- DUPLICADAS no CSV ({duplicates.Count}) -----");
                foreach (var kv in duplicates) sb.AppendLine($"  {kv.Key}   ->   {string.Join(" , ", kv.Value)}");
                sb.AppendLine();

                sb.AppendLine($"----- FALTANDO ({missing.Count}) - usadas no jogo mas SEM linha no CSV (typo? key removida?) -----");
                foreach (var k in missing) sb.AppendLine($"  {k}   <-   {string.Join(" | ", used[k])}");
                sb.AppendLine();

                sb.AppendLine($"----- USADAS ({usedKeys.Count}) -----");
                foreach (var k in usedKeys.OrderBy(x => x, StringComparer.Ordinal))
                    sb.AppendLine($"  {k}   <-   {string.Join(" | ", used[k])}");

                var report = sb.ToString();

                // Arquivo na RAIZ do projeto (fora de Assets -> nao dispara import).
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                var path = Path.Combine(projectRoot, $"FineLocalization_KeyReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, report, new UTF8Encoding(false));

                // Console: resumo + blocos (cada bloco e um log separado, expansivel).
                Debug.Log($"[KeyReport] Definidas={definedKeys.Count} | Usadas={usedKeys.Count} | NAO usadas={unused.Count} | Faltando={missing.Count} | Duplicadas={duplicates.Count}\nRelatorio completo salvo em:\n{path}");

                if (unused.Count > 0)
                    Debug.LogWarning($"[KeyReport] NAO USADAS ({unused.Count}){(scanScenes ? "" : " [SEM cenas - rode '+ Scenes' p/ confirmar]")}:\n" + string.Join("\n", unused));
                if (duplicates.Count > 0)
                    Debug.LogWarning($"[KeyReport] DUPLICADAS ({duplicates.Count}):\n" +
                                     string.Join("\n", duplicates.Select(kv => $"{kv.Key}  ->  {string.Join(" , ", kv.Value)}")));
                if (missing.Count > 0)
                    Debug.LogWarning($"[KeyReport] FALTANDO ({missing.Count}):\n" +
                                     string.Join("\n", missing.Select(k => $"{k}  <-  {string.Join(" | ", used[k])}")));

                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(e);
            }
        }

        // ---------------- Scanners ----------------

        private static void ScanPrefabs(Action<string, string> add)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    if (EditorUtility.DisplayCancelableProgressBar("KeyReport", "Prefabs: " + path, (float)i / Mathf.Max(1, guids.Length)))
                        break;

                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) CollectFromHierarchy(go, "Prefab " + path, add);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private static void ScanScenes(Action<string, string> add)
        {
            // Salva (se o usuario quiser) as cenas modificadas antes de trocar de cena.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[KeyReport] Scan de cenas cancelado (havia cenas nao salvas).");
                return;
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path)) continue;

                    if (EditorUtility.DisplayCancelableProgressBar("KeyReport", "Cenas: " + path, (float)i / Mathf.Max(1, guids.Length)))
                        break;

                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects())
                        CollectFromHierarchy(root, "Cena " + path, add);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                try
                {
                    if (setup != null && setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
                        EditorSceneManager.RestoreSceneManagerSetup(setup);
                    else
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[KeyReport] Nao consegui restaurar as cenas abertas: " + e.Message);
                }
            }
        }

        private static void CollectFromHierarchy(GameObject root, string location, Action<string, string> add)
        {
            foreach (var c in root.GetComponentsInChildren<LocalizedText>(true))
                add(c.LocalizationKey, $"{location} > {GetPath(c.transform)} (LocalizedText)");

            foreach (var c in root.GetComponentsInChildren<LocalizedDropdown>(true))
                if (c.LocalizationKeys != null)
                    foreach (var k in c.LocalizationKeys)
                        add(k, $"{location} > {GetPath(c.transform)} (LocalizedDropdown)");

            foreach (var c in root.GetComponentsInChildren<MultiLocale>(true))
                add(ReadSerializedKey(c, "key"), $"{location} > {GetPath(c.transform)} (MultiLocale)");

            foreach (var c in root.GetComponentsInChildren<LocaleComponent>(true))
                add(ReadSerializedKey(c, "key"), $"{location} > {GetPath(c.transform)} (LocaleComponent)");
        }

        // Le campos [SerializeField] private (ex.: 'key') de forma segura via SerializedObject.
        private static string ReadSerializedKey(Component c, string propertyName)
        {
            if (c == null) return null;
            using var so = new SerializedObject(c);
            var p = so.FindProperty(propertyName);
            return p != null && p.propertyType == SerializedPropertyType.String ? p.stringValue : null;
        }

        private static void ScanCode(Action<string, string> add)
        {
            // Captura o 1o argumento string literal de Localize/HasKey/ChangeKey.
            var rx = new Regex("(?:Localize|HasKey|ChangeKey)\\s*\\(\\s*\"([^\"]+)\"", RegexOptions.Compiled);

            string[] files;
            try { files = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories); }
            catch { return; }

            foreach (var file in files)
            {
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                if (text.IndexOf("Localize", StringComparison.Ordinal) < 0 &&
                    text.IndexOf("HasKey", StringComparison.Ordinal) < 0 &&
                    text.IndexOf("ChangeKey", StringComparison.Ordinal) < 0) continue;

                var norm = file.Replace('\\', '/');
                var rel = norm.StartsWith(Application.dataPath)
                    ? "Assets" + norm.Substring(Application.dataPath.Length)
                    : norm;

                foreach (Match m in rx.Matches(text))
                    add(m.Groups[1].Value, "Codigo " + rel);
            }
        }

        private static string GetPath(Transform t)
        {
            var stack = new Stack<string>();
            while (t != null) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }
    }
}
