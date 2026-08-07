using System;
using System.Collections.Generic;
using UnityEngine;

namespace FineLocalization.Runtime
{
    /// <summary>
    /// Regras de código de idioma usadas por todo o pacote: normalização, raiz, detecção de
    /// script Latin e pontuação de compatibilidade entre um idioma e um prefixo configurado.
    ///
    /// Na v2 essa lógica estava duplicada entre RuntimeLocaleDownloader e RemoteFontBundleLoader
    /// (duas tabelas Latin idênticas que precisavam ser editadas em par). Aqui existe uma só.
    /// </summary>
    public static class LanguageCode
    {
        private const char Bom = (char)0xFEFF;

        /// <summary>Prefixos cujo script é Latin — não precisam de fonte remota.</summary>
        private static readonly HashSet<string> LatinPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "en", "es", "pt", "fr", "de", "it", "nl", "ca", "gl", "eu", "oc", "rm",
            "sv", "no", "nb", "nn", "da", "fi", "is", "fo",
            "pl", "cs", "sk", "ro", "hu", "sl", "hr", "bs", "sq", "lt", "lv", "et",
            "tr", "az", "uz", "tk", "kk",
            "id", "ba", "ms", "vi", "tl", "fil",   // "ba" cobre o código custom "ba-id" (Bahasa Indonesia); "id" cobre id/id-id
            "ga", "cy", "gd", "br", "kw",
            "sw", "af", "zu", "xh", "yo", "ig", "ha", "so", "rw", "mg", "st", "sn", "ny",
            "lb", "fy", "mt", "ku", "ht", "qu", "gn"
        };

        private static readonly string[] UrlQueryKeys = { "lang", "language", "locale", "culture", "lng" };

        /// <summary>
        /// Forma canônica de um código: minúsculo, sem espaços, sem BOM, com '-' como separador.
        /// Retorna string vazia para entrada nula/vazia — nunca null.
        /// </summary>
        public static string Normalize(string language)
        {
            return string.IsNullOrWhiteSpace(language)
                ? string.Empty
                : language.Trim().Trim(Bom).Replace('_', '-').ToLowerInvariant();
        }

        /// <summary>Parte antes do primeiro '-'. "pt-br" → "pt".</summary>
        public static string Root(string language)
        {
            var normalized = Normalize(language);
            var dash = normalized.IndexOf('-');
            return dash >= 0 ? normalized.Substring(0, dash) : normalized;
        }

        /// <summary>
        /// True quando o idioma é escrito em script Latin e, portanto, já é coberto pelas fontes
        /// embutidas no projeto. <paramref name="forceRemote"/> tem prioridade sobre tudo — use
        /// para fontes base minimalistas que não têm acentuação completa.
        /// </summary>
        public static bool IsLatinScript(string language, IList<string> extraLatin = null, IList<string> forceRemote = null)
        {
            var normalized = Normalize(language);
            if (string.IsNullOrEmpty(normalized))
                return false;

            var root = Root(normalized);

            if (ListContains(forceRemote, root, normalized))
                return false;

            return LatinPrefixes.Contains(root) || ListContains(extraLatin, root, normalized);
        }

        /// <summary>
        /// Quão bem <paramref name="language"/> casa com <paramref name="prefix"/>.
        /// Maior = melhor. -1 quando não casam. Usado para escolher o bundle mais específico
        /// quando há várias configurações candidatas (ex: "zh" e "zh-tw" para "zh-tw").
        /// </summary>
        public static int MatchScore(string language, string prefix)
        {
            language = Normalize(language);
            prefix = Normalize(prefix);

            if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(prefix))
                return -1;

            if (language == prefix)
                return 300 + prefix.Length;

            if (language.StartsWith(prefix + "-", StringComparison.Ordinal))
                return 200 + prefix.Length;

            if (prefix.StartsWith(language + "-", StringComparison.Ordinal))
                return 100 + language.Length;

            var languageRoot = Root(language);
            return languageRoot == Root(prefix) ? 50 + languageRoot.Length : -1;
        }

        /// <summary>True quando os dois códigos compartilham raiz (ou um contém o outro).</summary>
        public static bool IsSameOrRoot(string a, string b) => MatchScore(a, b) >= 0;

        /// <summary>
        /// Lê o idioma da query string da URL de lançamento (WebGL). Aceita ?lang=, ?language=,
        /// ?locale=, ?culture= e ?lng=. Retorna string vazia quando não há.
        /// </summary>
        public static string FromLaunchUrl()
        {
            var url = Application.absoluteURL;
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            var queryStart = url.IndexOf('?');
            if (queryStart < 0)
                return string.Empty;

            var queryEnd = url.IndexOf('#', queryStart + 1);
            var query = queryEnd >= 0
                ? url.Substring(queryStart + 1, queryEnd - queryStart - 1)
                : url.Substring(queryStart + 1);

            foreach (var pair in query.Split('&'))
            {
                var equals = pair.IndexOf('=');
                if (equals <= 0)
                    continue;

                var key = DecodeQueryPart(pair.Substring(0, equals));
                if (Array.IndexOf(UrlQueryKeys, key.ToLowerInvariant()) < 0)
                    continue;

                var value = Normalize(DecodeQueryPart(pair.Substring(equals + 1)));
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return string.Empty;
        }

        /// <summary>
        /// Idioma do sistema operacional / navegador como código BCP-47 aproximado.
        /// Retorna string vazia quando a Unity não consegue determinar.
        /// </summary>
        public static string FromSystemLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Portuguese: return "pt-br";
                case SystemLanguage.English: return "en-us";
                case SystemLanguage.Spanish: return "es-es";
                case SystemLanguage.French: return "fr-fr";
                case SystemLanguage.German: return "de-de";
                case SystemLanguage.Italian: return "it-it";
                case SystemLanguage.Dutch: return "nl-nl";
                case SystemLanguage.Russian: return "ru-ru";
                case SystemLanguage.Turkish: return "tr-tr";
                case SystemLanguage.Polish: return "pl-pl";
                case SystemLanguage.Japanese: return "ja-jp";
                case SystemLanguage.Korean: return "ko-kr";
                case SystemLanguage.Thai: return "th-th";
                case SystemLanguage.Vietnamese: return "vi-vn";
                case SystemLanguage.Indonesian: return "id-id";
                case SystemLanguage.Arabic: return "ar-sa";
                case SystemLanguage.Hebrew: return "he-il";
                case SystemLanguage.Hindi: return "hi-in";
                case SystemLanguage.ChineseSimplified: return "zh-cn";
                case SystemLanguage.ChineseTraditional: return "zh-tw";
                case SystemLanguage.Chinese: return "zh-cn";
                case SystemLanguage.Unknown: return string.Empty;
                default: return string.Empty;
            }
        }

        private static bool ListContains(IList<string> list, string root, string normalized)
        {
            if (list == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                var item = Normalize(list[i]);
                if (item.Length > 0 && (item == root || item == normalized))
                    return true;
            }

            return false;
        }

        private static string DecodeQueryPart(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : Uri.UnescapeDataString(value.Replace("+", " "));
        }
    }
}
