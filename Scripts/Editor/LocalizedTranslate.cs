using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FineLocalization.Runtime;
using Newtonsoft.Json.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FineLocalization.Editor
{
    public class LocalizedTranslate : PopupWindowContent
    {
        public static string DestinationLanguage;

        public static string PrimaryLanguage = "English";

        private static bool _emptyOnly;

        private static LocalizationEditor Editor => LocalizationEditor.Instance;

        private const int ButtonWidth = 220;

        public override void OnGUI(Rect rect)
        {
            GUILayout.BeginVertical();

            if (GUILayout.Button("Set primary language", GUILayout.Width(ButtonWidth)))
            {
                PrimaryLanguage = DestinationLanguage;
                GUILayout.EndVertical();
                editorWindow.Close();
            }

            if (GUILayout.Button("Translate cell", GUILayout.Width(ButtonWidth)))
            {
                if (ValidateInput())
                {
                    EditorCoroutineUtility.StartCoroutine(TranslateCell(), this);
                    GUILayout.EndVertical();
                    editorWindow.Close();
                }
            }
            if (GUILayout.Button("Translate row (empty cells only)", GUILayout.Width(ButtonWidth)))
            {
                if (ValidateInput())
                {
                    _emptyOnly = true;
                    EditorCoroutineUtility.StartCoroutine(TranslateKey(), this);
                    GUILayout.EndVertical();
                    editorWindow.Close();
                }
            }
            if (GUILayout.Button("Translate row (override)", GUILayout.Width(ButtonWidth)))
            {
                if (ValidateInput())
                {
                    _emptyOnly = false;
                    EditorCoroutineUtility.StartCoroutine(TranslateKey(), this);
                    GUILayout.EndVertical();
                    editorWindow.Close();
                }
            }
            if (GUILayout.Button("Translate column (empty cells only)", GUILayout.Width(ButtonWidth)))
            {
                if (ValidateInput())
                {
                    _emptyOnly = true;
                    EditorCoroutineUtility.StartCoroutine(TranslateColumn(), this);
                    GUILayout.EndVertical();
                    editorWindow.Close();
                }
            }
            if (GUILayout.Button("Translate column (override)", GUILayout.Width(ButtonWidth)))
            {
                if (ValidateInput())
                {
                    _emptyOnly = false;
                    EditorCoroutineUtility.StartCoroutine(TranslateColumn(), this); 
                    GUILayout.EndVertical();
                    editorWindow.Close();
                }
            }

            GUILayout.EndVertical();
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(225, 130);
        }

        private IEnumerator TranslateCell()
        {
            yield return Translate(new[] { DestinationLanguage }, Editor.CurrentKey, 1, 1, _ => {} );

            EditorUtility.ClearProgressBar();
            editorWindow.Close();
        }

        private IEnumerator TranslateColumn()
        {
            var emptyKeys = Editor.SheetDictionary[DestinationLanguage].Keys.Where(key => string.IsNullOrEmpty(Editor.SheetDictionary[DestinationLanguage][key])).ToArray();
            var translatedKeys = Editor.SheetDictionary[DestinationLanguage].Keys.Where(key => !string.IsNullOrEmpty(Editor.SheetDictionary[DestinationLanguage][key])).ToArray();

            if (!emptyKeys.Any() && _emptyOnly)
            {
                editorWindow.Close();
            }

            var success = true;
            var progressMax = emptyKeys.Length + (_emptyOnly ? 0 : translatedKeys.Length);
            var i = 1;

            foreach(var key in emptyKeys)
            {
                yield return Translate(new[] { DestinationLanguage }, key, i++, progressMax, (result) => { success = result; });
            }

            if (!_emptyOnly && success)
            {
                i = 1;

                foreach (var key in translatedKeys)
                {
                    yield return Translate(new[] { DestinationLanguage }, key,i++, progressMax, (result) => { success = result; });
                }
            }

            EditorUtility.ClearProgressBar();
            editorWindow.Close();
        }

        public IEnumerator TranslateAuto()
        {
            if (ValidateInput(true))
            {
                yield return TranslateKey();
            }
        }

        private IEnumerator TranslateKey()
        {
            var emptyLanguages = Editor.SheetDictionary.Keys.Where(language => string.IsNullOrEmpty(Editor.SheetDictionary[language][Editor.CurrentKey])).ToArray();
            var translatedLanguages = Editor.SheetDictionary.Keys.Where(language => !string.IsNullOrEmpty(Editor.SheetDictionary[language][Editor.CurrentKey])).ToArray();

            if (!emptyLanguages.Any() && _emptyOnly)
            {
                editorWindow.Close();
            }

            var success = true;
            var progressMax = emptyLanguages.Length + (_emptyOnly ? 0 : translatedLanguages.Length);

            yield return Translate(emptyLanguages, Editor.CurrentKey, 1, progressMax, (result) => { success = result; });

            if (!_emptyOnly && success)
            {
                yield return Translate(translatedLanguages, Editor.CurrentKey, 1, progressMax, (result) => { success = result; });
            }

            EditorUtility.ClearProgressBar();

            if (editorWindow) editorWindow.Close();
        }

        private static IEnumerator Translate(string[] languages, string key, int keyProgress, int progressMax, Action<bool> callback)
        {
            var error = "";
            var translatedText = "";

            for (var i = 0; i < languages.Length; i++)
            {
                var progress = (float) (i + keyProgress) / progressMax;

                if (EditorUtility.DisplayCancelableProgressBar("Submitting data...", $"[{(int)(100 * progress)}%] [{i + keyProgress}/{progressMax}] ...", progress))
                {
                    callback(false);

                    yield break;
                }

                if (string.IsNullOrEmpty(PrimaryLanguage))
                {
                    EditorUtility.DisplayDialog("Error", $"Assign the source text of {key} key for {PrimaryLanguage} language please!", "OK");

                    yield break;
                }

                yield return TranslateText(GetLangCode(languages[i]), Editor.SheetDictionary[PrimaryLanguage][key], PrimaryLanguage,
                    (message, translated) =>
                    {
                        translatedText = translated;
                        error = message;
                    });


                if (!string.IsNullOrEmpty(error))
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Error", $"Can't translate data: {error}", "OK");
                    callback(false);

                    yield break;
                }

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(translatedText)) continue;

                Editor.SheetDictionary[languages[i]][key] = translatedText;

                if (!Editor.IsNewKey(key))
                {
                    if (!Editor.KeysActions.ContainsKey(key))
                    {
                        Editor.KeysActions.Add(key, ActionType.Edit);
                    }
                }
            }
        }

        private static IEnumerator TranslateText(string targetLang, string sourceText, string sourceLanguage, Action<string, string> callback)
        {
            var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=" + GetLangCode(sourceLanguage) + "&tl=" + targetLang + "&dt=t&q=" + UnityWebRequest.EscapeURL(sourceText);
            using var request = UnityWebRequest.Get(url);

            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            if (request.error == null)
            {
                var result = string.Join(null, JArray.Parse(request.downloadHandler.text)[0].Select(i => i[0].Value<string>()));
                
                callback(null, result);
            }
            else
            {
                callback(request.error, null);
            }
        }

        private static readonly HashSet<string> UnknownLanguagesLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Código de idioma que o Google Translate entende, a partir do nome da coluna do CSV.
        ///
        /// Aceita as duas convenções: o nome em inglês ("Japanese") e o código ("ja-jp", "zh-CN").
        /// Antes só aceitava o nome, mas todo call site passa o header da coluna — então uma
        /// planilha nomeada por código caía no default "en" e a tradução automática traduzia
        /// <b>toda</b> coluna para inglês.
        /// </summary>
        private static string GetLangCode(string language)
        {
            var fromName = GetLangCodeFromEnglishName(language);
            if (!string.IsNullOrEmpty(fromName))
                return fromName;

            var fromTag = GetLangCodeFromTag(language);
            if (!string.IsNullOrEmpty(fromTag))
                return fromTag;

            if (!string.IsNullOrWhiteSpace(language) && UnknownLanguagesLogged.Add(language))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] Coluna '{language}' não foi reconhecida como idioma. " +
                          "Traduzindo para inglês. Renomeie a coluna para um código (ex: ja-jp) ou " +
                          "para o nome em inglês (ex: Japanese)."
                );
            }

            return "en";
        }

        /// <summary>
        /// Traduz um código BCP-47 no dialeto que a API do Google espera — ela não usa códigos
        /// puros para chinês, hebraico, javanês, filipino e português.
        /// </summary>
        private static string GetLangCodeFromTag(string language)
        {
            var tag = LanguageTag.Parse(LanguageCode.Canonicalize(language));
            if (!tag.HasLanguage)
                return null;

            switch (tag.Language)
            {
                case "zh":
                    return tag.Region == "tw" || tag.Region == "hk" || tag.Region == "mo" ? "zh-TW" : "zh-CN";
                case "pt":
                    return tag.Region == "pt" ? "pt-PT" : "pt-BR";
                case "he": return "iw";
                case "jv": return "jw";
                case "fil": return "tl";
                default: return tag.Language;
            }
        }

        /// <summary>Nome do idioma em inglês, como as planilhas antigas nomeiam as colunas.</summary>
        private static string GetLangCodeFromEnglishName(string language)
        {
            return language switch
            {
                "Afrikaans" => "af",
                "Albanian" => "sq",
                "Amharic" => "am",
                "Arabic" => "ar",
                "Armenian" => "hy",
                "Azerbaijani" => "az",
                "Basque" => "eu",
                "Belarusian" => "be",
                "Bengali" => "bn",
                "Bosnian" => "bs",
                "Bulgarian" => "bg",
                "Catalan" => "ca",
                "Cebuano" => "ceb",
                "Chinese" => "zh",
                "Corsican" => "co",
                "Croatian" => "hr",
                "Czech" => "cs",
                "Danish" => "da",
                "Dutch" => "nl",
                "English" => "en",
                "Esperanto" => "eo",
                "Estonian" => "et",
                "Finnish" => "fi",
                "French" => "fr",
                "Frisian" => "fy",
                "Galician" => "gl",
                "Georgian" => "ka",
                "German" => "de",
                "Greek" => "el",
                "Gujarati" => "gu",
                "Haitian Creole" => "ht",
                "Hausa" => "ha",
                "Hawaiian" => "haw",
                "Hebrew" => "he",
                "Hindi" => "hi",
                "Hmong" => "hmn",
                "Hungarian" => "hu",
                "Icelandic" => "is",
                "Igbo" => "ig",
                "Indonesian" => "id",
                "Irish" => "ga",
                "Italian" => "it",
                "Japanese" => "ja",
                "Javanese" => "jw",
                "Kannada" => "kn",
                "Kazakh" => "kk",
                "Khmer" => "km",
                "Korean" => "ko",
                "Kurdish" => "ku",
                "Kyrgyz" => "ky",
                "Lao" => "lo",
                "Latin" => "la",
                "Latvian" => "lv",
                "Lithuanian" => "lt",
                "Luxembourgish" => "lb",
                "Macedonian" => "mk",
                "Malagasy" => "mg",
                "Malay" => "ms",
                "Malayalam" => "ml",
                "Maltese" => "mt",
                "Maori" => "mi",
                "Marathi" => "mr",
                "Mongolian" => "mn",
                "Myanmar" => "my",
                "Nepali" => "ne",
                "Norwegian" => "no",
                "Nyanja" => "ny",
                "Pashto" => "ps",
                "Persian" => "fa",
                "Polish" => "pl",
                "Portuguese" => "pt",
                "Punjabi" => "pa",
                "Romanian" => "ro",
                "Russian" => "ru",
                "Samoan" => "sm",
                "Scots Gaelic" => "gd",
                "Serbian" => "sr",
                "Sesotho" => "st",
                "Shona" => "sn",
                "Sindhi" => "sd",
                "Sinhala" => "si",
                "Slovak" => "sk",
                "Slovenian" => "sl",
                "Somali" => "so",
                "Spanish" => "es",
                "Sundanese" => "su",
                "Swahili" => "sw",
                "Swedish" => "sv",
                "Tagalog" => "tl",
                "Tajik" => "tg",
                "Tamil" => "ta",
                "Telugu" => "te",
                "Thai" => "th",
                "Turkish" => "tr",
                "Ukrainian" => "uk",
                "Urdu" => "ur",
                "Uzbek" => "uz",
                "Vietnamese" => "vi",
                "Welsh" => "cy",
                "Xhosa" => "xh",
                "Yiddish" => "yi",
                "Yoruba" => "yo",
                "Zulu" => "zu",
                _ => null
            };
        }

        private static bool ValidateInput(bool auto = false)
        {
            if (string.IsNullOrEmpty(PrimaryLanguage))
            {
                EditorUtility.DisplayDialog("Error", "Assign the source language please!", "OK");

                return false;
            }

            if (string.IsNullOrEmpty(Editor.SheetDictionary[PrimaryLanguage][Editor.CurrentKey]))
            {
                EditorUtility.DisplayDialog("Error", $"Assign the source text for {PrimaryLanguage} please!", "OK");

                return false;
            }

            if (!auto && string.IsNullOrEmpty(DestinationLanguage))
            {
                EditorUtility.DisplayDialog("Error", "Assign the destination language please!", "OK");

                return false;
            }

            return true;
        }
    }
}
