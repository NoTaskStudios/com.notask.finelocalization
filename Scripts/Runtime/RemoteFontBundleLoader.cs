using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FineLocalization.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace FineLocalization.Scripts.Runtime
{
    /// <summary>
    /// Loads remote TMP_FontAsset bundles only for non-latin languages and registers them as TMP fallbacks.
    ///
    /// WebGL/mobile notes:
    /// - Does not scan/apply to every loaded TMP_FontAsset by default. This avoids touching broken/old font assets
    ///   from other projects/packages and keeps memory/CPU lower on 2 GB devices.
    /// - Rebuilds visible texts in small batches to avoid frame spikes.
    /// - Validates font material/atlas/main texture before adding fallback or forcing TMP rebuild.
    /// </summary>
    public class RemoteFontBundleLoader : MonoBehaviour
    {
        [Serializable]
        private class RemoteFontBundleConfig
        {
            [Tooltip("Prefixo de idioma usado para detectar quando carregar esta fonte. Ex: zh, zh-tw, ja, ko, th")]
            public string languagePrefix;

            [Tooltip("Opcional. Se vazio, usa Base Bundle Url + languagePrefix.")]
            public string bundleUrlOverride;

            [Tooltip("Nome exato do TMP_FontAsset dentro do bundle. Ex: NotoSansJP-used")]
            public string fontAssetName;
        }

        [Header("Remote Font Bundles")]
        [Tooltip("URL base da pasta dos bundles. Ex: https://cdn.site.com/fonts/")]
        [SerializeField] private string baseBundleUrl;

        [Tooltip("Extensão adicionada depois do prefixo quando usar a URL base. Ex: .ft")]
        [SerializeField] private string bundleFileExtension = ".ft";

        [SerializeField] private List<RemoteFontBundleConfig> bundles = new();

        [Header("Fallback Target")]
        [Tooltip("Se marcado, adiciona nos fallbacks globais do TMP Settings.")]
        [SerializeField] private bool addToGlobalTmpFallbacks = true;

        [Tooltip("Fontes principais do projeto que devem receber o fallback remoto diretamente. Recomendo colocar aqui sua fonte base principal, não as fontes remotas CJK.")]
        [SerializeField] private List<TMP_FontAsset> mainFontAssets = new();

        [Tooltip("Também adiciona o fallback nas fontes usadas pelos TMP_Text da cena.")]
        [SerializeField] private bool addToSceneTextFonts = true;

        [Tooltip("Desligado por padrão para WebGL/celulares. Se ligar, pode pegar fontes antigas/quebradas carregadas em memória e causar erro de material.")]
        [SerializeField] private bool addToAllLoadedFonts = false;

        [Tooltip("Força rebuild só nos textos ativos na hierarquia. Recomendado para WebGL mobile.")]
        [SerializeField] private bool rebuildOnlyActiveTexts = true;

        [Tooltip("Quantidade de TMP_Text rebuildados por frame. Menor = menos travada em celular 2GB; maior = troca de idioma mais rápida.")]
        [SerializeField] private int rebuildBatchSize = 24;

        [Tooltip("Último recurso. Evite em celular/WebGL: desativa/ativa GameObjects de texto.")]
        [SerializeField] private bool aggressiveRebuild = false;

        [Header("Safety Filters")]
        [Tooltip("Ignora fontes cujo nome contenha estes termos. Útil para evitar NotoSansJP antigo local quando o bundle usa NotoSansJP-used.")]
        [SerializeField] private List<string> ignoredFontNameContains = new() { "NotoSansJP" };

        [Tooltip("Se marcado, não ignora a própria fonte remota mesmo que o nome bata com ignoredFontNameContains.")]
        [SerializeField] private bool allowRemoteFontWhenIgnoredByName = true;

        [Header("Localization Integration")]
        [Tooltip("Carrega o bundle de fonte automaticamente quando LocalizationManager.Language mudar.")]
        [SerializeField] private bool loadOnLocalizationChanged = true;

        [Tooltip("Tenta carregar a fonte do idioma atual quando este componente for habilitado.")]
        [SerializeField] private bool loadCurrentLanguageOnEnable = false;

        [Header("Script Detection (Optimization)")]
        [Tooltip("Quando marcado, idiomas latinos (en, pt, es, fr...) não baixam bundle remoto.")]
        [SerializeField] private bool skipDownloadForLatinScripts = true;

        [Tooltip("Prefixos extras tratados como Latin. Use lowercase. Ex: eo")]
        [SerializeField] private List<string> extraLatinPrefixes = new();

        [Tooltip("Força bundle remoto para esses prefixos mesmo que sejam Latin. Ex: vi, tr")]
        [SerializeField] private List<string> forceRemoteFontPrefixes = new();

        [Header("Manual Test")]
        [SerializeField] private string testLanguage = "ja-jp";

        private static readonly HashSet<string> DefaultLatinScriptPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "en", "es", "pt", "fr", "de", "it", "nl", "ca", "gl", "eu", "oc", "rm",
            "sv", "no", "nb", "nn", "da", "fi", "is", "fo",
            "pl", "cs", "sk", "ro", "hu", "sl", "hr", "bs", "sq", "lt", "lv", "et",
            "tr", "az", "uz", "tk", "kk",
            "id", "ms", "vi", "tl", "fil",
            "ga", "cy", "gd", "br", "kw",
            "sw", "af", "zu", "xh", "yo", "ig", "ha", "so", "rw", "mg", "st", "sn", "ny",
            "lb", "fy", "mt", "ku", "ht", "qu", "gn"
        };

        private readonly HashSet<string> _loadedLanguages = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loadingLanguages = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TMP_FontAsset> _runtimeFontAssets = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Material> _runtimeMaterials = new();
        private readonly HashSet<TMP_FontAsset> _seenFonts = new();
        private readonly HashSet<TMP_FontAsset> _fontValidationStack = new();

        private void OnEnable()
        {
            if (loadOnLocalizationChanged)
                LocalizationManager.OnLocalizationChanged += EnsureCurrentLanguageFont;

            if (loadCurrentLanguageOnEnable)
                EnsureCurrentLanguageFont();
        }

        private void OnDisable()
        {
            if (loadOnLocalizationChanged)
                LocalizationManager.OnLocalizationChanged -= EnsureCurrentLanguageFont;
        }

        public void EnsureCurrentLanguageFont()
        {
            StartCoroutine(EnsureFontForLanguage(LocalizationManager.Language));
        }

        public void TestLanguage(string language)
        {
            StartCoroutine(EnsureFontForLanguage(language, success =>
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Teste manual finalizado para '{language}'. Success: {success}")));
        }

        [ContextMenu("Fine Localization/Test Remote Font Language")]
        private void TestConfiguredLanguage()
        {
            TestLanguage(testLanguage);
        }

        [ContextMenu("Fine Localization/Dump Diagnostics")]
        private void DumpDiagnosticsFromContextMenu()
        {
            DumpDiagnostics();
        }

        public void DumpDiagnostics()
        {
            var lang = LocalizationManager.Language ?? "<null>";
            var normalized = string.IsNullOrWhiteSpace(lang) ? "" : lang.Trim().ToLowerInvariant();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== RemoteFontBundleLoader Diagnostics ===");
            sb.AppendLine($"Current language: '{lang}' (normalized: '{normalized}')");
            sb.AppendLine($"Is Latin script: {IsLatinScript(normalized)}");
            sb.AppendLine($"Loaded bundles: {_loadedLanguages.Count} ({string.Join(", ", _loadedLanguages)})");
            sb.AppendLine($"Loading now: {_loadingLanguages.Count} ({string.Join(", ", _loadingLanguages)})");
            sb.AppendLine($"Runtime fonts: {_runtimeFontAssets.Count}");
            foreach (var kvp in _runtimeFontAssets)
                sb.AppendLine($"  {kvp.Key}: {DescribeFont(kvp.Value)}");

            var globals = TMP_Settings.fallbackFontAssets;
            sb.AppendLine($"TMP_Settings.fallbackFontAssets: {globals?.Count ?? 0} entries");
            if (globals != null)
            {
                for (int i = 0; i < globals.Count; i++)
                    sb.AppendLine($"  [{i}] {DescribeFont(globals[i])}");
            }

            FineLocalizationLogger.Log(sb.ToString());
        }

        public IEnumerator EnsureFontForLanguage(string language, Action<bool> onComplete = null)
        {
            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] EnsureFontForLanguage: {language}");

            if (string.IsNullOrWhiteSpace(language))
            {
                FineLocalizationLogger.LogWarning("[RemoteFontBundleLoader] Idioma vazio. Nenhum bundle será carregado.");
                onComplete?.Invoke(true);
                yield break;
            }

            var normalizedLanguage = language.Trim().ToLowerInvariant();

            if (IsLatinScript(normalizedLanguage))
            {
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] SKIP '{normalizedLanguage}': script Latin detectado.");
                onComplete?.Invoke(true);
                yield break;
            }

            var config = FindConfig(normalizedLanguage);
            if (config == null)
            {
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Nenhum bundle configurado para '{normalizedLanguage}'.");
                onComplete?.Invoke(true);
                yield break;
            }

            var prefix = config.languagePrefix.Trim().ToLowerInvariant();
            var bundleUrl = GetBundleUrl(config, prefix);

            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Config encontrada. Idioma: {normalizedLanguage} | Prefix: {prefix} | URL: {bundleUrl} | FontAsset: {config.fontAssetName}");

            if (_loadedLanguages.Contains(prefix))
            {
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Fonte de '{prefix}' já carregada. Reaplicando fallback e rebuild leve.");

                if (_runtimeFontAssets.TryGetValue(prefix, out var alreadyLoadedFont) && IsUsableFontAsset(alreadyLoadedFont))
                    yield return StartCoroutine(ApplyFallbackToTargetsAndRebuild(alreadyLoadedFont));
                else
                    FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Fonte em cache para '{prefix}' está inválida. Reinicie o loader ou recarregue a cena.");

                onComplete?.Invoke(true);
                yield break;
            }

            if (!_loadingLanguages.Add(prefix))
            {
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Fonte de '{prefix}' já está carregando. Aguardando...");
                while (_loadingLanguages.Contains(prefix))
                    yield return null;

                onComplete?.Invoke(_loadedLanguages.Contains(prefix));
                yield break;
            }

            if (string.IsNullOrWhiteSpace(bundleUrl))
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] URL do bundle vazia para '{prefix}'.");
                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            using var request = UnityWebRequestAssetBundle.GetAssetBundle(bundleUrl);
            yield return request.SendWebRequest();

            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Requisição finalizada. Result: {request.result} | HTTP: {request.responseCode} | Downloaded: {request.downloadedBytes} bytes | Error: {FormatRequestError(request)}");

            if (request.result != UnityWebRequest.Result.Success)
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Falha ao baixar bundle de fonte '{language}'. Erro: {request.error}");
                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            var bundle = DownloadHandlerAssetBundle.GetContent(request);
            if (bundle == null)
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Bundle retornou null para '{language}'.");
                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            TMP_FontAsset fontAsset = null;
            Material bundleMaterial = null;

            if (!string.IsNullOrWhiteSpace(config.fontAssetName))
            {
                var fontLoadRequest = bundle.LoadAssetAsync<TMP_FontAsset>(config.fontAssetName);
                yield return fontLoadRequest;

                fontAsset = fontLoadRequest.asset as TMP_FontAsset;
            }

            if (fontAsset == null)
            {
                var loadRequest = bundle.LoadAllAssetsAsync();
                yield return loadRequest;

                fontAsset = FindFontAssetFromLoadedAssets(loadRequest.allAssets, config.fontAssetName);
                bundleMaterial = FindBestMaterialFromLoadedAssets(loadRequest.allAssets, fontAsset);
            }

            if (fontAsset == null)
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Nenhum TMP_FontAsset encontrado no bundle para '{language}'. Assets: {string.Join(", ", bundle.GetAllAssetNames())}");
                bundle.Unload(false);
                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(config.fontAssetName) && !fontAsset.name.Equals(config.fontAssetName, StringComparison.OrdinalIgnoreCase))
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] TMP_FontAsset encontrado como '{fontAsset.name}', mas o configurado é '{config.fontAssetName}'.");
            }

            RepairFontMaterial(fontAsset, bundleMaterial);
            SanitizeFallbackTree(fontAsset, fontAsset);

            if (!IsUsableFontAsset(fontAsset))
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Fonte remota inválida após reparo: {DescribeFont(fontAsset)}");
                bundle.Unload(false);
                _loadingLanguages.Remove(prefix);
                onComplete?.Invoke(false);
                yield break;
            }

            fontAsset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (fontAsset.material != null)
                fontAsset.material.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            _runtimeFontAssets[prefix] = fontAsset;

            RegisterGlobalFallback(fontAsset);
            yield return StartCoroutine(ApplyFallbackToTargetsAndRebuild(fontAsset));

            _loadedLanguages.Add(prefix);
            _loadingLanguages.Remove(prefix);

            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Fonte registrada com sucesso: {DescribeFont(fontAsset)}");

            bundle.Unload(false);
            onComplete?.Invoke(true);
        }

        private IEnumerator ApplyFallbackToTargetsAndRebuild(TMP_FontAsset remoteFont)
        {
            yield return null;

            RepairFontMaterial(remoteFont, null);
            if (!IsUsableFontAsset(remoteFont))
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] ApplyFallback cancelado: fonte remota inválida: {DescribeFont(remoteFont)}");
                yield break;
            }

            _seenFonts.Clear();

            if (mainFontAssets != null)
            {
                for (int i = 0; i < mainFontAssets.Count; i++)
                    TryAddFallbackToFont(mainFontAssets[i], remoteFont, "mainFontAssets");
            }

            TMP_Text[] texts = FindSceneTexts();

            if (addToSceneTextFonts)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    var text = texts[i];
                    if (text == null || text.font == null)
                        continue;

                    TryAddFallbackToFont(text.font, remoteFont, $"TMP_Text '{text.name}'");
                }
            }

            if (addToAllLoadedFonts)
            {
                var allFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] addToAllLoadedFonts=true | TMP_FontAssets em memória: {allFonts.Length}");

                for (int i = 0; i < allFonts.Length; i++)
                    TryAddFallbackToFont(allFonts[i], remoteFont, "allLoadedFonts");
            }

            _seenFonts.Clear();

            yield return StartCoroutine(RebuildTextsBatched(texts));
        }

        private IEnumerator RebuildTextsBatched(TMP_Text[] texts)
        {
            if (texts == null || texts.Length == 0)
                yield break;

            // Repair/remove any global TMP fallbacks with null materials BEFORE starting the
            // rebuild loop. TMP_MaterialManager.GetFallbackMaterial walks TMP_Settings.fallbackFontAssets
            // for every character not found in the primary font; a null material there causes a crash
            // that ValidateFontTreeForText (which only checks local fallbackFontAssetTable) cannot prevent.
            RepairGlobalTMPFallbacks();

            int rebuilt = 0;
            int skipped = 0;
            int batch = Mathf.Max(1, rebuildBatchSize);

            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] RebuildTextsBatched: analisando {texts.Length} TMP_Text(s), batch={batch}, onlyActive={rebuildOnlyActiveTexts}.");

            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (!CanRebuildText(text))
                {
                    skipped++;
                    continue;
                }

                if (!ValidateFontTreeForText(text.font))
                {
                    skipped++;
                    FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Rebuild pulado: texto='{text.name}', fonte inválida='{DescribeFont(text.font)}'.");
                    continue;
                }

                try
                {
                    text.SetAllDirty();
                    text.havePropertiesChanged = true;
                    text.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: true);
                    rebuilt++;
                }
                catch (Exception ex)
                {
                    // Catch all exceptions so a single bad TMP_Text never aborts the whole rebuild.
                    // Known cases: UnassignedReferenceException (null m_Material property),
                    //              MissingReferenceException (fake-null / destroyed native object),
                    //              NullReferenceException (font or material became null mid-frame).
                    skipped++;
                    FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Rebuild falhou e foi ignorado. Texto='{text.name}', fonte='{DescribeFont(text.font)}', erro='{ex.GetType().Name}: {ex.Message}'.");
                }

                if (rebuilt > 0 && rebuilt % batch == 0)
                    yield return null;
            }

            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Rebuild concluído. Rebuild={rebuilt}, Skip={skipped}.");

            if (!aggressiveRebuild)
                yield break;

            yield return null;

            int toggled = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null || !text.gameObject.activeInHierarchy)
                    continue;

                var go = text.gameObject;
                go.SetActive(false);
                go.SetActive(true);
                toggled++;

                if (toggled > 0 && toggled % batch == 0)
                    yield return null;
            }
        }

        private bool CanRebuildText(TMP_Text text)
        {
            if (text == null || text.font == null)
                return false;

            if (rebuildOnlyActiveTexts && !text.gameObject.activeInHierarchy)
                return false;

            return true;
        }

        private bool TryAddFallbackToFont(TMP_FontAsset targetFont, TMP_FontAsset fallbackFont, string source)
        {
            if (targetFont == null || fallbackFont == null || targetFont == fallbackFont)
                return false;

            if (!_seenFonts.Add(targetFont))
                return false;

            if (ShouldIgnoreTargetFont(targetFont, fallbackFont))
            {
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Ignorando fonte alvo '{targetFont.name}' em {source}.");
                return false;
            }

            RepairFontMaterial(targetFont, null);
            RepairFontMaterial(fallbackFont, null);

            if (!ValidateFontTreeForText(targetFont))
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Não adicionou fallback: fonte alvo inválida em {source}: {DescribeFont(targetFont)}");
                return false;
            }

            if (!IsUsableFontAsset(fallbackFont))
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Não adicionou fallback: fonte remota inválida: {DescribeFont(fallbackFont)}");
                return false;
            }

            targetFont.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            RemoveNullAndDuplicateFamilyFallbacks(targetFont, fallbackFont);

            if (!targetFont.fallbackFontAssetTable.Contains(fallbackFont))
            {
                targetFont.fallbackFontAssetTable.Add(fallbackFont);
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Fallback adicionado: '{fallbackFont.name}' -> '{targetFont.name}' ({source}).");
            }

            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, targetFont);
            return true;
        }

        private void RegisterGlobalFallback(TMP_FontAsset fontAsset)
        {
            if (!addToGlobalTmpFallbacks || fontAsset == null)
                return;

            if (!IsUsableFontAsset(fontAsset))
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Global fallback ignorado: fonte inválida {DescribeFont(fontAsset)}");
                return;
            }

            var globalFallbacks = TMP_Settings.fallbackFontAssets;
            if (globalFallbacks == null)
            {
                globalFallbacks = TryCreateGlobalFallbackList();
                if (globalFallbacks == null)
                {
                    FineLocalizationLogger.LogWarning("[RemoteFontBundleLoader] TMP_Settings.fallbackFontAssets é NULL e não pôde ser inicializado nesta versão do TMP.");
                    return;
                }
            }

            RemoveNullAndDuplicateFamilyFallbacks(globalFallbacks, fontAsset);

            if (!globalFallbacks.Contains(fontAsset))
            {
                globalFallbacks.Add(fontAsset);
                FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Adicionado aos fallbacks globais: {fontAsset.name}. Total={globalFallbacks.Count}");
            }

            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
        }

        /// <summary>
        /// Validates every entry in <c>TMP_Settings.fallbackFontAssets</c> and tries to repair
        /// null materials in place. Entries that cannot be repaired are removed from the list.
        ///
        /// This must be called before any <c>ForceMeshUpdate</c> pass because TMP walks the
        /// global fallback list for every character not found in the primary font. If any entry
        /// has a null material, TMP_MaterialManager.GetFallbackMaterial throws even though
        /// <c>ValidateFontTreeForText</c> (which only checks local fallbackFontAssetTable) passed.
        /// </summary>
        private void RepairGlobalTMPFallbacks()
        {
            var globals = TMP_Settings.fallbackFontAssets;
            if (globals == null || globals.Count == 0)
                return;

            for (int i = globals.Count - 1; i >= 0; i--)
            {
                var font = globals[i];
                if (font == null)
                {
                    globals.RemoveAt(i);
                    continue;
                }

                RepairFontMaterial(font, null);

                if (!IsUsableFontAsset(font))
                {
                    FineLocalizationLogger.LogWarning(
                        () => $"[RemoteFontBundleLoader] Removendo fallback global inválido '{font.name}'. {DescribeFont(font)}"
                    );
                    globals.RemoveAt(i);
                }
            }
        }

        private static List<TMP_FontAsset> TryCreateGlobalFallbackList()
        {
            try
            {
                var settings = TMP_Settings.instance;
                if (settings == null)
                    return null;

                var field = typeof(TMP_Settings).GetField("m_fallbackFontAssets", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null)
                    return null;

                var list = field.GetValue(settings) as List<TMP_FontAsset>;
                if (list == null)
                {
                    list = new List<TMP_FontAsset>();
                    field.SetValue(settings, list);
                }

                return list;
            }
            catch
            {
                return null;
            }
        }

        private bool ValidateFontTreeForText(TMP_FontAsset rootFont)
        {
            _fontValidationStack.Clear();
            return ValidateFontTreeRecursive(rootFont);
        }

        private bool ValidateFontTreeRecursive(TMP_FontAsset font)
        {
            if (font == null)
                return false;

            if (!_fontValidationStack.Add(font))
                return true;

            RepairFontMaterial(font, null);
            if (!IsUsableFontAsset(font))
                return false;

            SanitizeFallbackTree(font, null);
            return true;
        }

        private void SanitizeFallbackTree(TMP_FontAsset font, TMP_FontAsset preferredRemoteFont)
        {
            if (font == null || font.fallbackFontAssetTable == null)
                return;

            var list = font.fallbackFontAssetTable;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var fallback = list[i];
                if (fallback == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                RepairFontMaterial(fallback, null);
                if (!IsUsableFontAsset(fallback))
                {
                    FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Removendo fallback inválido '{fallback.name}' de '{font.name}'.");
                    list.RemoveAt(i);
                    continue;
                }

                if (preferredRemoteFont != null && fallback != preferredRemoteFont && IsSameRemoteFontFamily(fallback, preferredRemoteFont))
                {
                    FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Removendo fallback duplicado '{fallback.name}' de '{font.name}', mantendo '{preferredRemoteFont.name}'.");
                    list.RemoveAt(i);
                }
            }
        }

        private void RemoveNullAndDuplicateFamilyFallbacks(TMP_FontAsset targetFont, TMP_FontAsset preferredFallback)
        {
            if (targetFont?.fallbackFontAssetTable == null)
                return;

            RemoveNullAndDuplicateFamilyFallbacks(targetFont.fallbackFontAssetTable, preferredFallback);
        }

        private void RemoveNullAndDuplicateFamilyFallbacks(List<TMP_FontAsset> list, TMP_FontAsset preferredFallback)
        {
            if (list == null)
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var item = list[i];
                if (item == null)
                {
                    list.RemoveAt(i);
                    continue;
                }

                if (preferredFallback != null && item != preferredFallback && IsSameRemoteFontFamily(item, preferredFallback))
                    list.RemoveAt(i);
            }
        }

        private bool ShouldIgnoreTargetFont(TMP_FontAsset targetFont, TMP_FontAsset remoteFont)
        {
            if (targetFont == null)
                return true;

            if (allowRemoteFontWhenIgnoredByName && targetFont == remoteFont)
                return false;

            if (ignoredFontNameContains == null)
                return false;

            for (int i = 0; i < ignoredFontNameContains.Count; i++)
            {
                var token = ignoredFontNameContains[i];
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                if (targetFont.name.IndexOf(token.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (allowRemoteFontWhenIgnoredByName && remoteFont != null && targetFont.name.Equals(remoteFont.name, StringComparison.OrdinalIgnoreCase))
                        return false;

                    return true;
                }
            }

            return false;
        }

        private bool IsSameRemoteFontFamily(TMP_FontAsset a, TMP_FontAsset b)
        {
            if (a == null || b == null)
                return false;

            var an = NormalizeFontFamilyName(a.name);
            var bn = NormalizeFontFamilyName(b.name);

            return !string.IsNullOrEmpty(an) && an.Equals(bn, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFontFamilyName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var result = RemoveOrdinalIgnoreCase(name, "-used");
            result = RemoveOrdinalIgnoreCase(result, "_used");
            result = RemoveOrdinalIgnoreCase(result, " Material");
            result = RemoveOrdinalIgnoreCase(result, " Atlas");
            return result.Trim();
        }

        private static string RemoveOrdinalIgnoreCase(string value, string token)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token))
                return value;

            int index;
            while ((index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase)) >= 0)
                value = value.Remove(index, token.Length);

            return value;
        }

        private void RepairFontMaterial(TMP_FontAsset fontAsset, Material preferredMaterial)
        {
            if (fontAsset == null)
                return;

            fontAsset.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var atlasTexture = fontAsset.atlasTexture;
            if (atlasTexture == null && fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
                atlasTexture = fontAsset.atlasTextures[0];

            var currentMaterial = fontAsset.material;
            if (TryNormalizeMaterial(currentMaterial, atlasTexture))
            {
                currentMaterial.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                return;
            }

            if (TryNormalizeMaterial(preferredMaterial, atlasTexture))
            {
                preferredMaterial.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                // Keep a strong managed reference so the C# GC cannot collect this
                // bundle-sourced material between the first load and subsequent rebuilds.
                if (!_runtimeMaterials.Contains(preferredMaterial))
                    _runtimeMaterials.Add(preferredMaterial);
                fontAsset.material = preferredMaterial;
                return;
            }

            if (atlasTexture == null)
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] '{fontAsset.name}' sem atlasTexture. Não é possível reparar material.");
                return;
            }

            var sourceMaterial = FindValidTMPMaterial(fontAsset);
            if (sourceMaterial == null)
            {
                FineLocalizationLogger.LogWarning(() => $"[RemoteFontBundleLoader] Não encontrou material TMP base para reparar '{fontAsset.name}'.");
                return;
            }

            var material = Instantiate(sourceMaterial);
            material.name = fontAsset.name + " Runtime Material";
            material.SetTexture(ShaderUtilities.ID_MainTex, atlasTexture);
            material.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            _runtimeMaterials.Add(material);
            fontAsset.material = material;

            FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Material reparado para '{fontAsset.name}' usando atlas '{atlasTexture.name}'.");
        }

        private Material FindValidTMPMaterial(TMP_FontAsset exclude)
        {
            if (mainFontAssets != null)
            {
                for (int i = 0; i < mainFontAssets.Count; i++)
                {
                    var font = mainFontAssets[i];
                    if (font == null || font == exclude)
                        continue;

                    var material = font.material;
                    if (material != null)
                        return material;
                }
            }

            var defaultMaterial = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset.material : null;
            if (defaultMaterial != null)
                return defaultMaterial;

            return null;
        }

        private static bool IsUsableFontAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return false;

            var atlas = fontAsset.atlasTexture;
            if (atlas == null && fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
                atlas = fontAsset.atlasTextures[0];

            return atlas != null && IsUsableMaterial(fontAsset.material, atlas);
        }

        private static bool TryNormalizeMaterial(Material material, Texture expectedAtlas)
        {
            if (material == null || material.shader == null)
                return false;

            if (expectedAtlas == null)
                return IsUsableMaterial(material, null);

            try
            {
                var mainTex = material.GetTexture(ShaderUtilities.ID_MainTex);
                if (mainTex == null)
                    material.SetTexture(ShaderUtilities.ID_MainTex, expectedAtlas);

                return material.GetTexture(ShaderUtilities.ID_MainTex) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUsableMaterial(Material material, Texture expectedAtlas)
        {
            if (material == null)
                return false;

            if (material.shader == null)
                return false;

            Texture mainTex = null;
            try
            {
                mainTex = material.GetTexture(ShaderUtilities.ID_MainTex);
            }
            catch
            {
                return false;
            }

            return mainTex != null || expectedAtlas == null;
        }

        private static TMP_Text[] FindSceneTexts()
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
#endif
        }

        private static TMP_FontAsset FindFontAssetFromLoadedAssets(UnityEngine.Object[] assets, string preferredName)
        {
            TMP_FontAsset firstFont = null;

            if (assets == null)
                return null;

            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not TMP_FontAsset font)
                    continue;

                firstFont ??= font;

                if (!string.IsNullOrWhiteSpace(preferredName) && font.name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                    return font;
            }

            return firstFont;
        }

        private static Material FindBestMaterialFromLoadedAssets(UnityEngine.Object[] assets, TMP_FontAsset fontAsset)
        {
            if (assets == null)
                return null;

            Material firstMaterial = null;
            Material nameMatchedMaterial = null;

            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not Material material)
                    continue;

                firstMaterial ??= material;

                if (fontAsset != null && material.name.IndexOf(fontAsset.name, StringComparison.OrdinalIgnoreCase) >= 0)
                    nameMatchedMaterial = material;
            }

            return nameMatchedMaterial != null ? nameMatchedMaterial : firstMaterial;
        }

        private RemoteFontBundleConfig FindConfig(string language)
        {
            if (bundles == null)
                return null;

            RemoteFontBundleConfig bestMatch = null;
            int bestLength = -1;

            for (int i = 0; i < bundles.Count; i++)
            {
                var config = bundles[i];
                if (config == null || string.IsNullOrWhiteSpace(config.languagePrefix))
                    continue;

                var prefix = config.languagePrefix.Trim().ToLowerInvariant();
                bool matches = language == prefix || language.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase);
                if (!matches)
                    continue;

                if (prefix.Length > bestLength)
                {
                    bestMatch = config;
                    bestLength = prefix.Length;
                }
            }

            return bestMatch;
        }

        private bool IsLatinScript(string normalizedLanguage)
        {
            if (!skipDownloadForLatinScripts || string.IsNullOrEmpty(normalizedLanguage))
                return false;

            var dashIndex = normalizedLanguage.IndexOf('-');
            var rootPrefix = dashIndex >= 0 ? normalizedLanguage.Substring(0, dashIndex) : normalizedLanguage;

            if (MatchesAnyPrefix(forceRemoteFontPrefixes, rootPrefix, normalizedLanguage))
                return false;

            return DefaultLatinScriptPrefixes.Contains(rootPrefix) || MatchesAnyPrefix(extraLatinPrefixes, rootPrefix, normalizedLanguage);
        }

        private static bool MatchesAnyPrefix(List<string> list, string rootPrefix, string normalizedLanguage)
        {
            if (list == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i]?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(item))
                    continue;

                if (item == rootPrefix || item == normalizedLanguage)
                    return true;
            }

            return false;
        }

        private string GetBundleUrl(RemoteFontBundleConfig config, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(config.bundleUrlOverride))
                return config.bundleUrlOverride.Trim();

            if (string.IsNullOrWhiteSpace(baseBundleUrl))
                return string.Empty;

            return CombineBundleUrl(baseBundleUrl, "font_" + prefix + bundleFileExtension);
        }

        private static string CombineBundleUrl(string baseUrl, string fileName)
        {
            var trimmedBaseUrl = baseUrl.Trim();
            return trimmedBaseUrl.EndsWith("/", StringComparison.Ordinal) ? trimmedBaseUrl + fileName : trimmedBaseUrl + "/" + fileName;
        }

        private static string FormatRequestError(UnityWebRequest request)
        {
            return string.IsNullOrWhiteSpace(request.error) ? "<none>" : request.error;
        }

        private static string DescribeFont(TMP_FontAsset font)
        {
            if (font == null)
                return "NULL";

            var mat = font.material;
            var atlas = font.atlasTexture;
            if (atlas == null && font.atlasTextures != null && font.atlasTextures.Length > 0)
                atlas = font.atlasTextures[0];

            string mainTexName = "NULL";
            if (mat != null)
            {
                try
                {
                    var mainTex = mat.GetTexture(ShaderUtilities.ID_MainTex);
                    mainTexName = mainTex != null ? mainTex.name : "NULL";
                }
                catch
                {
                    mainTexName = "ERROR";
                }
            }

            return $"{font.name} | mat={(mat != null ? mat.name : "NULL")} | atlas={(atlas != null ? atlas.name : "NULL")} | mainTex={mainTexName} | chars={font.characterTable?.Count ?? 0}";
        }
    }
}
