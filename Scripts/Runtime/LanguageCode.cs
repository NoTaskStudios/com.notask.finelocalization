using System;
using System.Collections.Generic;
using UnityEngine;

namespace FineLocalization.Runtime
{
    /// <summary>
    /// Regras de código de idioma usadas por todo o pacote: normalização, raiz, detecção de
    /// script Latin, canonicalização BCP-47 e escolha do melhor código disponível.
    ///
    /// Na v2 essa lógica estava duplicada entre RuntimeLocaleDownloader e RemoteFontBundleLoader
    /// (duas tabelas Latin idênticas que precisavam ser editadas em par). Aqui existe uma só.
    ///
    /// <b>Regra de ouro para escolher o método:</b>
    /// <list type="bullet">
    /// <item><see cref="Normalize"/> nos caminhos que <i>autoram</i> dado — header de CSV, nome de
    /// bundle, nome de arquivo. Nunca muda o formato, só arruma caixa e separador.</item>
    /// <item><see cref="Candidates"/> / <see cref="SelectBest"/> nos caminhos de <i>pedido → dado</i>,
    /// onde um host pode mandar qualquer coisa ("cn" no lugar de "zh") e o que existe de verdade
    /// (colunas da planilha, bundles configurados) é quem decide.</item>
    /// </list>
    ///
    /// A escada de candidatos sempre começa pela entrada crua normalizada, então qualquer código
    /// que já resolvia antes da v3.1 continua resolvendo exatamente igual.
    /// </summary>
    public static class LanguageCode
    {
        private const char Bom = (char)0xFEFF;

        /// <summary>Teto de candidatos por pedido. Além disso é ruído: nenhuma planilha real tem tantas variantes do mesmo idioma.</summary>
        private const int MaxCandidates = 16;

        private const int CacheCapacity = 64;

        private static readonly string[] EmptyStrings = new string[0];

        private static readonly object CacheLock = new object();

        /// <summary>Escada de candidatos por entrada normalizada. Função pura da entrada + <see cref="Aliases"/>.</summary>
        private static readonly Dictionary<string, string[]> CandidateCache = new(StringComparer.Ordinal);

