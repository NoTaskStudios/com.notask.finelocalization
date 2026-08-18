using System;
using System.Collections.Generic;

namespace FineLocalization.Runtime
{
    /// <summary>
    /// Subtags de um código BCP-47, classificadas por <b>forma</b> e não por semântica:
    /// <c>Parse("cn")</c> devolve <c>Language = "cn"</c> porque "cn" tem a forma de um idioma,
    /// mesmo sendo uma região. Reinterpretar isso é trabalho da escada de candidatos em
    /// <see cref="LanguageCode"/> — aqui só existe gramática.
    ///
    /// Nunca lança e nunca devolve campo null. Entrada que não parseia devolve <see cref="Empty"/>,
    /// e nesse caso a resolução cai no candidato cru, que é o comportamento pré-v3.1.
    /// </summary>
    public readonly struct LanguageTag
    {
        public static readonly LanguageTag Empty = new LanguageTag(string.Empty, string.Empty, string.Empty);

        private LanguageTag(string language, string script, string region)
        {
            Language = language ?? string.Empty;
            Script = script ?? string.Empty;
            Region = region ?? string.Empty;
        }

        /// <summary>2-3 alpha ASCII minúsculo, ou vazio. Ex: "zh", "yue".</summary>
        public string Language { get; }

        /// <summary>4 alpha ASCII minúsculo, ou vazio. Ex: "hans", "latn".</summary>
        public string Script { get; }

        /// <summary>2 alpha ou 3 dígitos, minúsculo, ou vazio. Ex: "cn", "419".</summary>
        public string Region { get; }

        public bool HasLanguage => Language.Length > 0;

        public bool IsEmpty => Language.Length == 0 && Script.Length == 0 && Region.Length == 0;

        /// <summary>
        /// Classifica os subtags de <paramref name="raw"/>. Tolera sufixos POSIX/Java que não
        /// são BCP-47 ("en_US.UTF-8", "zh-CN@pinyin", "zh_CN_#Hans") e para de consumir num
        /// singleton ("-u-", "-x-"), descartando extensões e variantes.
        /// </summary>
        public static LanguageTag Parse(string raw)
        {
            var body = StripNonBcp47Suffix(LanguageCode.Normalize(raw));
            if (body.Length == 0)
                return Empty;

            var tokens = body.Split('-');
            var first = tokens[0];
            if (LanguageTables.IsUnspecified(first))
                return Empty;

            string language = string.Empty, script = string.Empty, region = string.Empty;

            if (IsAlpha(first, 2, 3)) language = first;
            else if (IsAlpha(first, 4, 4)) script = first;
            else if (IsDigits(first, 3)) region = first;
            else return Empty;

            for (int i = 1; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (token.Length <= 1)
                    break;                                  // singleton: começou uma extensão

                if (script.Length == 0 && IsAlpha(token, 4, 4)) { script = token; continue; }
                if (region.Length == 0 && (IsAlpha(token, 2, 2) || IsDigits(token, 3))) { region = token; continue; }

                // Variante ("fonipa", "min", "nan"): não muda coluna nem fonte.
            }

            return new LanguageTag(language, script, region);
        }

        /// <summary>Forma completa: "zh-hans-cn", "zh-cn", "zh" ou "".</summary>
        public string ToCode() => Join(Language, Script, Region);

        /// <summary>"zh-hans" — ou só o idioma quando não há script.</summary>
        public string LanguageAndScript() => Join(Language, Script, string.Empty);

        /// <summary>"zh-cn" — ou só o idioma quando não há região.</summary>
        public string LanguageAndRegion() => Join(Language, string.Empty, Region);

        public override string ToString() => ToCode();

        /// <summary>
        /// Corta o que vem depois de '.', '@' ou "-#" — respectivamente charset POSIX,
        /// modificador POSIX e o script legado de <c>java.util.Locale.toString()</c>.
        /// </summary>
        private static string StripNonBcp47Suffix(string normalized)
        {
            for (int i = 0; i < normalized.Length; i++)
            {
                var c = normalized[i];
                if (c == '.' || c == '@')
                    return normalized.Substring(0, i);
                if (c == '#' && i > 0 && normalized[i - 1] == '-')
                    return normalized.Substring(0, i - 1);
            }

            return normalized;
        }

        private static string Join(string language, string script, string region)
        {
            var parts = new List<string>(3);
            if (language.Length > 0) parts.Add(language);
            if (script.Length > 0) parts.Add(script);
            if (region.Length > 0) parts.Add(region);
            return string.Join("-", parts);
        }

        // Ranges ASCII explícitos, não char.IsLetter/IsDigit: um BOM no meio da string sobrevive
        // ao Trim() do Normalize e seria classificado como subtag válida por char.IsLetter.
        private static bool IsAlpha(string value, int min, int max)
        {
            if (value.Length < min || value.Length > max)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c < 'a' || c > 'z')
                    return false;
            }

