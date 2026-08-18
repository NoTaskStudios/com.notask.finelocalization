using System;
using System.Collections.Generic;
using System.Linq;
using FineLocalization.Utils;
using UnityEngine;
using com.notask.finelocalization.Scripts.Runtime.Utils;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FineLocalization.Runtime
{
    [CreateAssetMenu(fileName = "LocalizationSettings", menuName = "Fine Localization/Settings")]
    public class LocalizationSettings : ScriptableObject
    {
        public enum LocalizationMode
        {
            Production,
            Development
        }

        /// <summary>
        /// Reescrita de um código de idioma antes de qualquer regra interna do pacote.
        /// </summary>
        [Serializable]
        public class LanguageAlias
        {
            [Tooltip("Código exatamente como o host manda. Ex: uk")]
            public string from;

            [Tooltip("Código a usar no lugar. Ex: en-gb")]
            public string to;
        }

        [Tooltip("Enable FineLocalization info, warning and error logs. Keep disabled for lighter WebGL builds.")]
        public bool EnableLogs = false;

        [Tooltip("Chooses which source list is used by runtime, editor download/resolve, and builds.")]
        public LocalizationMode Mode = LocalizationMode.Production;

        public List<LocalizationSource> Sources = new();
        public List<LocalizationSource> DevSources = new();
        public UnityEngine.Object SaveFolder;
        public int skip = 0;

        [Header("Códigos de idioma")]
        [Tooltip("Overrides para o que o host manda. Ex: 'uk' → 'en-gb', 'es' → 'es-mx'.\n\n" +
                 "Só é necessário para ambiguidade genuína entre idioma e região — 'uk' é ucraniano " +
                 "e Reino Unido ao mesmo tempo, e o mesmo vale para ca, ch, be, sg e my. " +
                 "Casos como 'cn' → chinês ou 'jp' → japonês já funcionam sem configurar nada.\n\n" +
                 "Um alias vence toda regra interna, mas nunca sombreia uma coluna que exista " +
                 "com o nome exato do que foi pedido.")]
        public List<LanguageAlias> LanguageAliases = new();

        public static string UrlPattern = "https://docs.google.com/spreadsheets/d/{0}/export?format=csv&gid={1}";
        public static DateTime Timestamp;

        public static LocalizationSettings Instance => CurrentSettingsPointer.CurrentSettings;

        public static event Action OnRunEditor;

        public List<LocalizationSource> GetActiveSources()
        {
            return Mode == LocalizationMode.Development
                ? (DevSources ??= new List<LocalizationSource>())
                : (Sources ??= new List<LocalizationSource>());
        }

        /// <summary>
        /// Empurra <see cref="LanguageAliases"/> para o <see cref="LanguageCode"/>. Empurrado e não
        /// puxado de propósito: assim <c>LanguageCode</c> continua sem depender de ScriptableObject
        /// nem da Unity para ser testado.
        /// </summary>
        public void ApplyLanguageAliases()
        {
            if (LanguageAliases == null || LanguageAliases.Count == 0)
            {
                LanguageCode.SetAliases(null);
                return;
            }

            var pairs = new List<KeyValuePair<string, string>>(LanguageAliases.Count);
            foreach (var alias in LanguageAliases)
            {
                if (alias != null)
                    pairs.Add(new KeyValuePair<string, string>(alias.from, alias.to));
            }

            LanguageCode.SetAliases(pairs);
        }

        public static void RaiseOnRunEditor() => OnRunEditor?.Invoke();

        public void Reset()
        {
            Sources = new List<LocalizationSource>
            {
                new LocalizationSource
                {
                    TableId = Constants.ExampleTableId,
                    Sheets = Constants.ExampleSheets.Select(i => new Sheet { Name = i.Key, Id = i.Value }).ToList()
                }
            };
            DevSources = new List<LocalizationSource>();
#if UNITY_EDITOR
            SaveFolder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(@"Assets/FineLocalization/Resources/Localization");
#endif
        }
    }
}
