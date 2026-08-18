#if UNITY_EDITOR

using System.Collections.Generic;
using System.Linq;
using System.Text;
using FineLocalization.Runtime;
using UnityEditor;
using UnityEngine;

namespace FineLocalization.EditorTools
{
    /// <summary>
    /// Verifica a resolução de códigos de idioma sem precisar entrar em Play. A tabela
    /// <see cref="Cases"/> <b>é</b> a especificação: cada linha é um pedido, as colunas que a
    /// planilha teria, e a coluna que deve ganhar.
    ///
    /// Rode isto depois de mexer em <see cref="LanguageCode"/> ou nas tabelas de
    /// <see cref="LanguageTag"/>. O primeiro bloco é o corpus de compatibilidade — se ele
    /// quebrar, alguém que já usa o pacote vai ver o idioma mudar sozinho.
    /// </summary>
    internal static class LanguageCodeSelfCheck
    {
        // { pedido, colunas separadas por '|', esperado ("-" = nenhuma) }
        private static readonly string[][] Cases =
        {
            // ---------- compatibilidade: tem que continuar batendo com a v3.0.0 ----------
            new[] { "en-us", "en-us|pt-br",      "en-us" },
            new[] { "pt-br", "en-us|pt-br",      "pt-br" },
            new[] { "cn",    "en-us|cn",         "cn"    },   // coluna literal "cn" continua ganhando
            new[] { "cn",    "en-us|cn|zh-cn",   "cn"    },
            new[] { "zh-cn", "en-us|zh-cn",      "zh-cn" },
            new[] { "ja-jp", "en-us|ja-jp",      "ja-jp" },
            new[] { "en-us", "en-us",            "en-us" },
            new[] { "ba-id", "en-us|ba-id",      "ba-id" },
            new[] { "qq-zz", "en-us",            "-"     },

            // ---------- o caso Playtech: região mandada no campo de idioma ----------
            new[] { "cn", "en-us|zh-cn",         "zh-cn" },
            new[] { "cn", "en-us|zh-tw",         "zh-tw" },
            new[] { "cn", "en-us|zh-cn|zh-tw",   "zh-cn" },
            new[] { "CN", "en-us|zh-cn|zh-tw",   "zh-cn" },
            new[] { "jp", "en-us|ja-jp",         "ja-jp" },
            new[] { "kr", "en-us|ko-kr",         "ko-kr" },
            new[] { "us", "en-us|pt-br",         "en-us" },
            new[] { "br", "en-us|pt-br",         "pt-br" },
            new[] { "br", "en-us|br",            "br"    },   // coluna bretã continua ganhando
            new[] { "gb", "en-us|en-gb",         "en-gb" },

            // ---------- chinês: Tradicional nunca pode virar Simplificado ----------
            new[] { "zh",          "en-us|zh-cn|zh-tw", "zh-cn" },
            new[] { "zh",          "en-us|zh-tw|zh-cn", "zh-cn" },   // ordem das colunas não decide
            new[] { "zh_CN",       "en-us|zh-cn|zh-tw", "zh-cn" },
            new[] { "zh-hans",     "en-us|zh-cn|zh-tw", "zh-cn" },
            new[] { "zh-Hans-CN",  "en-us|zh-cn|zh-tw", "zh-cn" },
            new[] { "zh-hant",     "en-us|zh-cn|zh-tw", "zh-tw" },
            new[] { "zh-hant",     "en-us|zh-cn",       "zh-cn" },
            new[] { "zh-hk",       "en-us|zh-cn|zh-tw", "zh-tw" },
            new[] { "zh-hk",       "en-us|zh-tw|zh-cn", "zh-tw" },
            new[] { "zh-hk",       "en-us|zh-hk|zh-tw", "zh-hk" },
            new[] { "zh-mo",       "en-us|zh-cn|zh-tw", "zh-tw" },
            new[] { "zh-tw",       "en-us|zh-cn|zh-tw", "zh-tw" },
            new[] { "zh-Hant-HK",  "en-us|zh-cn|zh-tw", "zh-tw" },
            new[] { "zh-sg",       "en-us|zh-cn|zh-tw", "zh-cn" },
            new[] { "yue",         "en-us|zh-cn|zh-tw", "zh-tw" },
            new[] { "yue",         "en-us|zh-cn",       "zh-cn" },

            // ---------- códigos depreciados e de três letras ----------
            new[] { "iw",  "en-us|he",          "he"    },
            new[] { "iw",  "en-us|he-il",       "he-il" },
            new[] { "he",  "en-us|iw",          "iw"    },
            new[] { "in",  "en-us|id-id",       "id-id" },
            new[] { "in",  "en-us|hi-in",       "hi-in" },
            new[] { "esp", "en-us|es-es",       "es-es" },
            new[] { "chn", "en-us|zh-cn",       "zh-cn" },
            new[] { "por", "en-us|pt-br",       "pt-br" },
            new[] { "jpn", "en-us|ja-jp",       "ja-jp" },
            new[] { "zho", "en-us|zh-cn|zh-tw", "zh-cn" },

            // ---------- código genérico com várias regiões na planilha ----------
            new[] { "pt",     "pt-pt|pt-br",  "pt-br" },
            new[] { "pt",     "pt-br|pt-pt",  "pt-br" },
            new[] { "pt",     "en-us|pt-pt",  "pt-pt" },
            new[] { "en",     "en-gb|en-us",  "en-us" },
            new[] { "es",     "es-mx|es-es",  "es-es" },
            new[] { "en-GB",  "en-us|en-gb",  "en-gb" },
            new[] { "en-GB",  "en-us",        "en-us" },
            new[] { "es-419", "en-us|es-es",  "es-es" },
            new[] { "es-419", "en-us|es-mx",  "es-mx" },

            // ---------- script explícito ----------
            new[] { "sr-latn",    "en-us|sr-latn|sr-cyrl", "sr-latn" },
            new[] { "sr-Cyrl-RS", "en-us|sr-rs",           "sr-rs"   },

            // ---------- lixo que não pode derrubar a carga ----------
            new[] { "und",                "en-us|zh-cn", "-"     },
            new[] { "",                   "en-us|zh-cn", "-"     },
            new[] { "*",                  "en-us|zh-cn", "-"     },
            new[] { "x-klingon",          "en-us",       "-"     },
            new[] { "en-US.UTF-8",        "en-us",       "en-us" },
            new[] { "zh-CN@pinyin",       "en-us|zh-cn", "zh-cn" },
            new[] { "zh-CN-#Hans",        "en-us|zh-cn", "zh-cn" },
            new[] { "en-US-u-ca-gregory", "en-us",       "en-us" },
            new[] { "zh-min-nan",         "en-us|zh-cn", "zh-cn" }
        };

