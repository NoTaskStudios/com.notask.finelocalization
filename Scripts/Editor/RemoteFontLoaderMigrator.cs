#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FineLocalization.Scripts.Runtime;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Migra a configuração do <c>RemoteFontBundleLoader</c> da v3.1 para o
    /// <see cref="RuntimeLocaleDownloader"/> da v3.2, que absorveu a fonte remota.
    ///
    /// A classe do loader não existe mais, então não dá para ler os campos por reflexão como o
    /// <see cref="LocaleComponentMigrator"/> faz: a Unity mostra "missing script" e não instancia
    /// nada. O que sobra em disco é o bloco YAML com os valores em texto — é dele que este
    /// migrador lê. A escrita é pela API normal (<see cref="SerializedObject"/>), então nada de
    /// reescrever YAML à mão.
    ///
    /// Exige cena/prefab em serialização de texto, que é o default da Unity. Arquivo binário é
    /// detectado e reportado em vez de falhar calado.
    /// </summary>
    public class RemoteFontLoaderMigrator : EditorWindow
    {
        /// <summary>GUID de Scripts/Runtime/RemoteFontBundleLoader.cs, deletado na v3.2.</summary>
        private const string LegacyLoaderGuid = "513469d413d6400abbb0ca8dc5329d17";

        private static readonly string[] SearchInAssets = { "Assets" };

        private readonly List<Candidate> _candidates = new();
        private readonly List<string> _report = new();

        private bool _includePrefabs = true;
        private bool _includeScenes = true;
        private bool _removeOrphans = true;
        private bool _scanned;
        private Vector2 _scroll;

        /// <summary>Um arquivo com loader legado e a configuração lida dele.</summary>
        private class Candidate
        {
            public string path;
            public bool isPrefab;
            public bool binary;
            public string gameObjectName;
            public LegacyFontConfig config;
        }

        /// <summary>Os campos do loader v3.1 que sobrevivem na v3.2.</summary>
        private class LegacyFontConfig
        {
            public string baseBundleUrl = string.Empty;
            public string gameId = string.Empty;
            public int rebuildBatchSize = -1;
            public readonly List<string> extraLatinPrefixes = new();
            public readonly List<string> forceRemoteFontPrefixes = new();
            public readonly List<TMP_FontAsset> mainFontAssets = new();
            public readonly List<TMP_FontAsset> coverageFallbackFontAssets = new();
            public int legacyMappings;

            public bool HasAnything =>
                baseBundleUrl.Length > 0 || gameId.Length > 0 || rebuildBatchSize > 0 ||
                extraLatinPrefixes.Count > 0 || forceRemoteFontPrefixes.Count > 0 ||
                mainFontAssets.Count > 0 || coverageFallbackFontAssets.Count > 0;
        }

        /// <summary>
        /// True quando algum prefab ou cena sob Assets/ ainda tem o componente da v3.1. O Hub usa
        /// para avisar antes do dev descobrir sozinho com um "missing script" na cena.
        /// </summary>
        public static bool HasLegacyLoaderInProject()
        {
            return ContainsLegacyLoader("t:Prefab") || ContainsLegacyLoader("t:Scene");
        }

        private static bool ContainsLegacyLoader(string filter)
        {
            var guids = AssetDatabase.FindAssets(filter, SearchInAssets);
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;

                try
                {
                    if (File.ReadAllText(path).IndexOf(LegacyLoaderGuid, StringComparison.Ordinal) >= 0)
                        return true;
                }
                catch
                {
                    // Arquivo ilegível não é motivo para derrubar o desenho da janela.
                }
            }

            return false;
        }

        [MenuItem("Tools/Fine Localization/Advanced/Migrate Remote Font Loader", false, 88)]
        public static void ShowWindow()
        {
            GetWindow<RemoteFontLoaderMigrator>("Remote Font Loader Migrator").minSize = new Vector2(520, 420);
        }

        private void OnGUI()
        {
            GUILayout.Label("Remote Font Loader Migrator", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                "A v3.2 juntou o RemoteFontBundleLoader no RuntimeLocaleDownloader. Este migrador lê a " +
                "configuração do componente antigo direto do arquivo da cena/prefab e escreve no " +
                "downloader, ligando Font Mode = Remote.\n\n" +
                "A lista Remote Font Mappings não é migrada de propósito: agora quem responde qual " +
                "bundle atende cada idioma é o manifesto gerado pelo Build Bundles.",
                MessageType.Info
            );

            if (EditorSettings.serializationMode == SerializationMode.ForceBinary)
            {
                EditorGUILayout.HelpBox(
                    "O projeto está em Force Binary. Cenas e prefabs binários não podem ser lidos, " +
                    "então os valores antigos serão perdidos e você precisará reconfigurar Base Bundle URL " +
                    "e as listas de fonte à mão. Para migrar sem perda, troque para Force Text em " +
                    "Project Settings → Editor → Asset Serialization, deixe a Unity reescrever os arquivos, " +
                    "e rode o migrador de novo.",
                    MessageType.Warning
                );
            }

            EditorGUILayout.Space(4);
            _includePrefabs = EditorGUILayout.Toggle("Incluir Prefabs (Assets/)", _includePrefabs);
            _includeScenes = EditorGUILayout.Toggle("Incluir Cenas (Assets/)", _includeScenes);
            _removeOrphans = EditorGUILayout.Toggle("Remover o componente órfão", _removeOrphans);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Escanear", GUILayout.Height(30)))
                Scan();

            if (_scanned)
                DrawResults();

            DrawReport();
        }

        private void DrawResults()
        {
            EditorGUILayout.Space(6);

            if (_candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Nenhum RemoteFontBundleLoader encontrado. Nada a migrar — ou já foi feito.",
                    MessageType.Info
                );
                return;
            }

            EditorGUILayout.HelpBox(
                $"{_candidates.Count} arquivo(s) com o loader antigo.",
                MessageType.Warning
            );

            foreach (var candidate in _candidates)
            {
                var summary = candidate.binary
                    ? "serialização binária — valores não legíveis"
                    : DescribeConfig(candidate.config);

                EditorGUILayout.LabelField($"  {Path.GetFileName(candidate.path)}", summary, EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Migrar", GUILayout.Height(30)))
            {
                var ok = EditorUtility.DisplayDialog(
                    "Confirmar migração",
                    $"Vou reescrever {_candidates.Count} arquivo(s) sob Assets/.\n\n" +
                    "Faça commit ou backup antes de continuar.",
                    "Migrar",
                    "Cancelar"
                );

                if (ok)
                    Migrate();
            }
        }

        private void DrawReport()
        {
            if (_report.Count == 0)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Relatório", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(120));
            foreach (var line in _report)
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ Scan

        private void Scan()
        {
            _candidates.Clear();
            _report.Clear();
            _scanned = true;

            if (_includePrefabs)
                CollectCandidates("t:Prefab", isPrefab: true);

            if (_includeScenes)
                CollectCandidates("t:Scene", isPrefab: false);

            _report.Add($"Escaneado: {_candidates.Count} arquivo(s) com o loader antigo.");
        }

        private void CollectCandidates(string filter, bool isPrefab)
        {
            var guids = AssetDatabase.FindAssets(filter, SearchInAssets);
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;

                string text;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch (Exception e)
                {
                    _report.Add($"Não foi possível ler '{path}': {e.Message}");
                    continue;
                }

                if (text.IndexOf(LegacyLoaderGuid, StringComparison.Ordinal) < 0)
                    continue;

                // Arquivo binário: o GUID pode aparecer, mas o bloco não é parseável.
                var binary = text.IndexOf("%YAML", StringComparison.Ordinal) < 0;

                _candidates.Add(new Candidate
                {
                    path = path,
                    isPrefab = isPrefab,
                    binary = binary,
                    gameObjectName = binary ? string.Empty : ReadGameObjectName(text),
                    config = binary ? new LegacyFontConfig() : ParseLegacyConfig(text)
                });
            }
        }

        // ------------------------------------------------------------ YAML (leitura)

        /// <summary>Documentos YAML de um arquivo de cena/prefab, separados por "--- ".</summary>
        private static List<string> SplitDocuments(string text)
        {
            var documents = new List<string>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var current = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    if (current.Count > 0)
                        documents.Add(string.Join("\n", current));

                    current.Clear();
                }

                current.Add(line);
            }

            if (current.Count > 0)
                documents.Add(string.Join("\n", current));

            return documents;
        }

        private static string FindLoaderDocument(string text)
        {
            foreach (var document in SplitDocuments(text))
            {
                if (document.IndexOf(LegacyLoaderGuid, StringComparison.Ordinal) >= 0 &&
                    document.IndexOf("MonoBehaviour:", StringComparison.Ordinal) >= 0)
                    return document;
            }

            return null;
        }

        /// <summary>Nome do GameObject que hospeda o loader, para desambiguar cena com vários downloaders.</summary>
        private static string ReadGameObjectName(string text)
        {
            var document = FindLoaderDocument(text);
            if (document == null)
                return string.Empty;

            var reference = Regex.Match(document, @"m_GameObject:\s*\{fileID:\s*(-?\d+)\}");
            if (!reference.Success)
                return string.Empty;

            var fileId = reference.Groups[1].Value;
            foreach (var candidate in SplitDocuments(text))
            {
                if (!Regex.IsMatch(candidate, @"^---\s+!u!1\s+&" + Regex.Escape(fileId) + @"\b", RegexOptions.Multiline))
                    continue;

                var name = Regex.Match(candidate, @"^\s{2}m_Name:\s*(.*)$", RegexOptions.Multiline);
                if (name.Success)
                    return name.Groups[1].Value.Trim();
            }

            return string.Empty;
        }

        /// <summary>
        /// Lê os campos do bloco do loader. Só olha chaves no indent de 2 espaços (o nível dos
        /// campos do MonoBehaviour) e para de consumir uma lista quando aparece a próxima chave.
        /// </summary>
        private static LegacyFontConfig ParseLegacyConfig(string text)
        {
            var config = new LegacyFontConfig();
            var document = FindLoaderDocument(text);
            if (document == null)
                return config;

            var lines = document.Split('\n');
            string listKey = null;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();

                // Item de lista do nível dos campos.
                if (listKey != null && line.StartsWith("  - ", StringComparison.Ordinal))
                {
                    AddListItem(config, listKey, line.Substring(4).Trim());
                    continue;
                }

                var match = Regex.Match(line, @"^\s{2}([A-Za-z_][A-Za-z0-9_]*):\s?(.*)$");
                if (!match.Success)
                    continue;

                var key = match.Groups[1].Value;
                var value = match.Groups[2].Value.Trim();
                listKey = null;

                switch (key)
                {
                    case "baseBundleUrl":
                        config.baseBundleUrl = Unquote(value);
                        break;
                    case "gameId":
                        config.gameId = Unquote(value);
                        break;
                    case "rebuildBatchSize":
                        if (int.TryParse(value, out var batch))
                            config.rebuildBatchSize = batch;
                        break;
                    case "extraLatinPrefixes":
                    case "forceRemoteFontPrefixes":
                    case "mainFontAssets":
                    case "coverageFallbackFontAssets":
                    case "bundles":
                        // Lista vazia vem como "key: []" na mesma linha.
                        if (value != "[]")
                            listKey = key;
                        break;
                }
            }

            return config;
        }

        private static void AddListItem(LegacyFontConfig config, string key, string item)
        {
            switch (key)
            {
                case "extraLatinPrefixes":
                    config.extraLatinPrefixes.Add(Unquote(item));
                    break;
                case "forceRemoteFontPrefixes":
                    config.forceRemoteFontPrefixes.Add(Unquote(item));
                    break;
                case "mainFontAssets":
                    AddFont(config.mainFontAssets, item);
                    break;
                case "coverageFallbackFontAssets":
                    AddFont(config.coverageFallbackFontAssets, item);
                    break;
                case "bundles":
                    // Só conta, para o relatório: os mapeamentos viraram o manifesto gerado.
                    if (item.StartsWith("languagePrefix:", StringComparison.Ordinal))
                        config.legacyMappings++;
                    break;
            }
        }

        /// <summary>Resolve "{fileID: N, guid: ..., type: 2}" no TMP_FontAsset correspondente.</summary>
        private static void AddFont(List<TMP_FontAsset> target, string item)
        {
            var match = Regex.Match(item, @"guid:\s*([0-9a-fA-F]{32})");
            if (!match.Success)
                return;

            var path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
            if (string.IsNullOrEmpty(path))
                return;

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null)
            {
                // A fonte pode ser sub-asset de outro arquivo.
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is TMP_FontAsset sub)
                    {
                        font = sub;
                        break;
                    }
                }
            }

            if (font != null && !target.Contains(font))
                target.Add(font);
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '\'' && value[value.Length - 1] == '\'') || (value[0] == '"' && value[value.Length - 1] == '"')))
                return value.Substring(1, value.Length - 2);

            return value;
        }

        // ------------------------------------------------------------- Migração

        private void Migrate()
        {
            _report.Clear();
            var migrated = 0;

            foreach (var candidate in _candidates)
            {
                try
                {
                    if (candidate.isPrefab ? MigratePrefab(candidate) : MigrateScene(candidate))
                        migrated++;
                }
                catch (Exception e)
                {
                    _report.Add($"ERRO em '{candidate.path}': {e.GetType().Name}: {e.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _report.Add($"Migrados {migrated} de {_candidates.Count} arquivo(s).");
            _report.Add("Próximo passo: Tools → Fine Localization → Setup and Update → Build Bundles, " +
                        "para gerar o manifesto que substitui os Remote Font Mappings.");

            Scan();
        }

        private bool MigratePrefab(Candidate candidate)
        {
            var root = PrefabUtility.LoadPrefabContents(candidate.path);
            if (root == null)
            {
                _report.Add($"Não foi possível abrir o prefab '{candidate.path}'.");
                return false;
            }

            try
            {
                var changed = ApplyToHierarchy(root, candidate);
                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, candidate.path);

                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private bool MigrateScene(Candidate candidate)
        {
            var scene = EditorSceneManager.OpenScene(candidate.path, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                _report.Add($"Não foi possível abrir a cena '{candidate.path}'.");
                return false;
            }

            var changed = false;
            foreach (var root in scene.GetRootGameObjects())
                changed |= ApplyToHierarchy(root, candidate);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            return changed;
        }

        private bool ApplyToHierarchy(GameObject root, Candidate candidate)
        {
            var downloaders = root.GetComponentsInChildren<RuntimeLocaleDownloader>(true);
            var target = PickDownloader(downloaders, candidate);

            var changed = false;

            if (target != null)
            {
                if (WriteConfig(target, candidate))
                    changed = true;
            }
            else if (downloaders.Length == 0)
            {
                _report.Add(
                    $"'{candidate.path}': nenhum RuntimeLocaleDownloader aqui. Adicione um e rode de novo, " +
                    "ou configure Base Bundle URL à mão."
                );
            }
            else
            {
                _report.Add(
                    $"'{candidate.path}': mais de um RuntimeLocaleDownloader e não deu para decidir qual " +
                    $"(o loader estava em '{candidate.gameObjectName}'). Configure à mão."
                );
            }

            if (_removeOrphans && RemoveMissingScripts(root))
                changed = true;

            return changed;
        }

        private static RuntimeLocaleDownloader PickDownloader(RuntimeLocaleDownloader[] downloaders, Candidate candidate)
        {
            if (downloaders == null || downloaders.Length == 0)
                return null;

            if (downloaders.Length == 1)
                return downloaders[0];

            // Vários: prefere o que está no mesmo GameObject onde o loader estava.
            if (!string.IsNullOrEmpty(candidate.gameObjectName))
            {
                foreach (var downloader in downloaders)
                {
                    if (string.Equals(downloader.gameObject.name, candidate.gameObjectName, StringComparison.Ordinal))
                        return downloader;
                }
            }

            return null;
        }

        private bool WriteConfig(RuntimeLocaleDownloader downloader, Candidate candidate)
        {
            var config = candidate.config;

            Undo.RegisterCompleteObjectUndo(downloader, "Migrate Remote Font Loader");

            var so = new SerializedObject(downloader);
            var fontMode = so.FindProperty("fontMode");
            var fonts = so.FindProperty("remoteFonts");

            if (fontMode == null || fonts == null)
            {
                _report.Add($"'{candidate.path}': o downloader não expõe fontMode/remoteFonts. Pacote desatualizado?");
                return false;
            }

            fontMode.enumValueIndex = (int)RuntimeLocaleDownloader.FontMode.Remote;

            if (config.baseBundleUrl.Length > 0)
                fonts.FindPropertyRelative("baseBundleUrl").stringValue = config.baseBundleUrl;

            if (config.gameId.Length > 0)
                fonts.FindPropertyRelative("gameId").stringValue = config.gameId;

            if (config.rebuildBatchSize > 0)
                fonts.FindPropertyRelative("rebuildBatchSize").intValue = config.rebuildBatchSize;

            WriteStringList(fonts.FindPropertyRelative("extraLatinPrefixes"), config.extraLatinPrefixes);
            WriteStringList(fonts.FindPropertyRelative("forceRemoteFontPrefixes"), config.forceRemoteFontPrefixes);
            WriteObjectList(fonts.FindPropertyRelative("mainFontAssets"), config.mainFontAssets);
            WriteObjectList(fonts.FindPropertyRelative("coverageFallbackFontAssets"), config.coverageFallbackFontAssets);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(downloader);

            _report.Add(
                $"'{candidate.path}' → '{downloader.gameObject.name}': {DescribeConfig(config)}" +
                (config.legacyMappings > 0
                    ? $" ({config.legacyMappings} mapeamento(s) antigo(s) descartado(s) — o manifesto substitui)"
                    : string.Empty)
            );

            return true;
        }

        private static void WriteStringList(SerializedProperty property, List<string> values)
        {
            if (property == null || values.Count == 0)
                return;

            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        private static void WriteObjectList(SerializedProperty property, List<TMP_FontAsset> values)
        {
            if (property == null || values.Count == 0)
                return;

            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>Limpa os blocos de script faltando que sobraram do loader deletado.</summary>
        private static bool RemoveMissingScripts(GameObject root)
        {
            var removed = 0;
            var all = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; i++)
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(all[i].gameObject);

            return removed > 0;
        }

        private static string DescribeConfig(LegacyFontConfig config)
        {
            if (!config.HasAnything)
                return "nada legível para migrar";

            var parts = new List<string>();

            if (config.baseBundleUrl.Length > 0) parts.Add($"url={config.baseBundleUrl}");
            if (config.gameId.Length > 0) parts.Add($"gameId={config.gameId}");
            if (config.mainFontAssets.Count > 0) parts.Add($"{config.mainFontAssets.Count} main font(s)");
            if (config.coverageFallbackFontAssets.Count > 0) parts.Add($"{config.coverageFallbackFontAssets.Count} coverage");
            if (config.rebuildBatchSize > 0) parts.Add($"batch={config.rebuildBatchSize}");
            if (config.forceRemoteFontPrefixes.Count > 0) parts.Add($"force=[{string.Join(",", config.forceRemoteFontPrefixes)}]");
            if (config.extraLatinPrefixes.Count > 0) parts.Add($"latin=[{string.Join(",", config.extraLatinPrefixes)}]");

            return string.Join(", ", parts);
        }
    }
}

#endif
