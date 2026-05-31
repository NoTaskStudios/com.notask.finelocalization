#if UNITY_EDITOR

using System;
using System.IO;
using FineLocalization.Scripts.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    [CustomEditor(typeof(RemoteFontBundleLoader))]
    public class RemoteFontBundleLoaderEditor : UnityEditor.Editor
    {
        private SerializedProperty _baseBundleUrl;
        private SerializedProperty _useGlobalLanguage;
        private SerializedProperty _gameId;
        private SerializedProperty _bundleFileExtension;
        private SerializedProperty _bundles;
        private SerializedProperty _mainFontAssets;
        private SerializedProperty _rebuildBatchSize;
        private SerializedProperty _ignoredFontNameContains;
        private SerializedProperty _extraLatinPrefixes;
        private SerializedProperty _forceRemoteFontPrefixes;
        private SerializedProperty _testLanguage;

        private void OnEnable()
        {
            _baseBundleUrl = serializedObject.FindProperty("baseBundleUrl");
            _useGlobalLanguage = serializedObject.FindProperty("useGlobalLanguage");
            _gameId = serializedObject.FindProperty("gameId");
            _bundleFileExtension = serializedObject.FindProperty("bundleFileExtension");
            _bundles = serializedObject.FindProperty("bundles");
            _mainFontAssets = serializedObject.FindProperty("mainFontAssets");
            _rebuildBatchSize = serializedObject.FindProperty("rebuildBatchSize");
            _ignoredFontNameContains = serializedObject.FindProperty("ignoredFontNameContains");
            _extraLatinPrefixes = serializedObject.FindProperty("extraLatinPrefixes");
            _forceRemoteFontPrefixes = serializedObject.FindProperty("forceRemoteFontPrefixes");
            _testLanguage = serializedObject.FindProperty("testLanguage");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawRemoteSource();
            DrawBundleMappings();
            DrawFallbackTargets();
            DrawAdvanced();
            DrawManualTest();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRemoteSource()
        {
            EditorGUILayout.LabelField("Remote Bundle Source", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_baseBundleUrl, new GUIContent("Base Bundle URL"));
            EditorGUILayout.PropertyField(_useGlobalLanguage, new GUIContent("Global Language"));

            if (!_useGlobalLanguage.boolValue)
                DrawGameIdWithPlaceholder();

            EditorGUILayout.PropertyField(_bundleFileExtension, new GUIContent("Bundle Extension"));

            if (_useGlobalLanguage.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Global Language ON: Base + /font_<languagePrefix> + Extension — mesma pasta /languages/ pra todos os jogos. " +
                    "Ex: .../languages/font_ja-jp.ft. Use com fontes que contêm todos os caracteres (bundles maiores).",
                    MessageType.None
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Global Language OFF: Base + /<Game Id> + /font_<languagePrefix> + Extension — fontes otimizadas por jogo. " +
                    "Ex: .../languages/trevor/font_ja-jp.ft. Game Id vazio = Product Name do Player Settings.",
                    MessageType.None
                );
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawGameIdWithPlaceholder()
        {
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.PropertyField(rect, _gameId, new GUIContent("Game Id"));

            if (!string.IsNullOrEmpty(_gameId.stringValue))
                return;

            var placeholder = RemoteFontBundleLoader.GetDefaultGameId();
            if (string.IsNullOrEmpty(placeholder))
                return;

            var placeholderRect = rect;
            placeholderRect.xMin += EditorGUIUtility.labelWidth + 2f;

            var style = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Italic,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f, 0.85f) }
            };

            EditorGUI.LabelField(placeholderRect, placeholder, style);
        }

        private void DrawBundleMappings()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Remote Font Mappings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "Remote TMP Font Asset Name is the exact TMP_FontAsset asset.name inside the remote AssetBundle. Keep it as text: a local object reference would not represent the downloaded bundle asset and can accidentally pull fonts into the build.",
                MessageType.Info
            );

            if (GUILayout.Button("Fill Missing From Bundle Builder Config", GUILayout.Height(24)))
                FillMissingRemoteFontNamesFromBuilderConfig();

            if (GUILayout.Button("Add Missing Mappings From Builder Config", GUILayout.Height(24)))
                AddMissingMappingsFromBuilderConfig();

            int removeIndex = -1;
            for (int i = 0; i < _bundles.arraySize; i++)
            {
                var entry = _bundles.GetArrayElementAtIndex(i);
                var languagePrefix = entry.FindPropertyRelative("languagePrefix");
                var fontAssetName = entry.FindPropertyRelative("fontAssetName");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(languagePrefix, new GUIContent("Language Prefix"));
                if (GUILayout.Button("X", GUILayout.Width(24)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(fontAssetName, new GUIContent("Remote TMP Font Asset Name"));
                DrawRuntimeBundleNamePreview(languagePrefix.stringValue);
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0)
                _bundles.DeleteArrayElementAtIndex(removeIndex);

            if (GUILayout.Button("+ Add Font Mapping", GUILayout.Height(24)))
                _bundles.arraySize++;

            EditorGUILayout.EndVertical();
        }

        private void DrawFallbackTargets()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Local Fallback Targets", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_mainFontAssets, new GUIContent("Main Local TMP Fonts"), true);
            EditorGUILayout.HelpBox(
                "These are local/base fonts that receive the downloaded remote font as a fallback. Put your project's normal UI font here, not the remote CJK font.",
                MessageType.None
            );
            EditorGUILayout.EndVertical();
        }

        private void DrawAdvanced()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_rebuildBatchSize);
            EditorGUILayout.PropertyField(_ignoredFontNameContains, true);
            EditorGUILayout.PropertyField(_extraLatinPrefixes, true);
            EditorGUILayout.PropertyField(_forceRemoteFontPrefixes, true);
            EditorGUILayout.EndVertical();
        }

        private void DrawManualTest()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Manual Test", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_testLanguage);
            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeBundleNamePreview(string languagePrefix)
        {
            if (string.IsNullOrWhiteSpace(languagePrefix))
                return;

            var normalized = languagePrefix.Trim().ToLowerInvariant();
            var extension = string.IsNullOrWhiteSpace(_bundleFileExtension.stringValue) ? ".ft" : _bundleFileExtension.stringValue.Trim();
            var fileName = $"font_{normalized}{extension}";

            string path;
            if (_useGlobalLanguage.boolValue)
            {
                path = fileName;
            }
            else
            {
                var game = string.IsNullOrWhiteSpace(_gameId.stringValue)
                    ? RemoteFontBundleLoader.GetDefaultGameId()
                    : _gameId.stringValue.Trim();
                path = string.IsNullOrEmpty(game) ? fileName : $"{game}/{fileName}";
            }

            EditorGUILayout.LabelField("Runtime Bundle Path", path, EditorStyles.miniLabel);
        }

        private void FillMissingRemoteFontNamesFromBuilderConfig()
        {
            var config = RemoteFontBundleBuildConfig.GetOrCreate();
            if (config?.entries == null)
                return;

            for (int i = 0; i < _bundles.arraySize; i++)
            {
                var entry = _bundles.GetArrayElementAtIndex(i);
                var languagePrefix = entry.FindPropertyRelative("languagePrefix");
                var fontAssetName = entry.FindPropertyRelative("fontAssetName");

                if (!string.IsNullOrWhiteSpace(fontAssetName.stringValue))
                    continue;

                var assetName = FindRemoteFontAssetName(config, languagePrefix.stringValue);
                if (!string.IsNullOrWhiteSpace(assetName))
                    fontAssetName.stringValue = assetName;
            }
        }

        private void AddMissingMappingsFromBuilderConfig()
        {
            var config = RemoteFontBundleBuildConfig.GetOrCreate();
            if (config?.entries == null)
                return;

            for (int i = 0; i < config.entries.Count; i++)
            {
                var entry = config.entries[i];
                if (entry == null)
                    continue;

                var language = NormalizeBundleLanguage(entry.bundleName);
                if (string.IsNullOrEmpty(language) || HasMappingForLanguage(language))
                    continue;

                var index = _bundles.arraySize;
                _bundles.arraySize++;

                var mapping = _bundles.GetArrayElementAtIndex(index);
                mapping.FindPropertyRelative("languagePrefix").stringValue = language;
                mapping.FindPropertyRelative("fontAssetName").stringValue = FindRemoteFontAssetName(config, language) ?? string.Empty;
            }
        }

        private bool HasMappingForLanguage(string language)
        {
            for (int i = 0; i < _bundles.arraySize; i++)
            {
                var entry = _bundles.GetArrayElementAtIndex(i);
                var existing = NormalizeLanguage(entry.FindPropertyRelative("languagePrefix").stringValue);
                if (IsSameLanguageOrRoot(existing, language))
                    return true;
            }

            return false;
        }

        private static string FindRemoteFontAssetName(RemoteFontBundleBuildConfig config, string languagePrefix)
        {
            var normalizedPrefix = NormalizeLanguage(languagePrefix);
            if (string.IsNullOrEmpty(normalizedPrefix))
                return null;

            for (int i = 0; i < config.entries.Count; i++)
            {
                var entry = config.entries[i];
                if (entry == null || entry.folder == null)
                    continue;

                var bundleLanguage = NormalizeBundleLanguage(entry.bundleName);
                if (!IsSameLanguageOrRoot(bundleLanguage, normalizedPrefix))
                    continue;

                var folderPath = AssetDatabase.GetAssetPath(entry.folder);
                var guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { folderPath });
                if (guids == null || guids.Length == 0)
                    return null;

                Array.Sort(guids, StringComparer.OrdinalIgnoreCase);
                for (int g = 0; g < guids.Length; g++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guids[g]);
                    if (!string.Equals(Path.GetExtension(assetPath), ".asset", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                    if (font != null)
                        return font.name;
                }
            }

            return null;
        }

        private static string NormalizeBundleLanguage(string bundleName)
        {
            var value = Path.GetFileNameWithoutExtension(bundleName ?? string.Empty).Trim().ToLowerInvariant();
            if (value.StartsWith("font_", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("font_".Length);

            return NormalizeLanguage(value);
        }

        private static string NormalizeLanguage(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace('_', '-').ToLowerInvariant();
        }

        private static bool IsSameLanguageOrRoot(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            return a.StartsWith(b + "-", StringComparison.OrdinalIgnoreCase) ||
                   b.StartsWith(a + "-", StringComparison.OrdinalIgnoreCase);
        }
    }
}

#endif
