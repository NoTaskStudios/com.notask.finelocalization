using System;
using System.Collections.Generic;
using FineLocalization.Runtime;
using TMPro;
using UnityEngine;

namespace FineLocalization.Scripts.Runtime
{
    /// <summary>
    /// Gerencia as tabelas de fallback do TextMeshPro.
    ///
    /// Há duas camadas com prioridades diferentes:
    /// <list type="bullet">
    /// <item><b>Fonte remota do idioma</b> — entra na frente (índice 0) e é trocada a cada idioma.</item>
    /// <item><b>Fontes de cobertura</b> — entram no fim e nunca saem; suprem glifos soltos
    /// (₴, ₹) que a fonte principal do projeto não tem.</item>
    /// </list>
    /// </summary>
    internal static class TmpFallbackRegistry
    {
        /// <summary>
        /// Identifica os font assets que o pacote gerencia (baixados de bundle). Só esses são
        /// removidos ao trocar de idioma — fontes do projeto nunca são mexidas.
        /// </summary>
        internal static Func<TMP_FontAsset, bool> IsManagedRemote { get; set; } = _ => false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            IsManagedRemote = _ => false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Fallbacks apontando para materiais de uma sessão de Play anterior sobrevivem ao stop
        /// e derrubam o TMP no Play seguinte. Limpa antes da cena carregar.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DropStaleEditorFallbacks()
        {
            RemoveUnusable(TMP_Settings.fallbackFontAssets);

            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (int i = 0; i < fonts.Length; i++)
                RemoveUnusable(fonts[i]?.fallbackFontAssetTable);

            TmpFontRepair.ClearFallbackMaterialCache();
        }
#endif

        /// <summary>
        /// Registra fontes de cobertura como fallback permanente de baixa prioridade nas fontes
        /// principais e nos fallbacks globais do TMP.
        /// </summary>
        internal static void InstallCoverage(IList<TMP_FontAsset> coverage, IList<TMP_FontAsset> mainFonts)
        {
            if (coverage == null)
                return;

            for (int i = 0; i < coverage.Count; i++)
            {
                var font = coverage[i];
                if (font == null)
                    continue;

                if (!TmpFontRepair.Repair(font))
                {
                    FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Fonte de cobertura inválida, ignorada: {TmpFontRepair.Describe(font)}");
                    continue;
                }

                if (mainFonts != null)
                {
                    for (int j = 0; j < mainFonts.Count; j++)
                    {
                        var main = mainFonts[j];
                        // Uma fonte não pode ser fallback de si mesma: gera material e atlas redundantes.
                        if (main == null || IsSameAsset(main, font))
                            continue;

                        Append(EnsureTable(main), font);
                    }
                }

                Append(GlobalFallbacks(), font);
            }
        }

        /// <summary>
        /// Instala a fonte remota como fallback prioritário, removendo antes as fontes remotas
        /// dos outros idiomas. Devolve false quando a fonte não está renderizável.
        /// </summary>
        internal static bool InstallRemote(TMP_FontAsset remote, IList<TMP_FontAsset> mainFonts, TMP_Text[] sceneTexts)
        {
            if (!TmpFontRepair.Repair(remote))
            {
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Fonte remota inválida, não instalada: {TmpFontRepair.Describe(remote)}");
                return false;
            }

            RemoveManagedRemote(remote, mainFonts, sceneTexts);
            Prepend(GlobalFallbacks(), remote);

            var seen = new HashSet<TMP_FontAsset>();
            var hasMainFonts = false;
            var installed = 0;

            if (mainFonts != null)
            {
                for (int i = 0; i < mainFonts.Count; i++)
                {
                    if (mainFonts[i] == null)
                        continue;

                    hasMainFonts = true;
                    if (TryPrepend(mainFonts[i], remote, seen))
                        installed++;
                }
            }

            // Sem Main Font Assets, a única coisa que sobra é a lista global e os textos que já
            // existem na cena. Um jogo que monta a UI depois do boot — o caso comum, com a
            // localização gateando o passo de boot — não tem texto nenhum aqui, e o idioma
            // aplicava sem nunca trocar o glifo. Patch no asset da fonte default cobre isso,
            // porque a tabela de fallback é do asset e todo texto criado depois herda.
            if (!hasMainFonts && TryPrepend(TMP_Settings.defaultFontAsset, remote, seen))
                installed++;

            if (sceneTexts != null)
            {
                for (int i = 0; i < sceneTexts.Length; i++)
                {
                    if (TryPrepend(sceneTexts[i] != null ? sceneTexts[i].font : null, remote, seen))
                        installed++;
                }
            }

            TmpFontRepair.ClearFallbackMaterialCache();

            var targets = installed;
            FineLocalizationLogger.Log(() =>
                $"[FineLocalization] Fallback de '{remote.name}' instalado na lista global e em {targets} fonte(s). " +
                (hasMainFonts
                    ? string.Empty
                    : "Main Font Assets está vazio — preencha com as fontes principais da UI para " +
                      "garantir que textos instanciados depois do boot também resolvam os glifos.")
            );

            return true;
        }

