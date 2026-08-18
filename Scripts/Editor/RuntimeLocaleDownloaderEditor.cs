#if UNITY_EDITOR

using System.Linq;
using FineLocalization.Runtime;
using FineLocalization.Scripts.Runtime;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Inspector do <see cref="RuntimeLocaleDownloader"/>. Além dos campos, mostra qual idioma a
    /// configuração atual vai resolver no boot — a dúvida "por que abriu em inglês?" some antes
    /// de virar bug — e qual URL de fonte cada idioma do manifesto vai pedir.
    /// </summary>
    [CustomEditor(typeof(RuntimeLocaleDownloader))]
    public class RuntimeLocaleDownloaderEditor : UnityEditor.Editor
    {
        private SerializedProperty _source;
        private SerializedProperty _csvUrlOverride;
        private SerializedProperty _startupLanguage;
        private SerializedProperty _useUrlQueryLanguage;
        private SerializedProperty _useSystemLanguage;
        private SerializedProperty _fallbackLanguage;
        private SerializedProperty _fontMode;
        private SerializedProperty _baseBundleUrl;
        private SerializedProperty _gameId;
        private SerializedProperty _mainFontAssets;
        private SerializedProperty _coverageFallbackFontAssets;
        private SerializedProperty _rebuildBatchSize;
        private SerializedProperty _extraLatinPrefixes;
        private SerializedProperty _forceRemoteFontPrefixes;
        private SerializedProperty _network;

        private bool _showAdvancedFonts;

        private void OnEnable() => BindProperties();

        private void BindProperties()
        {
            var so = serializedObject;
            _source = so.FindProperty("source");
            _csvUrlOverride = so.FindProperty("csvUrlOverride");
            _startupLanguage = so.FindProperty("startupLanguage");
            _useUrlQueryLanguage = so.FindProperty("useUrlQueryLanguage");
            _useSystemLanguage = so.FindProperty("useSystemLanguage");
            _fallbackLanguage = so.FindProperty("fallbackLanguage");
            _fontMode = so.FindProperty("fontMode");
            _network = so.FindProperty("network");

            var fonts = so.FindProperty("remoteFonts");
            _baseBundleUrl = fonts?.FindPropertyRelative("baseBundleUrl");
            _gameId = fonts?.FindPropertyRelative("gameId");
            _mainFontAssets = fonts?.FindPropertyRelative("mainFontAssets");
            _coverageFallbackFontAssets = fonts?.FindPropertyRelative("coverageFallbackFontAssets");
            _rebuildBatchSize = fonts?.FindPropertyRelative("rebuildBatchSize");
            _extraLatinPrefixes = fonts?.FindPropertyRelative("extraLatinPrefixes");
            _forceRemoteFontPrefixes = fonts?.FindPropertyRelative("forceRemoteFontPrefixes");
        }

        public override void OnInspectorGUI()
        {
            // A Unity destrói os SerializedProperty num domain reload mas mantém este editor vivo.
            if (_source == null || _fontMode == null)
                BindProperties();

            if (_source == null || _fontMode == null)
            {
                EditorGUILayout.HelpBox("Reselecione o objeto após a Unity terminar de recompilar.", MessageType.Info);
                return;
            }

            serializedObject.Update();

            DrawSource();
            DrawLanguage();
            DrawFonts();
            DrawNetwork();
            DrawRuntimeStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSource()
        {
            EditorGUILayout.LabelField("Planilha", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_source, new GUIContent("Csv Source"));

            var mode = (RuntimeLocaleDownloader.CsvSource)_source.enumValueIndex;
            var hasProxy = !string.IsNullOrWhiteSpace(_csvUrlOverride.stringValue);

            if (mode != RuntimeLocaleDownloader.CsvSource.Bundled)
                EditorGUILayout.PropertyField(_csvUrlOverride, new GUIContent("Csv Url Override"));

            switch (mode)
            {
                case RuntimeLocaleDownloader.CsvSource.Auto:
                    EditorGUILayout.HelpBox(
                        hasProxy
                            ? "Auto + proxy configurado: baixa em todas as plataformas, inclusive WebGL."
                            : "Auto: baixa em Editor/Desktop/Mobile. Em WebGL usa os CSVs embutidos, porque o " +
                              "Google Sheets responde ao export sem cabeçalho CORS e o navegador bloqueia. " +
                              "Preencha Csv Url Override com um proxy/CDN para baixar também em WebGL.",
                        MessageType.Info
                    );
                    break;

                case RuntimeLocaleDownloader.CsvSource.Remote:
                    EditorGUILayout.HelpBox(
                        hasProxy
                            ? "Remote: sempre baixa, usando a URL configurada."
                            : "Remote sem Csv Url Override: em WebGL o download vai falhar por CORS e o jogo cairá " +
                              "nos CSVs embutidos. Use Auto se esse é o comportamento desejado.",
                        hasProxy ? MessageType.Info : MessageType.Warning
                    );
                    break;

                case RuntimeLocaleDownloader.CsvSource.Bundled:
                    EditorGUILayout.HelpBox(
                        "Bundled: nunca baixa. Sincronize as planilhas no Editor antes do build " +
                        "(Tools → Fine Localization → Setup & Update).",
                        MessageType.Info
                    );
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLanguage()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Idioma", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_startupLanguage, new GUIContent("Startup Language"));
            EditorGUILayout.PropertyField(_useUrlQueryLanguage, new GUIContent("Usar ?lang= da URL"));
            EditorGUILayout.PropertyField(_useSystemLanguage, new GUIContent("Usar idioma do sistema"));
            EditorGUILayout.PropertyField(_fallbackLanguage, new GUIContent("Fallback Language"));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Ordem de resolução", DescribeResolutionChain(), EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.HelpBox(
                "Localization.SetLanguage(\"ja-jp\") vence toda a cadeia e pode ser chamado a qualquer " +
                "momento — inclusive antes da planilha terminar de baixar. O download nunca espera " +
                "pelo idioma: o CSV traz todas as colunas.",
                MessageType.None
            );

            EditorGUILayout.EndVertical();
        }

        private string DescribeResolutionChain()
        {
            var steps = new System.Collections.Generic.List<string> { "Localization.SetLanguage()" };

            if (!string.IsNullOrWhiteSpace(_startupLanguage.stringValue))
                steps.Add($"Startup Language ({_startupLanguage.stringValue})");

            if (_useUrlQueryLanguage.boolValue)
                steps.Add("?lang= da URL");

            if (_useSystemLanguage.boolValue)
                steps.Add("idioma do sistema");

            var fallback = string.IsNullOrWhiteSpace(_fallbackLanguage.stringValue)
                ? LocalizationManager.DefaultLanguage
                : _fallbackLanguage.stringValue;

            steps.Add($"fallback ({fallback})");
            return string.Join("  →  ", steps);
        }

        private void DrawFonts()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Fontes", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_fontMode, new GUIContent("Font Mode"));

            var mode = (RuntimeLocaleDownloader.FontMode)_fontMode.enumValueIndex;
            if (mode == RuntimeLocaleDownloader.FontMode.LatinOnly)
            {
                EditorGUILayout.HelpBox(
                    "Latin Only: nada é baixado. Só os idiomas cobertos pelas fontes embutidas do " +
                    "projeto são aplicados; os demais caem no Fallback Language com um aviso no log. " +
                    "É o modo que garante nunca renderizar caractere faltando.",
                    MessageType.None
                );
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.PropertyField(_baseBundleUrl, new GUIContent("Base Bundle URL"));
            DrawGameIdWithPlaceholder();

            DrawManifestStatus(mode);

            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(_mainFontAssets, new GUIContent("Main Font Assets"), true);
            if (_mainFontAssets.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "Sem Main Font Assets não há onde instalar a fonte remota como fallback — o " +
                    "download acontece e não muda nada na tela.",
                    MessageType.Warning
                );
            }

            EditorGUILayout.PropertyField(_coverageFallbackFontAssets, new GUIContent("Coverage Fallback Fonts"), true);

            EditorGUILayout.Space(2);
            _showAdvancedFonts = EditorGUILayout.Foldout(_showAdvancedFonts, "Avançado", true);
            if (_showAdvancedFonts)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_rebuildBatchSize, new GUIContent("Rebuild Batch Size"));
                EditorGUILayout.PropertyField(_extraLatinPrefixes, new GUIContent("Extra Latin Prefixes"), true);
                EditorGUILayout.PropertyField(_forceRemoteFontPrefixes, new GUIContent("Force Remote Font Prefixes"), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>Mostra o Product Name em cinza quando o Game Id está vazio, que é o default real.</summary>
        private void DrawGameIdWithPlaceholder()
        {
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.PropertyField(rect, _gameId, new GUIContent("Game Id"));

            if (!string.IsNullOrEmpty(_gameId.stringValue))
                return;

            var placeholder = RuntimeLocaleDownloader.GetDefaultGameId();
            if (string.IsNullOrEmpty(placeholder))
                return;

            rect.xMin += EditorGUIUtility.labelWidth + 2f;
            var style = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Italic,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };
            EditorGUI.LabelField(rect, placeholder, style);
        }

        /// <summary>
        /// O que o manifesto declara. É a resposta para "configurei tudo e a fonte não baixa":
        /// sem manifesto não há bundle nenhum, por mais que a URL esteja certa.
        /// </summary>
        private void DrawManifestStatus(RuntimeLocaleDownloader.FontMode mode)
        {
            var manifest = FontBundleManifest.FindExisting();

            if (manifest == null || !manifest.HasAnyBundle)
            {
                EditorGUILayout.HelpBox(
                    manifest == null
                        ? "Nenhum manifesto de fontes no projeto. Rode Tools → Fine Localization → " +
                          "Setup & Update → Build Bundles. " +
                          (mode == RuntimeLocaleDownloader.FontMode.Auto
                              ? "Em Auto, sem manifesto o jogo roda como Latin Only."
                              : "Em Remote, todo idioma não-latino vai cair no fallback.")
                        : "O manifesto existe mas está vazio — nenhum bundle foi construído ainda.",
                    mode == RuntimeLocaleDownloader.FontMode.Auto ? MessageType.Info : MessageType.Warning
                );
                return;
            }

            var baseUrl = _baseBundleUrl.stringValue;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                EditorGUILayout.HelpBox(
                    $"O manifesto tem {manifest.entries.Count} bundle(s), mas Base Bundle URL está vazia. " +
                    (mode == RuntimeLocaleDownloader.FontMode.Auto
                        ? "Em Auto, isso equivale a Latin Only."
                        : "Nada será baixado."),
                    MessageType.Warning
                );
                return;
            }

            var gameSegment = string.IsNullOrWhiteSpace(_gameId.stringValue)
                ? RuntimeLocaleDownloader.GetDefaultGameId()
                : _gameId.stringValue.Trim();

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField($"Bundles no manifesto ({manifest.entries.Count})", EditorStyles.miniBoldLabel);

            var root = baseUrl.Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(gameSegment))
                root += "/" + gameSegment;

            foreach (var entry in manifest.entries)
            {
                if (entry == null)
                    continue;

                EditorGUILayout.LabelField($"  {entry.language}", $"{root}/{entry.bundleFileName}", EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(
                "  ",
                "Esses arquivos precisam existir no CDN nesses caminhos.",
                EditorStyles.wordWrappedMiniLabel
            );
        }

        private void DrawNetwork()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_network, new GUIContent("Rede"), true);
        }

        private void DrawRuntimeStatus()
        {
            if (!Application.isPlaying)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Pronto", Localization.IsReady ? $"sim ({(Localization.LoadSucceeded ? "ok" : "com falhas")})" : "carregando...");
            EditorGUILayout.LabelField("Idioma aplicado", Localization.Language);
            EditorGUILayout.LabelField("Fonte remota", Localization.FontReady ? "instalada" : "não usada");

            var available = Localization.AvailableLanguages.ToArray();
            EditorGUILayout.LabelField("Disponíveis", available.Length == 0 ? "—" : string.Join(", ", available));

            EditorGUILayout.Space(2);

            using (new EditorGUI.DisabledScope(available.Length == 0))
            {
                var current = Mathf.Max(0, System.Array.IndexOf(available, Localization.Language));
                var picked = EditorGUILayout.Popup("Trocar para", current, available);
                if (available.Length > 0 && picked != current)
                    Localization.SetLanguage(available[picked]);
            }

            if (GUILayout.Button("Recarregar planilhas"))
                Localization.Reload();

            EditorGUILayout.EndVertical();
        }
    }
}

#endif
