#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FineLocalization.Editor;
using FineLocalization.Runtime;
using FineLocalization.Utils;
using TMPro;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Janela única de setup e atualização. Substitui o vaivém entre os menus <i>Sheets</i> e
    /// <i>WebGL Remote Fonts</i>, que tinham uma ordem obrigatória entre si que nada validava:
    /// resolver abas depois de baixar apagava todos os <c>sheet.TextAsset</c> e o build WebGL
    /// morria; gerar bundle antes de assar a fonte pulava o idioma em silêncio.
    ///
    /// Cada passo detecta sozinho se já está pronto, então a janela serve tanto para o primeiro
    /// setup quanto para "adicionei uma coluna nova, o que falta fazer?".
    /// </summary>
    public class FineLocalizationHub : EditorWindow
    {
        private const string UploadDoneKeyPrefix = "FineLocalization_Hub_UploadDone_";

        private static readonly Color DoneColor = new(0.40f, 1.00f, 0.50f);
        private static readonly Color WarnColor = new(1.00f, 0.82f, 0.35f);

        private LocalizationSettings _settings;
        private RemoteFontBundleBuildConfig _buildConfig;
        private SerializedObject _buildConfigSo;

        private readonly List<string> _languages = new();
        private bool _languagesScanned;

        private bool _showFonts;
        private Vector2 _scroll;
        private string _busyLabel;

        [MenuItem("Tools/Fine Localization/Setup & Update", false, 0)]
        public static void Open()
        {
            GetWindow<FineLocalizationHub>("Fine Localization").minSize = new Vector2(620, 520);
        }

        private void OnEnable() => Rebind();

        /// <summary>
        /// A Unity destrói os SerializedObject num domain reload mas mantém as referências de
        /// objeto, então um guard <c>!= null</c> passa e o Update() estoura. Revalidado no topo do
        /// OnGUI, nunca só no OnEnable.
        /// </summary>
        private void Rebind()
        {
            _settings = FindSettingsWithoutCreating();
            _buildConfig = RemoteFontBundleBuildConfig.GetOrCreate();
            _buildConfigSo = _buildConfig != null ? new SerializedObject(_buildConfig) : null;
        }

        private void OnGUI()
        {
            if (_settings == null || _buildConfig == null || _buildConfigSo == null ||
                _buildConfigSo.targetObject == null)
                Rebind();

            _buildConfigSo?.Update();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            DrawMigrationBanner();
            DrawTextPhase();
            DrawFontPhase();

            EditorGUILayout.EndScrollView();

            if (_buildConfigSo != null && _buildConfigSo.targetObject != null &&
                _buildConfigSo.ApplyModifiedProperties())
                EditorUtility.SetDirty(_buildConfig);
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Fine Localization — Setup & Update", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_busyLabel))
                EditorGUILayout.HelpBox(_busyLabel, MessageType.Info);

            if (_settings == null)
            {
                EditorGUILayout.HelpBox(
                    "Nenhum LocalizationSettings no projeto ainda. O primeiro passo cria.",
                    MessageType.Warning
                );
                return;
            }

            var production = _settings.Mode == LocalizationSettings.LocalizationMode.Production;
            Banner(
                production ? DoneColor : WarnColor,
                production
                    ? "Mode: Production — usando a lista Sources."
                    : "Mode: Development — usando DevSources. Não builde release neste modo."
            );
        }

        /// <summary>Cena aberta com o loader da v3.1 ainda pendurado é o erro mais provável pós-update.</summary>
        private void DrawMigrationBanner()
        {
            if (!RemoteFontLoaderMigrator.HasLegacyLoaderInProject())
                return;

            EditorGUILayout.Space(2);
            Banner(WarnColor,
                "Encontrei RemoteFontBundleLoader (v3.1) em cena ou prefab. A v3.2 juntou esse " +
                "componente no RuntimeLocaleDownloader — rode o migrador para não perder a configuração.");

            if (GUILayout.Button("Abrir migrador", GUILayout.Height(24)))
                RemoteFontLoaderMigrator.ShowWindow();
        }

        // --------------------------------------------------------- Fase 1: textos

        private void DrawTextPhase()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("1 · Textos", EditorStyles.boldLabel);

            DrawSettingsStep();
            DrawSourcesStep();
            DrawResolveStep();
            DrawDownloadStep();
            DrawLanguagesStep();
        }

        private void DrawSettingsStep()
        {
            var done = _settings != null;

            BeginStep("Settings", done,
                done
                    ? "O asset de configuração existe e está apontado pelo CurrentSettingsPointer."
                    : "Cria Assets/FineLocalization/Resources/LocalizationSettings.asset.");

            if (!done)
            {
                if (GUILayout.Button("Criar settings", GUILayout.Height(24)))
                {
                    // Só tocar em Instance já cria o asset e o pointer.
                    _settings = LocalizationSettings.Instance;
                    Rebind();
                }
            }
            else if (GUILayout.Button("Selecionar no Project", GUILayout.Height(20)))
            {
                Selection.activeObject = _settings;
                EditorGUIUtility.PingObject(_settings);
            }

            EndStep();
        }

        private void DrawSourcesStep()
        {
            var sources = _settings != null ? _settings.GetActiveSources() : null;
            var hasSources = sources != null && sources.Count > 0;
            var allHaveTableId = hasSources;

            if (hasSources)
            {
                foreach (var source in sources)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.TableId))
                        allHaveTableId = false;
                }
            }

            var saveFolder = SaveFolderPath();
            var folderOk = !string.IsNullOrEmpty(saveFolder);
            var done = allHaveTableId && folderOk;

            BeginStep("Planilhas e pasta de destino", done,
                "Table Id de cada source e o Save Folder, que precisa ficar dentro de uma pasta Resources.");

            using (new EditorGUI.DisabledScope(_settings == null))
            {
                if (!allHaveTableId)
                    EditorGUILayout.HelpBox("Falta Table Id em pelo menos uma source ativa.", MessageType.Warning);

                if (!folderOk)
                {
                    EditorGUILayout.HelpBox(
                        _settings != null && _settings.SaveFolder == null
                            ? "Save Folder não está definido."
                            : "Save Folder precisa ser uma pasta do projeto dentro de Resources, fora de Packages/.",
                        MessageType.Warning
                    );
                }
                else
                {
                    EditorGUILayout.LabelField("Save Folder", saveFolder, EditorStyles.miniLabel);
                }

                if (GUILayout.Button("Abrir settings para editar", GUILayout.Height(20)) && _settings != null)
                    EditorUtility.OpenPropertyEditor(_settings);
            }

            EndStep();
        }

        private void DrawResolveStep()
        {
            var sources = _settings != null ? _settings.GetActiveSources() : null;
            var done = sources != null && sources.Count > 0;

            if (done)
            {
                foreach (var source in sources)
                {
                    if (source?.Sheets == null || source.Sheets.Count == 0)
                    {
                        done = false;
                        break;
                    }

                    foreach (var sheet in source.Sheets)
                    {
                        if (sheet == null || sheet.Id <= 0 || string.IsNullOrWhiteSpace(sheet.Name))
                        {
                            done = false;
                            break;
                        }
                    }
                }
            }

            BeginStep("Resolver abas", done,
                "Descobre as abas da planilha e seus gids. Só é preciso quando você adiciona ou renomeia uma aba.");

            EditorGUILayout.HelpBox(
                "Resolver limpa a lista de abas e, com ela, as referências de CSV já baixado. " +
                "Sempre baixe de novo depois — senão o build WebGL falha por falta de CSV.",
                MessageType.Warning
            );

            using (new EditorGUI.DisabledScope(_settings == null || Busy))
            {
                if (GUILayout.Button("↺ Resolver abas", GUILayout.Height(24)))
                    _settings.ResolveGoogleSheets();
            }

            EndStep();
        }

        private void DrawDownloadStep()
        {
            var sheets = AllSheets();
            var hasSheets = sheets.Count > 0;
            var allDownloaded = hasSheets;

            foreach (var sheet in sheets)
            {
                if (sheet.TextAsset == null)
                    allDownloaded = false;
            }

            var charactersOk = File.Exists(CharactersAllPath);
            var done = allDownloaded && charactersOk;

            BeginStep("Baixar planilhas", done,
                "Baixa os CSVs, aponta os TextAssets que o build exige e regenera os TXT de caracteres.");

            if (hasSheets && !allDownloaded)
                EditorGUILayout.HelpBox("Alguma aba está sem CSV baixado — o build WebGL falharia.", MessageType.Warning);
            else if (allDownloaded && !charactersOk)
                EditorGUILayout.HelpBox("CSVs prontos, mas os TXT de caracteres não foram gerados.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(_settings == null || !hasSheets || Busy))
            {
                if (GUILayout.Button("▼ Baixar e gerar caracteres", GUILayout.Height(26)))
                    EditorCoroutineUtility.StartCoroutineOwnerless(DownloadThenCharacters());
            }

            EditorGUILayout.LabelField(
                "Uma ação só. Antes eram dois menus que gravavam em pastas diferentes e nenhum " +
                "dos dois completava o pipeline.",
                EditorStyles.wordWrappedMiniLabel
            );

            EndStep();
        }

        /// <summary>
        /// Baixa e gera caracteres, na ordem certa e a partir da mesma pasta. Na v3.1 o download
        /// oficial gravava em <c>SaveFolder</c> e setava os TextAssets mas não gerava caracteres,
        /// enquanto o sync de caracteres gravava num caminho fixo e não setava TextAsset.
        /// </summary>
        private IEnumerator DownloadThenCharacters()
        {
            _busyLabel = "Baixando planilhas...";
            Repaint();

            yield return _settings.DownloadGoogleSheetsCoroutine(null, silent: true);

            var folder = SaveFolderPath();
            if (!string.IsNullOrEmpty(folder))
            {
                _busyLabel = "Gerando TXT de caracteres...";
                Repaint();
                LocalizationEditorCsvSync.GenerateCharactersFromFolder(folder);
            }

            _busyLabel = null;
            _languagesScanned = false;
            Repaint();
        }

        private void DrawLanguagesStep()
        {
            EnsureLanguagesScanned();

            BeginStep("Idiomas detectados", _languages.Count > 0,
                "Colunas de idioma encontradas nos CSVs. É daqui que as entradas de bundle são propostas.");

            if (_languages.Count == 0)
            {
                EditorGUILayout.HelpBox("Nenhuma coluna de idioma encontrada. Baixe as planilhas primeiro.", MessageType.Info);
            }
            else
            {
                foreach (var language in _languages)
                {
                    var latin = LanguageCode.IsLatinScript(language);
                    EditorGUILayout.LabelField(
                        $"  {language}",
                        latin ? "fonte local cobre" : "precisa de bundle de fonte",
                        EditorStyles.miniLabel
                    );
                }
            }

            if (GUILayout.Button("Reescanear", GUILayout.Height(20)))
                _languagesScanned = false;

            EndStep();
        }

        // ---------------------------------------------------------- Fase 2: fontes

        private void DrawFontPhase()
        {
            EditorGUILayout.Space(10);
            _showFonts = EditorGUILayout.Foldout(_showFonts, "2 · Fontes (opcional)", true);

            if (!_showFonts)
            {
                EditorGUILayout.LabelField(
                    "    Só para idiomas com muitos glifos — CJK, tailandês, árabe, hindi. " +
                    "Se seu jogo é só latino, ignore esta fase.",
                    EditorStyles.wordWrappedMiniLabel
                );
                return;
            }

            EnsureLanguagesScanned();

            DrawEntriesStep();
            DrawSourceFontsStep();
            DrawGenerateStep();
            DrawBuildStep();
            DrawUploadStep();
            DrawVerifyStep();
        }

        private List<string> LanguagesNeedingBundle()
        {
            var result = new List<string>();
            foreach (var language in _languages)
            {
                if (!LanguageCode.IsLatinScript(language))
                    result.Add(language);
            }

            return result;
        }

        private void DrawEntriesStep()
        {
            var needing = LanguagesNeedingBundle();
            var missing = new List<string>();

            foreach (var language in needing)
            {
                if (_buildConfig == null || _buildConfig.FindEntryForLanguage(language) == null)
                    missing.Add(language);
            }

            BeginStep("Entradas de bundle", missing.Count == 0 && needing.Count > 0,
                "Uma entrada por idioma que precisa de fonte remota, com a pasta criada. Nome do bundle e pasta saem da coluna da planilha.");

            if (needing.Count == 0)
            {
                EditorGUILayout.HelpBox("Nenhum idioma detectado precisa de fonte remota.", MessageType.Info);
                EndStep();
                return;
            }

            if (missing.Count > 0)
            {
                EditorGUILayout.HelpBox($"Sem entrada: {string.Join(", ", missing)}", MessageType.Warning);

                if (GUILayout.Button($"Criar {missing.Count} entrada(s) faltante(s)", GUILayout.Height(24)))
                {
                    var added = _buildConfig.AddMissingEntries(missing);
                    Debug.Log($"[FineLocalization] Entradas criadas: {string.Join(", ", added)}");
                    Rebind();
                }
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Também posso criar bundle para um idioma latino (ex: vi, tr):", EditorStyles.miniLabel);
            foreach (var language in _languages)
            {
                if (!LanguageCode.IsLatinScript(language) || _buildConfig.FindEntryForLanguage(language) != null)
                    continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  {language}", EditorStyles.miniLabel, GUILayout.Width(120));
                if (GUILayout.Button("forçar bundle", EditorStyles.miniButton, GUILayout.Width(100)))
                {
                    _buildConfig.AddMissingEntries(new[] { language });
                    Debug.LogWarning(
                        $"[FineLocalization] Entrada criada para '{language}', que é latino. " +
                        "Adicione o código em Force Remote Font Prefixes no RuntimeLocaleDownloader, " +
                        "senão o download nunca acontece."
                    );
                    Rebind();
                }
                EditorGUILayout.EndHorizontal();
            }

            EndStep();
        }

        private void DrawSourceFontsStep()
        {
            var entries = _buildConfigSo?.FindProperty("entries");
            var missing = 0;

            if (entries != null)
            {
                for (int i = 0; i < entries.arraySize; i++)
                {
                    if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("sourceFont").objectReferenceValue == null)
                        missing++;
                }
            }

            BeginStep("Fontes de origem", entries != null && entries.arraySize > 0 && missing == 0,
                "O único campo que ninguém deriva: qual .ttf/.otf usar em cada idioma. É escolha tipográfica e de licença.");

            if (entries == null || entries.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Nenhuma entrada de bundle ainda.", MessageType.Info);
                EndStep();
                return;
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var bundleName = entry.FindPropertyRelative("bundleName").stringValue;
                var language = RemoteFontBundleBuildConfig.LanguageOf(bundleName);

                EditorGUILayout.PropertyField(
                    entry.FindPropertyRelative("sourceFont"),
                    new GUIContent(string.IsNullOrEmpty(language) ? bundleName : language)
                );
            }

            if (missing > 0)
                EditorGUILayout.HelpBox($"{missing} idioma(s) sem fonte de origem — o bake vai pular.", MessageType.Warning);

            EndStep();
        }

        private void DrawGenerateStep()
        {
            var entries = _buildConfig?.entries;
            var baked = 0;
            var total = 0;

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.bundleName))
                        continue;

                    total++;
                    if (HasBakedFont(entry))
                        baked++;
                }
            }

            BeginStep("Assar font assets", total > 0 && baked == total,
                "Gera um TMP_FontAsset estático com apenas os caracteres que aquela coluna usa.");

            if (total == 0)
            {
                EditorGUILayout.HelpBox("Nenhuma entrada para assar.", MessageType.Info);
                EndStep();
                return;
            }

            EditorGUILayout.LabelField($"Assados: {baked} de {total}", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(Busy))
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.75f, 0.9f, 0.6f);
                if (GUILayout.Button("⚙ Gerar font assets", GUILayout.Height(26)))
                {
                    AssetDatabase.SaveAssetIfDirty(_buildConfig);
                    GenerateRemoteFontAssets.GenerateAll(_buildConfig);
                    Rebind();
                }
                GUI.backgroundColor = previous;
            }

            EndStep();
        }

        private void DrawBuildStep()
        {
            var manifest = FontBundleManifest.FindExisting();
            var output = OutputFolderPath();
            var built = 0;
            var total = 0;

            if (_buildConfig?.entries != null)
            {
                foreach (var entry in _buildConfig.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.bundleName))
                        continue;

                    total++;
                    if (File.Exists(Path.Combine(output, entry.bundleName.Trim() + ".ft")))
                        built++;
                }
            }

            var manifestOk = manifest != null && manifest.HasAnyBundle;
            BeginStep("Build dos bundles", total > 0 && built == total && manifestOk,
                "Empacota os bundles e grava o manifesto que o runtime lê para saber quais idiomas têm fonte.");

            EditorGUILayout.LabelField($"Construídos: {built} de {total}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "Manifesto",
                manifestOk ? $"{manifest.entries.Count} idioma(s), gerado em {manifest.generatedAt}" : "não gerado",
                EditorStyles.miniLabel
            );

            if (!manifestOk)
            {
                EditorGUILayout.HelpBox(
                    "Sem manifesto o jogo não sabe que existem fontes remotas — todo idioma não-latino " +
                    "cai no fallback. Ele precisa ser commitado junto com o projeto.",
                    MessageType.Warning
                );
            }

            using (new EditorGUI.DisabledScope(Busy || total == 0))
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
                if (GUILayout.Button("▶ Build bundles", GUILayout.Height(26)))
                {
                    AssetDatabase.SaveAssetIfDirty(_buildConfig);
                    BuildRemoteFontBundles.BuildWebGlFontBundles();
                    Rebind();
                }
                GUI.backgroundColor = previous;
            }

            EndStep();
        }

        private void DrawUploadStep()
        {
            var manifest = FontBundleManifest.FindExisting();
            var key = UploadDoneKeyPrefix + (manifest != null ? manifest.generatedAt : "none");
            var acknowledged = EditorPrefs.GetBool(key, false);

            BeginStep("Enviar para o CDN", acknowledged,
                "O único passo que nenhuma checagem alcança: os arquivos precisam existir no seu CDN.");

            if (manifest == null || !manifest.HasAnyBundle)
            {
                EditorGUILayout.HelpBox("Construa os bundles primeiro.", MessageType.Info);
                EndStep();
                return;
            }

            EditorGUILayout.LabelField("Arquivos a enviar:", EditorStyles.miniBoldLabel);
            foreach (var entry in manifest.entries)
            {
                if (entry != null)
                    EditorGUILayout.LabelField($"  {entry.language}", entry.bundleFileName, EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(
                "O caminho completo depende do Base Bundle URL e do Game Id do RuntimeLocaleDownloader — " +
                "o inspector dele mostra a URL final de cada idioma.",
                EditorStyles.wordWrappedMiniLabel
            );

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Revelar pasta dos bundles", GUILayout.Height(20)))
            {
                var full = Path.GetFullPath(OutputFolderPath());
                if (Directory.Exists(full))
                    EditorUtility.RevealInFinder(full);
            }

            var toggled = EditorGUILayout.ToggleLeft("Já enviei esta versão dos bundles", acknowledged);
            if (toggled != acknowledged)
                EditorPrefs.SetBool(key, toggled);

            EndStep();
        }

        private void DrawVerifyStep()
        {
            BeginStep("Verificar", false,
                "Roda as checagens que pegam as contradições silenciosas.");

            if (GUILayout.Button("Verificar tudo", GUILayout.Height(24)))
            {
                var problems = CollectProblems();

                if (problems.Count == 0)
                    Debug.Log("[FineLocalization] Verificação: nada a apontar.");
                else
                    Debug.LogWarning("[FineLocalization] Verificação:\n  • " + string.Join("\n  • ", problems));
            }

            EndStep();
        }

        /// <summary>
        /// Contradições que não aparecem como erro em lugar nenhum e só se manifestam em Play —
        /// ou pior, em produção.
        /// </summary>
        private List<string> CollectProblems()
        {
            var problems = new List<string>();

            foreach (var problem in LanguageCode.ValidateTables())
                problems.Add("Tabela de idiomas: " + problem);

            var configs = RemoteFontBundleBuildConfig.FindAllConfigPaths();
            if (configs.Length > 1)
            {
                problems.Add(
                    $"Há {configs.Length} RemoteFontBundleBuildConfig no projeto e qual vale não é estável: " +
                    string.Join(", ", configs)
                );
            }

            var manifest = FontBundleManifest.FindExisting();
            var output = OutputFolderPath();

            if (_buildConfig?.entries != null)
            {
                foreach (var entry in _buildConfig.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.bundleName))
                        continue;

                    var language = RemoteFontBundleBuildConfig.LanguageOf(entry);
                    var bundleName = entry.bundleName.Trim();

                    if (LanguageCode.IsLatinScript(language))
                    {
                        problems.Add(
                            $"'{bundleName}' é para '{language}', que conta como latino — o download só " +
                            "acontece se o código estiver em Force Remote Font Prefixes."
                        );
                    }

                    var expected = BuildRemoteFontBundles.LoadExpectedCharactersForBundle(bundleName, out _);
                    if (string.IsNullOrEmpty(expected))
                    {
                        problems.Add($"'{bundleName}' não tem characters_{language}.txt — baixe as planilhas.");
                    }
                    else if (HasBakedFont(entry, out var font))
                    {
                        var missing = CountMissingGlyphs(font, expected);
                        if (missing > 0)
                            problems.Add($"'{bundleName}': a fonte assada não tem {missing} glifo(s) da coluna.");
                    }

                    var bundlePath = Path.Combine(output, bundleName + ".ft");
                    if (!File.Exists(bundlePath))
                    {
                        problems.Add($"'{bundleName}' não foi construído ainda.");
                    }
                    else if (manifest?.Find(language) == null)
                    {
                        problems.Add($"'{bundleName}' existe em disco mas não está no manifesto — rode Build Bundles.");
                    }
                }
            }

            if (manifest?.entries != null)
            {
                foreach (var entry in manifest.entries)
                {
                    if (entry == null)
                        continue;

                    if (!File.Exists(Path.Combine(output, entry.bundleFileName)))
                        problems.Add($"O manifesto declara '{entry.bundleFileName}', que não existe em {output}.");
                }
            }

            return problems;
        }

        // ---------------------------------------------------------------- Helpers

        private bool Busy => !string.IsNullOrEmpty(_busyLabel);

        private static string CharactersAllPath =>
            "Assets/FineLocalization/Editor/GeneratedCharacters/characters_all.txt";

        private string OutputFolderPath()
        {
            return _buildConfig != null && !string.IsNullOrWhiteSpace(_buildConfig.outputFolder)
                ? _buildConfig.outputFolder
                : "AssetBundles/WebGL/Fonts";
        }

        /// <summary>
        /// Settings do projeto <b>sem criar</b>. Ler <c>LocalizationSettings.Instance</c> cria o
        /// asset como efeito colateral, o que faria o passo 1 nunca aparecer como pendente.
        /// </summary>
        private static LocalizationSettings FindSettingsWithoutCreating()
        {
            var pointer = Resources.Load<CurrentSettingsPointer>("CurrentSettingsPointer");
            if (pointer?.settings != null)
                return pointer.settings;

            return Resources.Load<LocalizationSettings>("LocalizationSettings");
        }

        private string SaveFolderPath()
        {
            if (_settings == null || _settings.SaveFolder == null)
                return null;

            var path = AssetDatabase.GetAssetPath(_settings.SaveFolder);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                return null;

            if (path.StartsWith("Packages/", StringComparison.Ordinal) ||
                path.IndexOf("/Resources", StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            return path;
        }

        private List<Sheet> AllSheets()
        {
            var result = new List<Sheet>();
            var sources = _settings != null ? _settings.GetActiveSources() : null;
            if (sources == null)
                return result;

            foreach (var source in sources)
            {
                if (source?.Sheets == null)
                    continue;

                foreach (var sheet in source.Sheets)
                {
                    if (sheet != null)
                        result.Add(sheet);
                }
            }

            return result;
        }

        private void EnsureLanguagesScanned()
        {
            if (_languagesScanned)
                return;

            _languagesScanned = true;
            _languages.Clear();

            var folder = SaveFolderPath();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;

            var skip = _settings != null ? _settings.skip : 0;

            foreach (var path in Directory.GetFiles(folder, "*.csv", SearchOption.TopDirectoryOnly))
            {
                string content;
                try
                {
                    content = File.ReadAllText(path, Encoding.UTF8);
                }
                catch
                {
                    continue;
                }

                var lines = LocalizationManager.GetLines(content);
                if (lines.Count == 0)
                    continue;

                var header = LocalizationManager.GetColumns(lines[0]);
                for (int i = skip + 1; i < header.Count; i++)
                {
                    var language = LanguageCode.Normalize(header[i]);
                    if (language.Length > 0 && !_languages.Contains(language))
                        _languages.Add(language);
                }
            }
        }

        private static bool HasBakedFont(RemoteFontBundleBuildConfig.Entry entry) =>
            HasBakedFont(entry, out _);

        private static bool HasBakedFont(RemoteFontBundleBuildConfig.Entry entry, out TMP_FontAsset font)
        {
            font = null;
            if (entry?.folder == null)
                return false;

            var folderPath = AssetDatabase.GetAssetPath(entry.folder);
            if (!AssetDatabase.IsValidFolder(folderPath))
                return false;

            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { folderPath }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidate = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                if (candidate != null)
                {
                    font = candidate;
                    return true;
                }
            }

            return false;
        }

        private static int CountMissingGlyphs(TMP_FontAsset font, string expected)
        {
            if (font == null || string.IsNullOrEmpty(expected))
                return 0;

            var seen = new HashSet<char>();
            var missing = 0;

            foreach (var c in expected)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c) || !seen.Add(c))
                    continue;

                if (!font.HasCharacter(c))
                    missing++;
            }

            return missing;
        }

        // --------------------------------------------------------------- Desenho

        private void BeginStep(string title, bool done, string explanation)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            var previous = GUI.contentColor;
            GUI.contentColor = done ? DoneColor : WarnColor;
            EditorGUILayout.LabelField(done ? "✔" : "○", GUILayout.Width(18));
            GUI.contentColor = previous;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(explanation, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2);
        }

        private void EndStep()
        {
            EditorGUILayout.EndVertical();
        }

        private static void Banner(Color color, string message)
        {
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = color;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
            GUI.backgroundColor = previous;
        }
    }
}

#endif