        /// <summary>
        /// Tira das tabelas de fallback todas as fontes remotas gerenciadas, menos
        /// <paramref name="keep"/>. Evita que a fonte do idioma anterior continue resolvendo glifos.
        /// </summary>
        internal static void RemoveManagedRemote(TMP_FontAsset keep, IList<TMP_FontAsset> mainFonts, TMP_Text[] sceneTexts)
        {
            Purge(GlobalFallbacks(), keep);

            if (mainFonts != null)
            {
                for (int i = 0; i < mainFonts.Count; i++)
                    Purge(mainFonts[i]?.fallbackFontAssetTable, keep);
            }

            if (sceneTexts == null)
                return;

            for (int i = 0; i < sceneTexts.Length; i++)
                Purge(sceneTexts[i]?.font?.fallbackFontAssetTable, keep);
        }

        /// <summary>
        /// Valida e conserta a lista global de fallbacks. Obrigatório antes de qualquer
        /// <c>ForceMeshUpdate</c>: o TMP percorre essa lista para todo caractere ausente na fonte
        /// principal, e uma entrada com material nulo derruba <c>GetFallbackMaterial</c> — algo
        /// que validar só a tabela local da fonte não previne.
        /// </summary>
        internal static void SanitizeGlobals()
        {
            var globals = GlobalFallbacks();
            if (globals == null)
                return;

            for (int i = globals.Count - 1; i >= 0; i--)
            {
                var font = globals[i];
                if (font == null || !TmpFontRepair.Repair(font))
                {
                    FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Removendo fallback global inválido: {TmpFontRepair.Describe(font)}");
                    globals.RemoveAt(i);
                }
            }
        }

        /// <summary>True quando a fonte e toda a sua árvore de fallback estão renderizáveis.</summary>
        internal static bool ValidateTree(TMP_FontAsset root)
        {
            if (root == null || !TmpFontRepair.Repair(root))
                return false;

            var table = root.fallbackFontAssetTable;
            if (table == null)
                return true;

            for (int i = table.Count - 1; i >= 0; i--)
            {
                var fallback = table[i];
                if (fallback == null || !TmpFontRepair.Repair(fallback))
                {
                    FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Removendo fallback inválido de '{root.name}'.");
                    table.RemoveAt(i);
                }
            }

            return true;
        }

        // ------------------------------------------------------------------ Interno

        /// <summary>Devolve true quando o fallback foi de fato instalado em <paramref name="target"/>.</summary>
        private static bool TryPrepend(TMP_FontAsset target, TMP_FontAsset remote, HashSet<TMP_FontAsset> seen)
        {
            if (target == null || IsSameAsset(target, remote) || SafeIsManaged(target))
                return false;

            if (!seen.Add(target) || !ValidateTree(target))
                return false;

            Prepend(EnsureTable(target), remote);
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, target);
            return true;
        }

        private static void Prepend(List<TMP_FontAsset> table, TMP_FontAsset font)
        {
            if (table == null || font == null)
                return;

            table.Remove(font);
            table.Insert(0, font);
        }

        private static void Append(List<TMP_FontAsset> table, TMP_FontAsset font)
        {
            if (table == null || font == null || table.Contains(font))
                return;

            table.Add(font);
            FineLocalizationLogger.Log(() => $"[FineLocalization] Fallback de cobertura registrado: '{font.name}'.");
        }

        private static void Purge(List<TMP_FontAsset> table, TMP_FontAsset keep)
        {
            if (table == null)
                return;

            for (int i = table.Count - 1; i >= 0; i--)
            {
                var font = table[i];
                if (font == null)
                    table.RemoveAt(i);
                else if (font != keep && SafeIsManaged(font))
                    table.RemoveAt(i);
            }
        }

        private static void RemoveUnusable(List<TMP_FontAsset> table)
        {
            if (table == null)
                return;

            for (int i = table.Count - 1; i >= 0; i--)
            {
                if (table[i] == null || !TmpFontRepair.IsUsable(table[i]))
                    table.RemoveAt(i);
            }
        }

        private static List<TMP_FontAsset> EnsureTable(TMP_FontAsset font)
        {
            return font.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
        }

        /// <summary>
        /// Lista global de fallbacks do TMP. Em algumas versões ela nasce nula e a propriedade
        /// pública não permite criá-la, daí a escrita direta no campo serializado.
        /// </summary>
        private static List<TMP_FontAsset> GlobalFallbacks()
        {
            var globals = TMP_Settings.fallbackFontAssets;
            if (globals != null)
                return globals;

            try
            {
                var settings = TMP_Settings.instance;
                var field = typeof(TMP_Settings).GetField(
                    "m_fallbackFontAssets",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                );

                if (settings == null || field == null)
                    return null;

                if (field.GetValue(settings) is not List<TMP_FontAsset> list)
                {
                    list = new List<TMP_FontAsset>();
                    field.SetValue(settings, list);
                }

                return list;
            }
            catch
            {
                FineLocalizationLogger.LogWarning("[FineLocalization] TMP_Settings.fallbackFontAssets é nulo e não pôde ser criado nesta versão do TMP.");
                return null;
            }
        }

        private static bool IsSameAsset(TMP_FontAsset a, TMP_FontAsset b)
        {
            if (a == null || b == null)
                return false;

            return a == b || string.Equals(a.name, b.name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SafeIsManaged(TMP_FontAsset font)
        {
            try
            {
                return font != null && (IsManagedRemote?.Invoke(font) ?? false);
            }
            catch
            {
                return false;
            }
        }
    }
}
