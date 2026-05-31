#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Single window to manage <see cref="RemoteFontBundleBuildConfig"/> entries and build
    /// the WebGL AssetBundles in one place. Nothing is hardcoded — every project sets its own
    /// language list here.
    /// </summary>
    public class RemoteFontBundleBuildWindow : EditorWindow
    {
        private RemoteFontBundleBuildConfig _config;
        private SerializedObject _serializedConfig;
        private Vector2 _scroll;

        [MenuItem("Tools/Fine Localization/WebGL Remote Fonts/Open Bundle Builder Window", false, 60)]
        public static void Open()
        {
            var window = GetWindow<RemoteFontBundleBuildWindow>("Remote Font Bundles");
            window.minSize = new Vector2(560, 380);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureConfig();
        }

        private void EnsureConfig()
        {
            if (_config != null) return;
            _config = RemoteFontBundleBuildConfig.GetOrCreate();
            _serializedConfig = new SerializedObject(_config);
        }

        private void OnGUI()
        {
            EnsureConfig();
            if (_config == null)
            {
                EditorGUILayout.HelpBox("Não foi possível carregar RemoteFontBundleBuildConfig.", MessageType.Error);
                return;
            }

            _serializedConfig.Update();

            DrawHeader();
            DrawOutputFolder();
            DrawAutoBakeSettings();
            DrawEntries();
            DrawBottomActions();

            if (_serializedConfig.ApplyModifiedProperties())
                EditorUtility.SetDirty(_config);
        }

        // -------------------------------------------------------------------
        // UI sections
        // -------------------------------------------------------------------

        private void DrawHeader()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Remote Font Bundles (WebGL)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Cada entry vira um AssetBundle separado para WebGL.\n" +
                "1) Dê um nome ao bundle (ex: font_ar, font_he, font_vi).\n" +
                "2) Arraste a pasta com o(s) TMP_FontAsset desse idioma.\n" +
                "3) Clique em Build WebGL Bundles.\n\n" +
                "Adicione/remova quantos idiomas precisar — totalmente dinâmico.",
                MessageType.Info
            );

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Config asset:", GUILayout.Width(90));
            EditorGUILayout.ObjectField(_config, typeof(RemoteFontBundleBuildConfig), false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        private void DrawOutputFolder()
        {
            var outputProp = _serializedConfig.FindProperty(nameof(RemoteFontBundleBuildConfig.outputFolder));
            EditorGUILayout.PropertyField(outputProp, new GUIContent("Output folder"));
            EditorGUILayout.Space(6);
        }

        private void DrawAutoBakeSettings()
        {
            EditorGUILayout.LabelField("Auto-Bake (opcional)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "Assa o TMP_FontAsset estático automaticamente a partir do characters_<lang>.txt " +
                "(gerado no sync das planilhas) + a Source Font de cada entry. Atlas SDF, sem .ttf no bundle. " +
                "Você ainda pode assar manualmente pelo Font Asset Creator se preferir.",
                MessageType.None
            );
            var autoSizeProp = _serializedConfig.FindProperty(nameof(RemoteFontBundleBuildConfig.autoSizeToAtlas));
            EditorGUILayout.PropertyField(autoSizeProp, new GUIContent("Auto Size (cabe em 1 atlas)"));
            EditorGUILayout.PropertyField(_serializedConfig.FindProperty(nameof(RemoteFontBundleBuildConfig.atlasSize)), new GUIContent("Atlas Size"));

            using (new EditorGUI.DisabledScope(autoSizeProp.boolValue))
                EditorGUILayout.PropertyField(_serializedConfig.FindProperty(nameof(RemoteFontBundleBuildConfig.samplingPointSize)), new GUIContent("Sampling Point Size (fallback)"));

            EditorGUILayout.PropertyField(_serializedConfig.FindProperty(nameof(RemoteFontBundleBuildConfig.paddingPercent)), new GUIContent("Padding (% do point size)"));

            if (autoSizeProp.boolValue)
                EditorGUILayout.HelpBox("Auto Size ligado: o point size é calculado pra encaixar todos os glifos em 1 página. O campo acima vira só fallback.", MessageType.None);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        private void DrawEntries()
        {
            EditorGUILayout.LabelField("Bundles", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(180));

            var entriesProp = _serializedConfig.FindProperty(nameof(RemoteFontBundleBuildConfig.entries));
            int removeIndex = -1;

            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                var entryProp = entriesProp.GetArrayElementAtIndex(i);
                var bundleNameProp = entryProp.FindPropertyRelative("bundleName");
                var folderProp = entryProp.FindPropertyRelative("folder");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(bundleNameProp, new GUIContent("Bundle File Name"));
                if (GUILayout.Button("✕", GUILayout.Width(28)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(folderProp, new GUIContent("Remote TMP Font Folder"));
                EditorGUILayout.PropertyField(entryProp.FindPropertyRelative("sourceFont"), new GUIContent("Source Font (auto-bake)"));
                DrawBundlePreview(bundleNameProp.stringValue);

                DrawEntryValidation(folderProp.objectReferenceValue);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();

            if (removeIndex >= 0)
                entriesProp.DeleteArrayElementAtIndex(removeIndex);

            if (GUILayout.Button("+ Add entry", GUILayout.Height(24)))
                entriesProp.arraySize++;
        }

        private static void DrawBundlePreview(string bundleName)
        {
            if (string.IsNullOrWhiteSpace(bundleName))
                return;

            var fileName = bundleName.EndsWith(".ft", System.StringComparison.OrdinalIgnoreCase)
                ? bundleName
                : bundleName + ".ft";

            EditorGUILayout.LabelField("Generated File", fileName, EditorStyles.miniLabel);
        }

        private static void DrawEntryValidation(Object folderAsset)
        {
            if (folderAsset == null)
            {
                EditorGUILayout.HelpBox("Sem pasta atribuída.", MessageType.None);
                return;
            }

            var path = AssetDatabase.GetAssetPath(folderAsset);
            if (!AssetDatabase.IsValidFolder(path))
            {
                EditorGUILayout.HelpBox("Asset selecionado não é uma pasta.", MessageType.Warning);
                return;
            }

            var guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { path });
            var validCount = 0;
            if (guids != null)
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!string.Equals(Path.GetExtension(assetPath), ".asset", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null)
                        validCount++;
                }
            }

            if (validCount == 0)
                EditorGUILayout.HelpBox($"Nenhum TMP_FontAsset em '{path}'.", MessageType.Warning);
            else
                EditorGUILayout.LabelField($"✔ {validCount} TMP_FontAsset .asset(s) em '{path}'", EditorStyles.miniLabel);
        }

        private void DrawBottomActions()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save Config", GUILayout.Height(28)))
            {
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssetIfDirty(_config);
            }

            var prevBg = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.75f, 0.9f, 0.6f);
            if (GUILayout.Button("⚙ Generate Font Assets", GUILayout.Height(28)))
            {
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssetIfDirty(_config);
                GenerateRemoteFontAssets.GenerateAll(_config);
            }

            GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
            if (GUILayout.Button("▶ Build WebGL Bundles", GUILayout.Height(28)))
            {
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssetIfDirty(_config);
                BuildRemoteFontBundles.BuildWebGlFontBundles();
            }

            GUI.backgroundColor = prevBg;

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
