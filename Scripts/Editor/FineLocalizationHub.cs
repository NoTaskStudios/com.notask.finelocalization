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
    /// <b>Todo estado vem de um snapshot, nunca do OnGUI.</b> Responder "esse passo já está
    /// pronto?" custa varrer o AssetDatabase, ler todo prefab e toda cena do projeto e bater em
    /// disco. Fazer isso por frame travava a janela.  O snapshot é recalculado ao abrir, ao ganhar
    /// foco, depois de cada ação e no botão Reatualizar.
    /// </summary>
    public class FineLocalizationHub : EditorWindow
    {
        private const string UploadDoneKeyPrefix = "FineLocalization_Hub_UploadDone_";
        private const string DefaultOutputFolder = "AssetBundles/WebGL/Fonts";

        private static readonly Color DoneColor = new(0.40f, 1.00f, 0.50f);
        private static readonly Color WarnColor = new(1.00f, 0.82f, 0.35f);

        /// <summary>
        /// Tudo que os passos precisam saber, calculado de uma vez só. Nada aqui pode ser
        /// consultado direto do desenho.
        /// </summary>
        private class Snapshot
        {
            public bool settingsOk;
            public bool tableIdsOk;
            public bool saveFolderOk;
            public string saveFolder;
            public bool sheetsResolved;
            public bool sheetsDownloaded;
            public bool charactersGenerated;
            public bool hasSheets;

            public readonly List<string> languages = new();
            public readonly List<string> needBundle = new();
            public readonly List<string> latinWithoutEntry = new();
            public readonly List<string> missingEntries = new();

            public int entriesTotal;
            public int sourceFontsMissing;
            public int bakedCount;
            public int builtCount;

            public bool manifestOk;
            public int manifestCount;
            public string manifestGeneratedAt;
            public readonly List<string> manifestFiles = new();

            public bool legacyLoaderPresent;
            public string outputFolder = DefaultOutputFolder;
            public string[] configPaths = new string[0];
        }

        private LocalizationSettings _settings;
        private RemoteFontBundleBuildConfig _buildConfig;
        private SerializedObject _buildConfigSo;
        private Snapshot _state;

        private bool _showFonts;
        private bool _showBakeSettings;
        private Vector2 _scroll;
        private string _busyLabel;

        [MenuItem("Tools/Fine Localization/Setup and Update", false, 0)]
        public static void Open()
        {
            GetWindow<FineLocalizationHub>("Fine Localization").minSize = new Vector2(620, 520);
        }

        private void OnEnable() => Invalidate();

        /// <summary>Ganhar foco costuma significar "mexi em asset na Unity": hora de reler.</summary>
        private void OnFocus() => Invalidate();

        private void Invalidate()
        {
            _state = null;
            _buildConfigSo = null;
        }

        private void EnsureState()
        {
            // A Unity destrói o SerializedObject num domain reload mas mantém a referência do
            // objeto, então checar só `!= null` passa e o Update() estoura.
            if (_state != null && _buildConfigSo != null && _buildConfigSo.targetObject != null)
                return;

            _settings = FindSettingsWithoutCreating();
            _buildConfig = RemoteFontBundleBuildConfig.GetOrCreate();
            _buildConfigSo = _buildConfig != null ? new SerializedObject(_buildConfig) : null;
            _state = BuildSnapshot();
        }

        private void OnGUI()
        {
            EnsureState();
            _buildConfigSo?.Update();

            DrawToolbar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            DrawMigrationBanner();
            DrawTextPhase();
            DrawFontPhase();

            EditorGUILayout.EndScrollView();

            if (_buildConfigSo != null && _buildConfigSo.targetObject != null &&
                _buildConfigSo.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(_buildConfig);
                Invalidate();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Reatualizar", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                Invalidate();
                GUIUtility.ExitGUI();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Bundle Builder (avançado)", EditorStyles.toolbarButton, GUILayout.Width(180)))
                RemoteFontBundleBuildWindow.Open();

            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ Snapshot

        private Snapshot BuildSnapshot()
        {
            var state = new Snapshot();

            state.settingsOk = _settings != null;
            state.saveFolder = ResolveSaveFolder();
            state.saveFolderOk = !string.IsNullOrEmpty(state.saveFolder);

            var sources = _settings != null ? _settings.GetActiveSources() : null;
            state.tableIdsOk = sources != null && sources.Count > 0;
            state.sheetsResolved = state.tableIdsOk;

            if (sources != null)
            {
                foreach (var source in sources)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.TableId))
                        state.tableIdsOk = false;

                    if (source?.Sheets == null || source.Sheets.Count == 0)
                    {
                        state.sheetsResolved = false;
                        continue;
                    }

                    foreach (var sheet in source.Sheets)
                    {
                        if (sheet == null || sheet.Id <= 0 || string.IsNullOrWhiteSpace(sheet.Name))
                        {
                            state.sheetsResolved = false;
                            continue;
                        }

                        state.hasSheets = true;
                    }
                }
            }

            if (state.hasSheets)
            {
                state.sheetsDownloaded = true;
                foreach (var sheet in AllSheets())
                {
                    if (sheet.TextAsset == null)
                        state.sheetsDownloaded = false;
                }
            }

            state.charactersGenerated = File.Exists(CharactersAllPath);

            ScanLanguages(state);

            if (_buildConfig != null)
            {
                state.outputFolder = string.IsNullOrWhiteSpace(_buildConfig.outputFolder)
                    ? DefaultOutputFolder
                    : _buildConfig.outputFolder;

                foreach (var language in state.needBundle)
                {
                    if (_buildConfig.FindEntryForLanguage(language) == null)
                        state.missingEntries.Add(language);
                }

                foreach (var language in state.languages)
                {
                    if (LanguageCode.IsLatinScript(language) &&
                        _buildConfig.FindEntryForLanguage(language) == null)
                        state.latinWithoutEntry.Add(language);
                }

                if (_buildConfig.entries != null)
                {
                    foreach (var entry in _buildConfig.entries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.bundleName))
                            continue;

                        state.entriesTotal++;

                        if (entry.sourceFont == null)
                            state.sourceFontsMissing++;

                        if (HasBakedFont(entry, out _))
                            state.bakedCount++;

                        if (File.Exists(Path.Combine(state.outputFolder, entry.bundleName.Trim() + ".ft")))
                            state.builtCount++;
                    }
                }

                state.configPaths = RemoteFontBundleBuildConfig.FindAllConfigPaths();
            }

            var manifest = FontBundleManifest.FindExisting();
            state.manifestOk = manifest != null && manifest.HasAnyBundle;
            if (state.manifestOk)
            {
                state.manifestCount = manifest.entries.Count;
                state.manifestGeneratedAt = manifest.generatedAt;
                foreach (var entry in manifest.entries)
                {
                    if (entry != null)
                        state.manifestFiles.Add($"{entry.language}  →  {entry.bundleFileName}");
                }
            }

            // Varredura caríssima: todo prefab e toda cena do projeto lidos como texto. Uma vez
            // por snapshot — era isso que travava a janela quando rodava por frame.
            state.legacyLoaderPresent = RemoteFontLoaderMigrator.HasLegacyLoaderInProject();

            return state;
        }

        private void ScanLanguages(Snapshot state)
        {
            var folder = state.saveFolder;
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
                    if (language.Length == 0 || state.languages.Contains(language))
                        continue;

                    state.languages.Add(language);
                    if (!LanguageCode.IsLatinScript(language))
                        state.needBundle.Add(language);
                }
            }
        }

        // --------------------------------------------------------------- Cabeçalho

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Fine Localization — setup e atualização", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_busyLabel))
                EditorGUILayout.HelpBox(_busyLabel, MessageType.Info);

            if (!_state.settingsOk)
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

        private void DrawMigrationBanner()
        {
            if (!_state.legacyLoaderPresent)
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
            BeginStep("Settings", _state.settingsOk,
                _state.settingsOk
                    ? "O asset de configuração existe e está apontado pelo CurrentSettingsPointer."
                    : "Cria Assets/FineLocalization/Resources/LocalizationSettings.asset.");

            if (!_state.settingsOk)
            {
                if (GUILayout.Button("Criar settings", GUILayout.Height(24)))
                {
                    // Só tocar em Instance já cria o asset e o pointer.
                    _settings = LocalizationSettings.Instance;
                    Invalidate();
                    GUIUtility.ExitGUI();
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
            BeginStep("Planilhas e pasta de destino", _state.tableIdsOk && _state.saveFolderOk,
                "Table Id de cada source e o Save Folder, que precisa ficar dentro de uma pasta Resources.");

            using (new EditorGUI.DisabledScope(!_state.settingsOk))
            {
                if (!_state.tableIdsOk)
                    EditorGUILayout.HelpBox("Falta Table Id em pelo menos uma source ativa.", MessageType.Warning);

                if (!_state.saveFolderOk)
                {
                    EditorGUILayout.HelpBox(
                        "Save Folder precisa ser uma pasta do projeto dentro de Resources, fora de Packages/.",
                        MessageType.Warning
                    );
                }
                else
                {
                    EditorGUILayout.LabelField("Save Folder", _state.saveFolder, EditorStyles.miniLabel);
                }

                if (GUILayout.Button("Abrir settings para editar", GUILayout.Height(20)) && _settings != null)
                    EditorUtility.OpenPropertyEditor(_settings);
            }

            EndStep();
        }

        private void DrawResolveStep()
        {
            BeginStep("Resolver abas", _state.sheetsResolved,
                "Descobre as abas da planilha e seus gids. Só é preciso quando você adiciona ou renomeia uma aba.");

            EditorGUILayout.HelpBox(
                "Resolver limpa a lista de abas e, com ela, as referências de CSV já baixado. " +
                "Sempre baixe de novo depois — senão o build WebGL falha por falta de CSV.",
                MessageType.Warning
            );

            using (new EditorGUI.DisabledScope(!_state.settingsOk || Busy))
            {
                if (GUILayout.Button("↺ Resolver abas", GUILayout.Height(24)))
                {
                    _settings.ResolveGoogleSheets();
                    Invalidate();
                    GUIUtility.ExitGUI();
                }
            }

            EndStep();
        }

        private void DrawDownloadStep()
        {
            BeginStep("Baixar planilhas", _state.sheetsDownloaded && _state.charactersGenerated,
                "Baixa os CSVs, aponta os TextAssets que o build exige e regenera os TXT de caracteres.");

            if (_state.hasSheets && !_state.sheetsDownloaded)
                EditorGUILayout.HelpBox("Alguma aba está sem CSV baixado — o build WebGL falharia.", MessageType.Warning);
            else if (_state.sheetsDownloaded && !_state.charactersGenerated)
                EditorGUILayout.HelpBox("CSVs prontos, mas os TXT de caracteres não foram gerados.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!_state.settingsOk || !_state.hasSheets || Busy))
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

        private IEnumerator DownloadThenCharacters()
        {
            _busyLabel = "Baixando planilhas...";
            Repaint();

            yield return _settings.DownloadGoogleSheetsCoroutine(null, silent: true);

            var folder = ResolveSaveFolder();
            if (!string.IsNullOrEmpty(folder))
            {
                _busyLabel = "Gerando TXT de caracteres...";
                Repaint();
                LocalizationEditorCsvSync.GenerateCharactersFromFolder(folder);
            }

            _busyLabel = null;
            Invalidate();
            Repaint();
        }

        private void DrawLanguagesStep()
        {
            BeginStep("Idiomas detectados", _state.languages.Count > 0,
                "Colunas de idioma encontradas nos CSVs. É daqui que as entradas de bundle são propostas.");

            if (_state.languages.Count == 0)
            {
                EditorGUILayout.HelpBox("Nenhuma coluna de idioma encontrada. Baixe as planilhas primeiro.", MessageType.Info);
            }
            else
            {
                foreach (var language in _state.languages)
                {
                    EditorGUILayout.LabelField(
                        $"  {language}",
                        _state.needBundle.Contains(language) ? "precisa de bundle de fonte" : "fonte local cobre",
                        EditorStyles.miniLabel
                    );
                }
            }

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

            DrawEntriesStep();
            DrawSourceFontsStep();
            DrawGenerateStep();
            DrawBuildStep();
            DrawUploadStep();
            DrawVerifyStep();
        }

        private void DrawEntriesStep()
        {
            BeginStep("Entradas de bundle", _state.missingEntries.Count == 0 && _state.needBundle.Count > 0,
                "Uma entrada por idioma que precisa de fonte remota, com a pasta criada. Nome do bundle e pasta saem da coluna da planilha.");

            if (_state.needBundle.Count == 0)
            {
                EditorGUILayout.HelpBox("Nenhum idioma detectado precisa de fonte remota.", MessageType.Info);
                EndStep();
                return;
            }

            if (_state.missingEntries.Count > 0)
            {
                EditorGUILayout.HelpBox($"Sem entrada: {string.Join(", ", _state.missingEntries)}", MessageType.Warning);

                if (GUILayout.Button($"Criar {_state.missingEntries.Count} entrada(s) faltante(s)", GUILayout.Height(24)))
                {
                    var added = _buildConfig.AddMissingEntries(_state.missingEntries);
                    Debug.Log($"[FineLocalization] Entradas criadas: {string.Join(", ", added)}");
                    Invalidate();
                    GUIUtility.ExitGUI();
                }
            }

            DrawEntryList(showFolder: true, showSourceFont: false);

            if (_state.latinWithoutEntry.Count > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Latino sem bundle (normal — a fonte local cobre):", EditorStyles.miniLabel);

                foreach (var language in _state.latinWithoutEntry)
                {
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
                        Invalidate();
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EndStep();
        }

        private void DrawSourceFontsStep()
        {
            BeginStep("Fontes de origem", _state.entriesTotal > 0 && _state.sourceFontsMissing == 0,
                "O único campo que ninguém deriva: qual .ttf/.otf usar em cada idioma. É escolha tipográfica e de licença.");

            if (_state.entriesTotal == 0)
            {
                EditorGUILayout.HelpBox("Nenhuma entrada de bundle ainda.", MessageType.Info);
                EndStep();
                return;
            }

            DrawEntryList(showFolder: false, showSourceFont: true);

            if (_state.sourceFontsMissing > 0)
                EditorGUILayout.HelpBox($"{_state.sourceFontsMissing} idioma(s) sem fonte de origem — o bake vai pular.", MessageType.Warning);

            EndStep();
        }

        /// <summary>Entradas do build config editáveis aqui mesmo, sem precisar de outra janela.</summary>
        private void DrawEntryList(bool showFolder, bool showSourceFont)
        {
            var entries = _buildConfigSo?.FindProperty("entries");
            if (entries == null || entries.arraySize == 0)
                return;

            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var bundleName = entry.FindPropertyRelative("bundleName");
                var label = RemoteFontBundleBuildConfig.LanguageOf(bundleName.stringValue);
                if (string.IsNullOrEmpty(label))
                    label = bundleName.stringValue;

                if (showFolder)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(90));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("folder"), GUIContent.none);
                    EditorGUILayout.EndHorizontal();
                }

                if (showSourceFont)
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("sourceFont"), new GUIContent(label));
            }
        }

        private void DrawGenerateStep()
        {
            BeginStep("Assar font assets", _state.entriesTotal > 0 && _state.bakedCount == _state.entriesTotal,
                "Gera um TMP_FontAsset estático com apenas os caracteres que aquela coluna usa.");

            if (_state.entriesTotal == 0)
            {
                EditorGUILayout.HelpBox("Nenhuma entrada para assar.", MessageType.Info);
                EndStep();
                return;
            }

            EditorGUILayout.LabelField($"Assados: {_state.bakedCount} de {_state.entriesTotal}", EditorStyles.miniLabel);

            _showBakeSettings = EditorGUILayout.Foldout(_showBakeSettings, "Configurações do atlas", true);
            if (_showBakeSettings && _buildConfigSo != null)
            {
                EditorGUI.indentLevel++;
                var autoSize = _buildConfigSo.FindProperty("autoSizeToAtlas");
                EditorGUILayout.PropertyField(autoSize, new GUIContent("Auto Size (cabe em 1 atlas)"));
                EditorGUILayout.PropertyField(_buildConfigSo.FindProperty("atlasSize"), new GUIContent("Atlas Size"));
                using (new EditorGUI.DisabledScope(autoSize.boolValue))
                    EditorGUILayout.PropertyField(_buildConfigSo.FindProperty("samplingPointSize"), new GUIContent("Sampling Point Size"));
                EditorGUILayout.PropertyField(_buildConfigSo.FindProperty("paddingPercent"), new GUIContent("Padding (%)"));
                EditorGUI.indentLevel--;
            }

            using (new EditorGUI.DisabledScope(Busy))
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.75f, 0.9f, 0.6f);
                if (GUILayout.Button("⚙ Gerar font assets", GUILayout.Height(26)))
                {
                    AssetDatabase.SaveAssetIfDirty(_buildConfig);
                    GenerateRemoteFontAssets.GenerateAll(_buildConfig);
                    Invalidate();
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = previous;
            }

            EndStep();
        }

        private void DrawBuildStep()
        {
            BeginStep("Build dos bundles",
                _state.entriesTotal > 0 && _state.builtCount == _state.entriesTotal && _state.manifestOk,
                "Empacota os bundles e grava o manifesto que o runtime lê para saber quais idiomas têm fonte.");

            if (_buildConfigSo != null)
                EditorGUILayout.PropertyField(_buildConfigSo.FindProperty("outputFolder"), new GUIContent("Output Folder"));

            EditorGUILayout.LabelField($"Construídos: {_state.builtCount} de {_state.entriesTotal}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "Manifesto",
                _state.manifestOk
                    ? $"{_state.manifestCount} idioma(s), gerado em {_state.manifestGeneratedAt}"
                    : "não gerado",
                EditorStyles.miniLabel
            );

            if (!_state.manifestOk)
            {
                EditorGUILayout.HelpBox(
                    "Sem manifesto o jogo não sabe que existem fontes remotas — todo idioma não-latino " +
                    "cai no fallback. Ele precisa ser commitado junto com o projeto.",
                    MessageType.Warning
                );
            }

            using (new EditorGUI.DisabledScope(Busy || _state.entriesTotal == 0))
            {
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
                if (GUILayout.Button("▶ Build bundles", GUILayout.Height(26)))
                {
                    AssetDatabase.SaveAssetIfDirty(_buildConfig);
                    BuildRemoteFontBundles.BuildWebGlFontBundles();
                    Invalidate();
                    GUIUtility.ExitGUI();
                }
                GUI.backgroundColor = previous;
            }

            EditorGUILayout.LabelField(
                "Assar antes, buildar depois: o bake lê os characters_<lang>.txt e o build empacota " +
                "o que o bake produziu, gravando o manifesto a partir dos artefatos reais.",
                EditorStyles.wordWrappedMiniLabel
            );

            EndStep();
        }

        private void DrawUploadStep()
        {
            var key = UploadDoneKeyPrefix + (_state.manifestGeneratedAt ?? "none");
            var acknowledged = EditorPrefs.GetBool(key, false);

            BeginStep("Enviar para o CDN", acknowledged,
                "O único passo que nenhuma checagem alcança: os arquivos precisam existir no seu CDN.");

            if (!_state.manifestOk)
            {
                EditorGUILayout.HelpBox("Construa os bundles primeiro.", MessageType.Info);
                EndStep();
                return;
            }

            EditorGUILayout.LabelField("Arquivos a enviar:", EditorStyles.miniBoldLabel);
            foreach (var line in _state.manifestFiles)
                EditorGUILayout.LabelField("  " + line, EditorStyles.miniLabel);

            EditorGUILayout.LabelField(
                "O caminho completo depende do Base Bundle URL e do Game Id do RuntimeLocaleDownloader — " +
                "o inspector dele mostra a URL final de cada idioma. No Editor, sem CDN configurado, " +
                "o jogo lê direto desta pasta.",
                EditorStyles.wordWrappedMiniLabel
            );

            EditorGUILayout.Space(2);
            if (GUILayout.Button("Revelar pasta dos bundles", GUILayout.Height(20)))
            {
                var full = Path.GetFullPath(_state.outputFolder);
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

        private List<string> CollectProblems()
        {
            var problems = new List<string>();

            foreach (var problem in LanguageCode.ValidateTables())
                problems.Add("Tabela de idiomas: " + problem);

            if (_state.configPaths.Length > 1)
            {
                problems.Add(
                    $"Há {_state.configPaths.Length} RemoteFontBundleBuildConfig no projeto e qual vale " +
                    "não é estável: " + string.Join(", ", _state.configPaths)
                );
            }

            var manifest = FontBundleManifest.FindExisting();
            var output = _state.outputFolder;

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

                    if (!File.Exists(Path.Combine(output, bundleName + ".ft")))
                        problems.Add($"'{bundleName}' não foi construído ainda.");
                    else if (manifest?.Find(language) == null)
                        problems.Add($"'{bundleName}' existe em disco mas não está no manifesto — rode Build Bundles.");
                }
            }

            if (manifest?.entries != null)
            {
                foreach (var entry in manifest.entries)
                {
                    if (entry != null && !File.Exists(Path.Combine(output, entry.bundleFileName)))
                        problems.Add($"O manifesto declara '{entry.bundleFileName}', que não existe em {output}.");
                }
            }

            foreach (var language in _state.needBundle)
            {
                if (_buildConfig == null || _buildConfig.FindEntryForLanguage(language) == null)
                    problems.Add($"A coluna '{language}' precisa de fonte remota e não tem entrada de bundle.");
            }

            return problems;
        }

        // ---------------------------------------------------------------- Helpers

        private bool Busy => !string.IsNullOrEmpty(_busyLabel);

        private static string CharactersAllPath =>
            "Assets/FineLocalization/Editor/GeneratedCharacters/characters_all.txt";

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

        private string ResolveSaveFolder()
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