        /// <summary>Overrides do operador, vindos de <c>LocalizationSettings.languageAliases</c>.</summary>
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);

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
        /// Zera o cache e os aliases entre execuções. Obrigatório com <i>Enter Play Mode Options</i>
        /// sem domain reload: sem isso os aliases da rodada anterior sobrevivem.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            lock (CacheLock)
            {
                CandidateCache.Clear();
                Aliases.Clear();
            }
        }

        // ------------------------------------------------------------------ Forma

        /// <summary>
        /// Forma canônica de um código: minúsculo, sem espaços, sem BOM, com '-' como separador.
        /// Retorna string vazia para entrada nula/vazia — nunca null.
        ///
        /// Não reescreve subtags: "cn" continua "cn". Use <see cref="Canonicalize"/> para isso.
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
        /// Tira o prefixo "font_" de um nome de bundle e normaliza o resto.
        /// A ordem importa: "font_" tem que sair <b>antes</b> do '_' → '-' do
        /// <see cref="Normalize"/>, senão "font_zh" viraria "font-zh".
        /// </summary>
        public static string FromBundleName(string bundleName)
        {
            if (string.IsNullOrWhiteSpace(bundleName))
                return string.Empty;

            var name = bundleName.Trim();
            if (name.StartsWith("font_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(5);

            return Normalize(name);
        }

        // ------------------------------------------------------- Canonicalização

        /// <summary>
        /// Forma padronizada de um código, sem olhar para dado nenhum. Idempotente.
        ///
        /// Conserta host que manda região no lugar de idioma ("cn" → "zh-cn", "us" → "en-us"),
        /// código depreciado ("iw" → "he-il"), três letras ("por" → "pt-br", "chn" → "zh-cn") e
        /// script sem região ("zh-hant" → "zh-tw"). Devolve string vazia para entrada vazia ou
        /// para os códigos que significam "não especificado" ("und", "mul", "zxx").
        ///
        /// Em ambiguidade genuína entre idioma e região (`ar`, `is`, `si`, `my`), a leitura como
        /// <b>idioma</b> vence. Códigos que não são idioma reconhecido pelo pacote (`cn`, `jp`,
        /// `br`, `se`) são lidos como região. Para forçar outra leitura, use o campo
        /// <c>Language Aliases</c> do LocalizationSettings.
        /// </summary>
        public static string Canonicalize(string language)
        {
            var normalized = Normalize(language);
            if (normalized.Length == 0)
                return string.Empty;

            var tag = LanguageTag.Parse(normalized);
            if (tag.IsEmpty)
                return string.Empty;

            var lang = tag.Language;
            var script = tag.Script;
            var region = tag.Region;

            if (lang.Length == 0)
                return tag.ToCode();

            if (lang.Length == 3)
            {
                var twoLetter = LanguageTables.Lookup(LanguageTables.Iso3To2, lang);
                if (twoLetter.Length > 0)
                {
                    lang = twoLetter;
                }
                else
                {
                    // Não é idioma de três letras conhecido: pode ser ISO 3166-1 alpha-3 ("esp").
                    var expanded = LanguageTables.Lookup(LanguageTables.Alpha3Region, lang);
                    if (expanded.Length > 0)
                    {
                        var parsed = LanguageTag.Parse(expanded);
                        lang = parsed.Language;
                        if (region.Length == 0)
                            region = parsed.Region;
                    }
                }
            }

            var canonicalLanguage = LanguageTables.Lookup(LanguageTables.CanonicalLanguage, lang);
            if (canonicalLanguage.Length > 0)
                lang = canonicalLanguage;

            // "cn"/"jp"/"us" chegaram no campo de idioma. Só reinterpreta quando o código não é
            // um idioma que o pacote conhece — PreferredRegion é essa lista.
            if (script.Length == 0 && region.Length == 0 && !LanguageTables.PreferredRegion.ContainsKey(lang))
            {
                var fromRegion = LanguageTables.Lookup(LanguageTables.RegionSoleLanguage, lang);
                if (fromRegion.Length > 0 && fromRegion != lang)
                {
                    region = lang;
                    lang = fromRegion;
                }
            }

            if (region.Length == 0)
            {
                if (script.Length > 0)
                {
                    var scriptRegions = ScriptRegionsOf(lang, script);
                    if (scriptRegions.Length > 0)
                        region = scriptRegions[0];
                }
                else
                {
                    region = LanguageTables.Lookup(LanguageTables.PreferredRegion, lang);
                }
            }

            // O script só sobrevive quando a região não o implica: "zh-hans-cn" é "zh-cn", mas
            // "sr-latn-rs" precisa do "latn" porque "sr-rs" seria cirílico.
            if (script.Length > 0 && region.Length > 0 &&
                string.Equals(LanguageTables.Lookup(LanguageTables.RegionScript, lang + "-" + region),
                    script, StringComparison.Ordinal))
            {
                script = string.Empty;
            }

            var result = lang;
            if (script.Length > 0) result += "-" + script;
            if (region.Length > 0) result += "-" + region;
            return result;
        }

        /// <summary>
        /// Códigos a tentar para atender <paramref name="language"/>, do mais confiável ao menos.
        /// O índice 0 é <b>sempre</b> <see cref="Normalize"/> da entrada — é isso que garante que
        /// nada que já funcionava mude de resultado.
        ///
        /// Só quem tem os dados (colunas da planilha, bundles configurados) sabe qual candidato
        /// vale; use <see cref="SelectBest"/> em vez de consumir esta lista à mão.
        /// </summary>
        public static IReadOnlyList<string> Candidates(string language)
        {
            var key = Normalize(language);

            lock (CacheLock)
            {
                if (CandidateCache.TryGetValue(key, out var cached))
                    return cached;
            }

            var buffer = new List<string>(MaxCandidates);
            BuildCandidates(key, buffer);
            var candidates = buffer.ToArray();

            lock (CacheLock)
            {
                if (CandidateCache.Count >= CacheCapacity)
                    CandidateCache.Clear();

                CandidateCache[key] = candidates;
            }

            return candidates;
        }

        /// <summary>
        /// Versão de <see cref="Candidates"/> que escreve num buffer reaproveitado. Devolve
        /// quantos candidatos foram escritos.
        /// </summary>
        public static int GetCandidates(string language, List<string> buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            buffer.Clear();
            var candidates = Candidates(language);
            for (int i = 0; i < candidates.Count; i++)
                buffer.Add(candidates[i]);

            return buffer.Count;
        }

        /// <summary>
        /// Overrides do operador, aplicados antes de qualquer regra interna — mas ainda incapazes
        /// de sombrear um match exato de dado. Serve para as ambiguidades que nenhuma tabela
        /// resolve: "uk" (ucraniano ou Reino Unido?), "ca", "ch", "be", "sg", "my".
        /// Empurrado pelo <see cref="LocalizationSettings"/>; nunca puxado, para esta classe
        /// continuar sem dependências.
        /// </summary>
        public static void SetAliases(IEnumerable<KeyValuePair<string, string>> aliases)
        {
            lock (CacheLock)
            {
                Aliases.Clear();

                if (aliases != null)
                {
                    foreach (var alias in aliases)
                    {
                        var from = Normalize(alias.Key);
                        var to = Normalize(alias.Value);
                        if (from.Length > 0 && to.Length > 0 && from != to)
                            Aliases[from] = to;
                    }
                }

                CandidateCache.Clear();
            }
        }

        // ------------------------------------------------------------- Resolução

        /// <summary>
        /// Melhor entrada de <paramref name="available"/> para atender <paramref name="requested"/>,
        /// ou null quando nada serve. Devolve o elemento <b>verbatim</b> — nunca um código
        /// sintetizado, porque quem chama usa o valor para montar caminho de bundle e URL.
        /// </summary>
        public static string SelectBest(string requested, IList<string> available)
        {
            if (available == null || available.Count == 0)
                return null;

            var candidates = Candidates(requested);

            // Passo 1: match exato, candidatos em ordem de confiança.
            for (int c = 0; c < candidates.Count; c++)
            {
                var candidate = candidates[c];
                if (candidate.Length == 0)
                    continue;

                for (int a = 0; a < available.Count; a++)
                {
                    if (string.Equals(Normalize(available[a]), candidate, StringComparison.Ordinal))
                        return available[a];
                }
            }

            // Passo 2: match por raiz. Desempate determinístico — antes da v3.1 ganhava a
            // primeira chave enumerada, então reordenar colunas do Sheet mudava o resultado.
            for (int c = 0; c < candidates.Count; c++)
            {
                var candidate = candidates[c];
                if (candidate.Length == 0)
                    continue;

                string best = null;
                var bestScore = -1;
                var bestPreferred = false;

                for (int a = 0; a < available.Count; a++)
                {
                    var entry = available[a];
                    var score = MatchScore(candidate, entry);
                    if (score < 0)
                        continue;

                    var preferred = IsPreferredRegion(entry);
                    if (!Beats(entry, score, preferred, best, bestScore, bestPreferred))
                        continue;

                    best = entry;
                    bestScore = score;
                    bestPreferred = preferred;
                }

                if (best != null)
                    return best;
            }

            return null;
        }

        /// <inheritdoc cref="SelectBest(string,IList{string})"/>
        public static string SelectBest(string requested, IEnumerable<string> available)
        {
            if (available == null)
                return null;

            if (available is IList<string> list)
                return SelectBest(requested, list);

            var buffer = new List<string>();
            foreach (var entry in available)
                buffer.Add(entry);

            return SelectBest(requested, buffer);
        }

        /// <summary>
        /// Overload para o dicionário do <see cref="LocalizationManager"/>. As chaves já saem
        /// normalizadas da leitura do CSV, então o passo exato é hash puro, sem alocar.
        /// </summary>
        public static string SelectBest(string requested, Dictionary<string, Dictionary<string, string>> dictionary)
        {
            if (dictionary == null || dictionary.Count == 0)
                return null;

            var candidates = Candidates(requested);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate.Length > 0 && dictionary.ContainsKey(candidate))
                    return candidate;
            }

            var buffer = new List<string>(dictionary.Count);
            foreach (var key in dictionary.Keys)
                buffer.Add(key);

            return SelectBest(requested, buffer);
        }

        /// <summary>
        /// True quando <paramref name="applied"/> é uma resposta legítima para
        /// <paramref name="requested"/> — ou seja, está na escada de candidatos dele. Use no lugar
        /// de <see cref="IsSameOrRoot"/> para decidir se vale avisar o desenvolvedor: "cn" atendido
        /// por "zh-cn" é sucesso, não divergência.
        /// </summary>
        public static bool IsAcceptableFor(string requested, string applied)
        {
            var normalizedApplied = Normalize(applied);
            if (normalizedApplied.Length == 0)
                return false;

            var candidates = Candidates(requested);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Length > 0 && MatchScore(candidates[i], normalizedApplied) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Rastro de uma linha da resolução, para log. Transforma "o idioma saiu errado em
        /// produção" em algo diagnosticável por screenshot.
        /// </summary>
        public static string Explain(string requested, IEnumerable<string> available)
        {
            var candidates = Candidates(requested);
            var options = new List<string>();
            if (available != null)
            {
                foreach (var entry in available)
                    options.Add(entry);
            }

            var chosen = SelectBest(requested, options);
            var outcome = chosen == null ? "nenhum candidato compatível" : $"escolheu '{chosen}'";

            return $"'{Normalize(requested)}' → [{string.Join(", ", candidates)}]; " +
                   $"{outcome} contra [{string.Join(", ", options)}]";
        }

        // ------------------------------------------------------------- Comparação

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

            // Script explícito vence qualquer suposição por prefixo: "sr-latn" é Latin mesmo com
            // "sr" fora da tabela, e "ku-arab" não é Latin mesmo com "ku" dentro dela.
            var script = LanguageTag.Parse(normalized).Script;
            if (script.Length > 0)
                return script == "latn";

            if (LatinPrefixes.Contains(root) || ListContains(extraLatin, root, normalized))
                return true;

            // Região no lugar do idioma ("us", "gb", "br"): reinterpreta antes de exigir uma
            // fonte remota que ninguém configurou e derrubar a carga inteira para o fallback.
            var canonicalRoot = Root(Canonicalize(normalized));
            return canonicalRoot.Length > 0 && canonicalRoot != root && LatinPrefixes.Contains(canonicalRoot);
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
        /// True quando os códigos são iguais, ou um estende o outro com um subtag ("zh" e
        /// "zh-cn"). Ao contrário de <see cref="IsSameOrRoot"/>, <b>não</b> casa por raiz: "zh-cn"
        /// e "zh-tw" são diferentes.
        ///
        /// Use no pipeline de fontes. Casar por raiz ali junta Simplificado com Tradicional — o
        /// characters_zh-tw.txt entraria no bundle de zh-cn, e um sync de mapeamento sobrescreve
        /// a fonte de um idioma com a do outro.
        /// </summary>
        public static bool IsExactOrSubtag(string a, string b)
        {
            a = Normalize(a);
            b = Normalize(b);

            if (a.Length == 0 || b.Length == 0)
                return false;

            return a == b
                   || a.StartsWith(b + "-", StringComparison.Ordinal)
                   || b.StartsWith(a + "-", StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------ Fontes externas

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

        // ------------------------------------------------------------- Diagnóstico

        /// <summary>
        /// Checagens de integridade das tabelas de referência. Lista vazia = tudo consistente.
        /// Chamada pelo self check do Editor; as tabelas são dados escritos à mão e um typo
        /// silencioso ("zh:cnn") viraria um idioma inalcançável em produção.
        /// </summary>
        public static IReadOnlyList<string> ValidateTables()
        {
            var problems = new List<string>();

            foreach (var entry in LanguageTables.PreferredRegion)
            {
                if (LanguageTag.Parse(entry.Key + "-" + entry.Value).Region != entry.Value)
                    problems.Add($"PreferredRegion['{entry.Key}'] = '{entry.Value}' não tem forma de região");
            }

            foreach (var entry in LanguageTables.RegionSoleLanguage)
            {
                if (LanguageTag.Parse(entry.Value).Language != entry.Value)
                    problems.Add($"RegionSoleLanguage['{entry.Key}'] = '{entry.Value}' não tem forma de idioma");
            }

            foreach (var entry in LanguageTables.Iso3To2)
            {
                if (entry.Key.Length != 3 || entry.Value.Length != 2)
                    problems.Add($"Iso3To2['{entry.Key}'] = '{entry.Value}' deveria ser 3 letras → 2 letras");
            }

            foreach (var entry in LanguageTables.CanonicalLanguage)
            {
                if (LanguageTables.CanonicalLanguage.ContainsKey(entry.Value))
                    problems.Add($"CanonicalLanguage['{entry.Key}'] = '{entry.Value}' encadeia com outra entrada");
            }

            foreach (var entry in LanguageTables.ScriptRegions)
            {
                var tag = LanguageTag.Parse(entry.Key);
                if (!tag.HasLanguage || tag.Script.Length == 0)
                    problems.Add($"ScriptRegions['{entry.Key}'] não tem a forma 'idioma-script'");
            }

            foreach (var entry in LanguageTables.DefaultScript)
            {
                if (!LanguageTables.ScriptRegions.ContainsKey(entry.Key + "-" + entry.Value))
                    problems.Add($"DefaultScript['{entry.Key}'] = '{entry.Value}' não tem entrada em ScriptRegions");
            }

            // Idempotência sobre todas as chaves conhecidas: Canonicalize é usada como último
            // recurso e não pode ficar oscilando entre duas formas.
            foreach (var key in AllTableKeys())
            {
                var once = Canonicalize(key);
                var twice = Canonicalize(once);
                if (once != twice)
                    problems.Add($"Canonicalize não é idempotente para '{key}': '{once}' → '{twice}'");
            }

            return problems;
        }

        private static IEnumerable<string> AllTableKeys()
        {
            foreach (var key in LanguageTables.PreferredRegion.Keys) yield return key;
            foreach (var key in LanguageTables.RegionSoleLanguage.Keys) yield return key;
            foreach (var key in LanguageTables.Iso3To2.Keys) yield return key;
            foreach (var key in LanguageTables.Alpha3Region.Keys) yield return key;
            foreach (var key in LanguageTables.CanonicalLanguage.Keys) yield return key;
            foreach (var key in LanguageTables.ScriptRegions.Keys) yield return key;
            foreach (var key in LanguageTables.FullCodeChain.Keys) yield return key;
        }

        // ------------------------------------------------------------------ Interno

        private static void BuildCandidates(string normalized, List<string> output)
        {
            // Índice 0: a entrada crua. É a âncora de compatibilidade — uma planilha que já
            // tenha uma coluna literal "cn" continua sendo atendida por ela.
            Add(output, normalized);
            if (normalized.Length == 0)
                return;

            Add(output, LookupAlias(normalized));

            var tag = LanguageTag.Parse(normalized);
            if (tag.IsEmpty)
                return;

            var rawLanguage = tag.Language;
            var lang = rawLanguage;
            var script = tag.Script;
            var region = tag.Region;

            if (lang.Length == 0)
            {
                Add(output, tag.ToCode());
                return;
            }

            Add(output, LookupAlias(lang));

            // Idiomas que não têm coluna própria em planilha nenhuma: "yue" → zh-hk → zh-tw → zh.
            if (LanguageTables.FullCodeChain.TryGetValue(lang, out var chain))
            {
                for (int i = 0; i < chain.Length; i++)
                    Add(output, chain[i]);
            }

            if (lang.Length == 3)
            {
                var twoLetter = LanguageTables.Lookup(LanguageTables.Iso3To2, lang);
                if (twoLetter.Length > 0)
                {
                    lang = twoLetter;
                }
                else
                {
                    var expanded = LanguageTables.Lookup(LanguageTables.Alpha3Region, lang);
                    if (expanded.Length > 0)
                    {
                        Add(output, expanded);
                        var parsed = LanguageTag.Parse(expanded);
                        lang = parsed.Language;
                        if (region.Length == 0)
                            region = parsed.Region;
                    }
                }
            }

            var canonicalLanguage = LanguageTables.Lookup(LanguageTables.CanonicalLanguage, lang);
            if (canonicalLanguage.Length > 0)
                lang = canonicalLanguage;

            Add(output, Canonicalize(normalized));

            if (script.Length > 0)
                Add(output, lang + "-" + script);

            if (region.Length > 0)
                Add(output, lang + "-" + region);

            // Regiões irmãs de mesmo script antes de qualquer outra: "zh-hk" cai em "zh-tw"
            // (Tradicional), nunca em "zh-cn" (Simplificado).
            AddScriptSiblings(output, lang, ScriptOf(lang, script, region));

            Add(output, lang);

            // Códigos equivalentes (iw ↔ he, sh ↔ sr), preservando a região pedida. Vêm depois
            // da raiz de propósito: "sr" é uma resposta melhor que o depreciado "sh".
            if (LanguageTables.LanguageSiblings.TryGetValue(lang, out var siblings))
            {
                for (int i = 0; i < siblings.Length; i++)
                {
                    if (region.Length > 0)
                        Add(output, siblings[i] + "-" + region);

                    Add(output, siblings[i]);
                }
            }

            // Reinterpretação região → idioma. Aqui é generosa de propósito, ao contrário de
            // Canonicalize: a leitura como idioma já foi tentada acima, então "in" pode ainda
            // alcançar "hi-in" se a planilha só tiver hindi.
            if (region.Length == 0 && script.Length == 0)
            {
                var fromRegion = LanguageTables.Lookup(LanguageTables.RegionSoleLanguage, rawLanguage);
                if (fromRegion.Length > 0 && fromRegion != rawLanguage)
                {
                    Add(output, fromRegion + "-" + rawLanguage);
                    Add(output, fromRegion);
                    AddScriptSiblings(output, fromRegion, ScriptOf(fromRegion, string.Empty, rawLanguage));
                }
            }

            var preferredRegion = LanguageTables.Lookup(LanguageTables.PreferredRegion, lang);
            if (preferredRegion.Length > 0)
                Add(output, lang + "-" + preferredRegion);

            // Último recurso: regiões dos outros scripts do mesmo idioma.
            if (LanguageTables.LanguageScripts.TryGetValue(lang, out var scripts))
            {
                for (int i = 0; i < scripts.Count; i++)
                    AddScriptSiblings(output, lang, scripts[i]);
            }
        }

        private static void Add(List<string> output, string candidate)
        {
            if (candidate == null)
                return;

            // Vazio só vale como índice 0 (entrada vazia), nunca como candidato de verdade.
            if (candidate.Length == 0 && output.Count > 0)
                return;

            if (output.Count >= MaxCandidates)
                return;

            for (int i = 0; i < output.Count; i++)
            {
                if (string.Equals(output[i], candidate, StringComparison.Ordinal))
                    return;
            }

            output.Add(candidate);
        }

        private static void AddScriptSiblings(List<string> output, string language, string script)
        {
            var regions = ScriptRegionsOf(language, script);
            for (int i = 0; i < regions.Length; i++)
                Add(output, language + "-" + regions[i]);
        }

        private static string[] ScriptRegionsOf(string language, string script)
        {
            if (language.Length == 0 || script.Length == 0)
                return EmptyStrings;

            return LanguageTables.ScriptRegions.TryGetValue(language + "-" + script, out var regions)
                ? regions
                : EmptyStrings;
        }

        /// <summary>Script explícito, senão o implicado pela região, senão o padrão do idioma.</summary>
        private static string ScriptOf(string language, string script, string region)
        {
            if (script.Length > 0)
                return script;

            if (region.Length > 0)
            {
                var fromRegion = LanguageTables.Lookup(LanguageTables.RegionScript, language + "-" + region);
                if (fromRegion.Length > 0)
                    return fromRegion;
            }

            return LanguageTables.Lookup(LanguageTables.DefaultScript, language);
        }

        private static string LookupAlias(string normalized)
        {
            lock (CacheLock)
            {
                return Aliases.TryGetValue(normalized, out var value) ? value : string.Empty;
            }
        }

        private static bool IsPreferredRegion(string entry)
        {
            var tag = LanguageTag.Parse(entry);
            if (!tag.HasLanguage || tag.Region.Length == 0)
                return false;

            return string.Equals(LanguageTables.Lookup(LanguageTables.PreferredRegion, tag.Language),
                tag.Region, StringComparison.Ordinal);
        }

        /// <summary>Ordem total sobre os empates, para o resultado não depender da ordem de enumeração.</summary>
        private static bool Beats(string entry, int score, bool preferred, string best, int bestScore, bool bestPreferred)
        {
            if (best == null)
                return true;

            if (score != bestScore)
                return score > bestScore;

            if (preferred != bestPreferred)
                return preferred;

            var candidate = Normalize(entry);
            var incumbent = Normalize(best);

            if (candidate.Length != incumbent.Length)
                return candidate.Length < incumbent.Length;

            return string.CompareOrdinal(candidate, incumbent) < 0;
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
