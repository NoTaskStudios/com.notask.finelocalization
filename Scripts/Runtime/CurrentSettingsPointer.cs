using System;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FineLocalization.Runtime
{
    public class CurrentSettingsPointer : ScriptableObject
    {
        private const string pointerDefaultName = "CurrentSettingsPointer";
        private const string settingsDefaultName = "LocalizationSettings";
        
        private const string folderPath = "Assets/FineLocalization/Resources";
        private const string localizationFolderPath = folderPath + "/Localization";
        private const string pointerPath = folderPath + "/" + pointerDefaultName + ".asset";
        private const string settingsPath = folderPath + "/" + settingsDefaultName + ".asset";
        
        public LocalizationSettings settings;

        private static CurrentSettingsPointer _instance;
        public static CurrentSettingsPointer SettingsPointer
        {
            get
            {
                _instance ??= Resources.Load<CurrentSettingsPointer>(pointerDefaultName);
                #if UNITY_EDITOR
                return _instance ??= CreateSettingsPointer();
                #else
                if (_instance == null) throw new Exception($"LocalizationSettins pointer not found: {folderPath}/{pointerDefaultName}");
                #endif
                return _instance;
            }
        }

        private static LocalizationSettings _settings => SettingsPointer?.settings;
        public static LocalizationSettings CurrentSettings
        {
            get => _settings ?? LoadSettings();
            set => SettingsPointer.settings = value;
        }

        private static LocalizationSettings LoadSettings()
        {
            // Tenta carregar o asset
            var settings = Resources.Load<LocalizationSettings>("LocalizationSettings");

#if UNITY_EDITOR
            if (settings == null) settings = CreateSettings();
#else
            if (settings == null)
                throw new Exception($"Localization settings não encontrado em: {settingsPath}");
#endif

            return settings;
        }

#if UNITY_EDITOR
        
        // Garante a existência das pastas
        private static void EnsureUsedFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FineLocalization"))
                AssetDatabase.CreateFolder("Assets", "FineLocalization");

            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets/FineLocalization", "Resources");

            if (!AssetDatabase.IsValidFolder(localizationFolderPath))
                AssetDatabase.CreateFolder(folderPath, "Localization");
        }
        
        private static CurrentSettingsPointer CreateSettingsPointer()
        {
            EnsureUsedFolders();
            var pointer = CreateInstance<CurrentSettingsPointer>();
            _instance = pointer;
            AssetDatabase.CreateAsset(pointer, pointerPath);
            pointer.settings = CurrentSettings;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return pointer;
        }

        private static LocalizationSettings CreateSettings()
        {
            EnsureUsedFolders();
            var settings = CreateInstance<LocalizationSettings>();

            // Cria o asset fora do package
            AssetDatabase.CreateAsset(settings, settingsPath);

            //Debug.log($"[FineLocalization] Criado novo LocalizationSettings em: {settingsPath}");

            // Garante que a pasta SaveFolder aponte corretamente
            settings.SaveFolder = AssetDatabase.LoadAssetAtPath<Object>(localizationFolderPath);
            SettingsPointer.settings = settings;
            
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }
#endif
    }
}
