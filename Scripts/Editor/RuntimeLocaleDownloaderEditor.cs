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
    /// de virar bug.
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
        private SerializedProperty _fontLoader;
        private SerializedProperty _network;

        private void OnEnable()
        {
            var so = serializedObject;
            _source = so.FindProperty("source");
            _csvUrlOverride = so.FindProperty("csvUrlOverride");
            _startupLanguage = so.FindProperty("startupLanguage");
            _useUrlQueryLanguage = so.FindProperty("useUrlQueryLanguage");
            _useSystemLanguage = so.FindProperty("useSystemLanguage");
            _fallbackLanguage = so.FindProperty("fallbackLanguage");
            _fontLoader = so.FindProperty("fontLoader");
            _network = so.FindProperty("network");
        }

        public override void OnInspectorGUI()
        {
            if (_source == null)
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
                        "(Tools → Fine Localization → Sheets → Sync from Google).",
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

            EditorGUILayout.PropertyField(_fontLoader, new GUIContent("Remote Font Loader"));

            if (_fontLoader.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Vazio: um RemoteFontBundleLoader é procurado na cena em runtime (inclusive inativo). " +
                    "Sem nenhum, idiomas fora do script Latin caem no fallback em vez de virar caixas vazias.",
                    MessageType.None
                );
            }

            EditorGUILayout.EndVertical();
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