        // { código, é script Latin? }. "false" significa "precisa de fonte remota".
        private static readonly object[][] LatinCases =
        {
            new object[] { "en-us", true }, new object[] { "pt-br", true },
            new object[] { "ba-id", true }, new object[] { "us", true },
            new object[] { "gb", true },    new object[] { "br", true },
            new object[] { "sr-latn", true },
            new object[] { "cn", false },   new object[] { "jp", false },
            new object[] { "kr", false },   new object[] { "zh-cn", false },
            new object[] { "ja-jp", false }, new object[] { "th-th", false },
            new object[] { "ku-arab", false }, new object[] { "", false }
        };

        [MenuItem("Tools/Fine Localization/Diagnostics/Run Language Code Self Check")]
        private static void Run()
        {
            var failures = new List<string>();
            var passed = 0;

            foreach (var problem in LanguageCode.ValidateTables())
                failures.Add("Tabela inconsistente: " + problem);

            foreach (var test in Cases)
            {
                var request = test[0];
                var columns = test[1].Length == 0 ? new string[0] : test[1].Split('|');
                var expected = test[2] == "-" ? null : test[2];

                // O resultado não pode depender da ordem das colunas na planilha.
                Expect(failures, ref passed, request, columns, expected, "ordem original");
                Expect(failures, ref passed, request, columns.Reverse().ToArray(), expected, "ordem invertida");

                var normalized = LanguageCode.Normalize(request);
                var candidates = LanguageCode.Candidates(request);

                if (candidates.Count == 0 || candidates[0] != normalized)
                    failures.Add($"Candidates('{request}')[0] deveria ser '{normalized}' — é a âncora de compatibilidade");
                else
                    passed++;

                var once = LanguageCode.Canonicalize(request);
                if (once != LanguageCode.Canonicalize(once))
                    failures.Add($"Canonicalize não é idempotente para '{request}': '{once}' → '{LanguageCode.Canonicalize(once)}'");
                else
                    passed++;

                foreach (var column in columns)
                {
                    if (LanguageCode.MatchScore(normalized, column) >= 0 &&
                        LanguageCode.SelectBest(request, new[] { column }) == null)
                        failures.Add($"SelectBest('{request}', ['{column}']) devolveu null mas MatchScore >= 0");
                    else
                        passed++;
                }
            }

            foreach (var latin in LatinCases)
            {
                var code = (string)latin[0];
                var expected = (bool)latin[1];
                if (LanguageCode.IsLatinScript(code) != expected)
                    failures.Add($"IsLatinScript('{code}') deveria ser {expected}");
                else
                    passed++;
            }

            if (LanguageCode.IsLatinScript("vi-vn", null, new[] { "vi" }))
                failures.Add("Force Remote Font Prefixes deveria vencer a tabela Latin");
            else
                passed++;

            if (!LanguageCode.IsLatinScript("xx-yy", new[] { "xx" }))
                failures.Add("Extra Latin Prefixes deveria marcar 'xx-yy' como Latin");
            else
                passed++;

            if (!LanguageCode.IsAcceptableFor("cn", "zh-cn"))
                failures.Add("'cn' atendido por 'zh-cn' é sucesso, não divergência");
            else
                passed++;

            if (LanguageCode.IsAcceptableFor("cn", "en-us"))
                failures.Add("'cn' atendido por 'en-us' deveria avisar");
            else
                passed++;

            if (LanguageCode.FromBundleName("font_zh_cn") != "zh-cn")
                failures.Add("FromBundleName tem que tirar 'font_' antes de trocar '_' por '-'");
            else
                passed++;

            Report(failures, passed);
        }

        private static void Expect(List<string> failures, ref int passed, string request,
            string[] columns, string expected, string label)
        {
            var actual = LanguageCode.SelectBest(request, columns);
            if (actual == expected)
            {
                passed++;
                return;
            }

            failures.Add($"SelectBest('{request}', [{string.Join(",", columns)}]) = '{actual ?? "null"}', " +
                         $"esperado '{expected ?? "null"}' ({label})\n      {LanguageCode.Explain(request, columns)}");
        }

        private static void Report(List<string> failures, int passed)
        {
            if (failures.Count == 0)
            {
                Debug.Log($"[FineLocalization] Self check de códigos de idioma: {passed} verificações passaram.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"[FineLocalization] Self check de códigos de idioma: {passed} passaram, {failures.Count} falharam.");
            foreach (var failure in failures)
                report.AppendLine("  • " + failure);

            Debug.LogError(report.ToString());
        }
    }
}

#endif