            return true;
        }

        private static bool IsDigits(string value, int length)
        {
            if (value.Length != length)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Tabelas de referência usadas pela canonicalização. São dados padronizados (ISO 639,
    /// ISO 3166, CLDR likely-subtags), não regras específicas de um host — por isso vivem aqui
    /// e não num switch espalhado pelo pacote.
    /// </summary>
    internal static class LanguageTables
    {
        /// <summary>Códigos que significam "idioma não especificado". Tratados como entrada vazia.</summary>
        private static readonly HashSet<string> Unspecified = new(StringComparer.Ordinal)
        {
            "und", "mul", "zxx", "root", "c", "posix", "*"
        };

        /// <summary>ISO 639-2/T e 639-2/B (bibliográfico) → ISO 639-1.</summary>
        internal static readonly Dictionary<string, string> Iso3To2 = ParseMap(
            "zho:zh chi:zh cmn:zh jpn:ja kor:ko tha:th vie:vi ind:id msa:ms may:ms eng:en spa:es " +
            "por:pt fra:fr fre:fr deu:de ger:de ita:it nld:nl dut:nl rus:ru pol:pl ces:cs cze:cs " +
            "slk:sk slo:sk hun:hu ron:ro rum:ro bul:bg hrv:hr srp:sr slv:sl ell:el gre:el tur:tr " +
            "ukr:uk swe:sv nor:nb dan:da fin:fi isl:is ice:is est:et lav:lv lit:lt ara:ar heb:he " +
            "fas:fa per:fa urd:ur hin:hi ben:bn tam:ta tel:te mar:mr guj:gu kan:kn mal:ml pan:pa " +
            "sin:si nep:ne mya:my bur:my khm:km lao:lo kat:ka geo:ka hye:hy arm:hy aze:az kaz:kk " +
            "uzb:uz kir:ky tgl:tl swa:sw afr:af cat:ca eus:eu baq:eu glg:gl cym:cy wel:cy gle:ga " +
            "mlt:mt sqi:sq alb:sq mkd:mk mac:mk bel:be bos:bs hat:ht");

        /// <summary>
        /// ISO 3166-1 alpha-3 → locale. Só consultada <b>depois</b> de <see cref="Iso3To2"/> falhar,
        /// então "jpn"/"kor"/"rus" continuam sendo lidos como idioma. Cobre hosts que mandam
        /// "esp"/"bra"/"chn" no campo de idioma.
        /// </summary>
        internal static readonly Dictionary<string, string> Alpha3Region = ParseMap(
            "chn:zh-cn twn:zh-tw hkg:zh-hk esp:es-es bra:pt-br prt:pt-pt mex:es-mx arg:es-ar " +
            "usa:en-us gbr:en-gb vnm:vi-vn idn:id-id");

        /// <summary>
        /// Regiões de idioma único: ISO 3166-1 alpha-2 → ISO 639-1. É o que conserta o "cn" da
        /// Playtech. Regiões genuinamente multilíngues (ca, ch, be, za, sg, my, ph, ng, ke) estão
        /// <b>deliberadamente ausentes</b> — chutar ali faz mais mal que cair no fallback.
        /// </summary>
        internal static readonly Dictionary<string, string> RegionSoleLanguage = ParseMap(
            "cn:zh tw:zh hk:zh mo:zh jp:ja kr:ko kp:ko th:th vn:vi kh:km la:lo mm:my gr:el cz:cs " +
            "sk:sk pl:pl hu:hu dk:da se:sv no:nb fi:fi is:is ee:et lv:lv lt:lt ua:uk ru:ru by:be " +
            "bg:bg ro:ro rs:sr hr:hr si:sl ba:bs mk:mk al:sq tr:tr il:he ir:fa sa:ar ae:ar eg:ar " +
            "in:hi pk:ur bd:bn lk:si np:ne de:de at:de fr:fr it:it es:es pt:pt nl:nl br:pt mx:es " +
            "ar:es cl:es co:es pe:es us:en gb:en au:en nz:en ie:en uk:en");

        /// <summary>
        /// Região padrão de cada idioma. É o desempate determinístico quando o host manda um
        /// código genérico ("zh", "pt") e a planilha tem várias regiões — antes ganhava a
        /// primeira coluna, o que fazia reordenar o Google Sheet mudar o idioma do jogador.
        /// A maior parte das entradas coincide com o que <see cref="LanguageCode.FromSystemLanguage"/>
        /// já assumia, então a tabela codifica o viés existente do pacote, não uma opinião nova.
        /// </summary>
        internal static readonly Dictionary<string, string> PreferredRegion = ParseMap(
            "en:us pt:br es:es zh:cn fr:fr de:de it:it nl:nl ru:ru tr:tr pl:pl ja:jp ko:kr th:th " +
            "vi:vn id:id ar:sa he:il hi:in uk:ua cs:cz el:gr da:dk sv:se nb:no fi:fi is:is et:ee " +
            "lv:lv lt:lt bg:bg hr:hr sr:rs sl:si bs:ba sq:al ms:my fa:ir ur:pk bn:bd si:lk ne:np " +
            "my:mm km:kh lo:la ka:ge hy:am tl:ph");

        /// <summary>Código depreciado → código atual. Aplicado antes de qualquer outra regra.</summary>
        internal static readonly Dictionary<string, string> CanonicalLanguage = ParseMap(
            "iw:he in:id ji:yi mo:ro no:nb fil:tl jw:jv sh:sr");

        /// <summary>
        /// Códigos equivalentes a tentar depois do original — bidirecional de propósito: a
        /// planilha pode ter a coluna nomeada com o código antigo ("iw") e o host mandar o novo.
        /// </summary>
        internal static readonly Dictionary<string, string[]> LanguageSiblings = new(StringComparer.Ordinal)
        {
            { "he", new[] { "iw" } },   { "iw", new[] { "he" } },
            { "id", new[] { "in" } },   { "in", new[] { "id" } },
            { "yi", new[] { "ji" } },   { "ji", new[] { "yi" } },
            { "ro", new[] { "mo" } },   { "mo", new[] { "ro" } },
            { "nb", new[] { "no" } },   { "no", new[] { "nb" } },
            { "tl", new[] { "fil" } },  { "fil", new[] { "tl" } },
            { "jv", new[] { "jw" } },   { "jw", new[] { "jv" } },
            { "sr", new[] { "sh" } },   { "sh", new[] { "sr" } },
            { "nn", new[] { "no", "nb" } }
        };

        /// <summary>
        /// Idiomas que não têm coluna própria em planilha nenhuma e precisam cair num locale
        /// completo. "yue" (cantonês) é o caso real: operadores asiáticos mandam isso.
        /// </summary>
        internal static readonly Dictionary<string, string[]> FullCodeChain = new(StringComparer.Ordinal)
        {
            { "yue", new[] { "zh-hk", "zh-hant", "zh-tw", "zh" } },
            { "nan", new[] { "zh-tw", "zh" } },
            { "gsw", new[] { "de-ch", "de" } },
            { "prs", new[] { "fa-af", "fa" } },
            { "pes", new[] { "fa" } },
            { "arb", new[] { "ar" } }
        };

        /// <summary>
        /// "idioma-script" → regiões que usam aquele script, na ordem de preferência. É o que faz
        /// "zh-hk" cair em "zh-tw" (ambos Tradicional) em vez de "zh-cn" (Simplificado).
        /// </summary>
        internal static readonly Dictionary<string, string[]> ScriptRegions = new(StringComparer.Ordinal)
        {
            { "zh-hans", new[] { "cn", "sg", "my" } },
            { "zh-hant", new[] { "tw", "hk", "mo" } },
            { "sr-cyrl", new[] { "rs", "me", "ba" } },
            { "sr-latn", new string[0] },
            { "pa-guru", new[] { "in" } },
            { "pa-arab", new[] { "pk" } },
            { "mn-cyrl", new[] { "mn" } },
            { "mn-mong", new[] { "cn" } },
            { "az-latn", new[] { "az" } },
            { "uz-latn", new[] { "uz" } },
            { "bs-latn", new[] { "ba" } },
            { "ku-latn", new[] { "tr" } }
        };

        /// <summary>Script assumido quando o código não traz um explícito.</summary>
        internal static readonly Dictionary<string, string> DefaultScript = ParseMap(
            "zh:hans sr:cyrl pa:guru mn:cyrl az:latn uz:latn bs:latn ku:latn");

        /// <summary>Inverso de <see cref="ScriptRegions"/>: "idioma-região" → script.</summary>
        internal static readonly Dictionary<string, string> RegionScript = BuildRegionScript();

        /// <summary>Idioma → todos os scripts conhecidos, na ordem em que foram declarados.</summary>
        internal static readonly Dictionary<string, List<string>> LanguageScripts = BuildLanguageScripts();

        internal static bool IsUnspecified(string token) => token != null && Unspecified.Contains(token);

        internal static string Lookup(Dictionary<string, string> table, string key)
        {
            return !string.IsNullOrEmpty(key) && table.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static Dictionary<string, string> ParseMap(string spec)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var pairs = spec.Split(' ');

            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                if (pair.Length == 0)
                    continue;

                var colon = pair.IndexOf(':');
                if (colon <= 0 || colon == pair.Length - 1)
                    continue;

                map[pair.Substring(0, colon)] = pair.Substring(colon + 1);
            }

            return map;
        }

        private static Dictionary<string, string> BuildRegionScript()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var entry in ScriptRegions)
            {
                var dash = entry.Key.IndexOf('-');
                if (dash <= 0)
                    continue;

                var language = entry.Key.Substring(0, dash);
                var script = entry.Key.Substring(dash + 1);

                for (int i = 0; i < entry.Value.Length; i++)
                {
                    var key = language + "-" + entry.Value[i];
                    if (!map.ContainsKey(key))
                        map[key] = script;
                }
            }

            return map;
        }

        private static Dictionary<string, List<string>> BuildLanguageScripts()
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var entry in ScriptRegions)
            {
                var dash = entry.Key.IndexOf('-');
                if (dash <= 0)
                    continue;

                var language = entry.Key.Substring(0, dash);
                var script = entry.Key.Substring(dash + 1);

                if (!map.TryGetValue(language, out var scripts))
                    map[language] = scripts = new List<string>(2);

                if (!scripts.Contains(script))
                    scripts.Add(script);
            }

            return map;
        }
    }
}
