using System;
using System.Collections;
using System.Collections.Generic;
using FineLocalization.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace FineLocalization.Scripts.Runtime
{
    /// <summary>
    /// Configuração da fonte remota. Vive dentro do <see cref="RuntimeLocaleDownloader"/> — na v3.1
    /// isso era um segundo MonoBehaviour (<c>RemoteFontBundleLoader</c>) que o downloader tinha que
    /// procurar na cena e negociar ordem com ele.
    ///
    /// Não há mais lista de mapeamento idioma → fonte: quem responde isso é o
    /// <see cref="FontBundleManifest"/>, gerado pelo Bundle Builder a partir dos bundles que
    /// realmente existem.
    /// </summary>
    [Serializable]
    public class RemoteFontOptions
    {
        [Tooltip("URL base da pasta dos bundles. Ex: https://cdn.site.com/languages/")]
        public string baseBundleUrl;

        [Tooltip("Segmento do jogo na URL. Vazio = Product Name do Player Settings, minúsculo e sem espaços.")]
        public string gameId;

        [Tooltip("Fontes base do projeto que recebem a fonte remota como fallback prioritário. Coloque aqui a fonte principal da UI, não as fontes CJK.")]
        public List<TMP_FontAsset> mainFontAssets = new();

        [Tooltip("Fallback permanente de baixa prioridade para glifos soltos que a fonte principal não tem (₴, ₹). Nunca removido ao trocar idioma. Deixe vazio se a fonte principal já cobre.")]
        public List<TMP_FontAsset> coverageFallbackFontAssets = new();

        [Tooltip("TMP_Text reconstruídos por frame. Menor = menos travada em aparelho fraco; maior = troca de idioma mais rápida.")]
        [Min(1)]
        public int rebuildBatchSize = 24;

        [Tooltip("Prefixos extras a tratar como Latin — não baixam bundle. Use minúsculo.")]
        public List<string> extraLatinPrefixes = new();

        [Tooltip("Força bundle remoto mesmo para idiomas Latin. Use quando a fonte base não tem acentuação completa. Ex: vi, tr")]
        public List<string> forceRemoteFontPrefixes = new();

        [Header("Teste local (só Editor)")]
        [Tooltip("Carrega os bundles da pasta de saída do Bundle Builder em vez do CDN. Serve para " +
                 "testar uma fonte recém-assada sem subir nada.\n\n" +
                 "O efeito é compilado fora do build: num player este campo não faz nada, seja qual " +
                 "for o valor salvo na cena.")]
        public bool useLocalBundlesInEditor;

        [Tooltip("Pasta dos bundles, relativa à raiz do projeto (o nível acima de Assets/). " +
                 "Tem que ser a mesma do Output Folder do Bundle Builder.")]
        public string localBundleFolder = DefaultLocalBundleFolder;

        internal const string DefaultLocalBundleFolder = "AssetBundles/WebGL/Fonts";

        /// <summary>
        /// True quando o teste local está ligado <b>e</b> estamos no Editor. Fora do Editor é
        /// sempre false, por compilação — não há como um build sair apontando para arquivo local.
        /// </summary>
        public bool UsesLocalBundles
        {
            get
            {
#if UNITY_EDITOR
                return useLocalBundlesInEditor;
#else
                return false;
#endif
            }
        }

        /// <summary>True quando há de onde baixar: um CDN configurado ou o teste local ligado.</summary>
        public bool HasBundleSource => !string.IsNullOrWhiteSpace(baseBundleUrl) || UsesLocalBundles;

        /// <summary>Pasta local efetiva, caindo no default quando o campo está vazio.</summary>
        public string ResolveLocalBundleFolder()
        {
            return string.IsNullOrWhiteSpace(localBundleFolder)
                ? DefaultLocalBundleFolder
                : localBundleFolder.Trim();
        }

        /// <summary>Segmento por jogo, caindo no Product Name quando <see cref="gameId"/> está vazio.</summary>
        public string ResolveGameId()
        {
            return string.IsNullOrWhiteSpace(gameId) ? DefaultGameId() : gameId.Trim();
        }

        /// <summary>Product Name minúsculo e sem espaços — o default de <see cref="gameId"/>.</summary>
        public static string DefaultGameId()
        {
            var productName = Application.productName;
            return string.IsNullOrWhiteSpace(productName)
                ? string.Empty
                : productName.Trim().ToLowerInvariant().Replace(" ", "");
        }
    }

    /// <summary>
    /// Baixa fontes por idioma como AssetBundle e as registra como fallback do TextMeshPro.
    ///
    /// Só idiomas fora do script Latin passam pela rede — as fontes embutidas do projeto já
    /// cobrem Latin e Latin Extended. Isso mantém o build pequeno: os glifos de CJK, tailandês,
    /// árabe e afins só chegam ao jogador que realmente escolhe esses idiomas.
    ///
    /// Não é MonoBehaviour: quem hospeda as coroutines e o ciclo de vida é o
    /// <see cref="RuntimeLocaleDownloader"/>. O trabalho pesado fica em
    /// <see cref="TmpFontRepair"/> (material e métricas SDF) e <see cref="TmpFallbackRegistry"/>
    /// (tabelas de fallback).
    /// </summary>
    internal sealed class RemoteFontInstaller
    {
        private readonly RemoteFontOptions _options;

        private readonly Dictionary<string, TMP_FontAsset> _fonts = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);

        // Só existe para segurar referências: sem isso a Unity descarrega o material e o atlas
        // vindos do bundle assim que ele é fechado, e a fonte remota vira quadrados vazios.
        private readonly HashSet<Object> _keepAlive = new();

        private Func<TMP_FontAsset, bool> _isManagedFont;
        private bool _hooksInstalled;

        internal RemoteFontInstaller(RemoteFontOptions options)
        {
            _options = options ?? new RemoteFontOptions();
        }

        /// <summary>True quando a última fonte processada ficou utilizável.</summary>
        internal bool Ready { get; private set; }

        /// <summary>Idioma da última fonte processada.</summary>
        internal string LastLanguage { get; private set; }

        /// <summary>True quando o Bundle Builder já declarou algum bundle.</summary>
        internal bool HasAnyBundle
        {
            get
            {
                var manifest = FontBundleManifest.Load();
                return manifest != null && manifest.HasAnyBundle;
            }
        }

        /// <summary>
        /// Instala os hooks globais do TMP. São estáticos por natureza (o TMP resolve fallback
        /// globalmente), então só um dono por vez faz sentido.
        /// </summary>
        internal void InstallHooks()
        {
            _isManagedFont ??= IsManagedFont;

            TmpFontRepair.MaterialTemplates = _options.mainFontAssets;
            TmpFontRepair.IsRemoteFont = _isManagedFont;
            TmpFallbackRegistry.IsManagedRemote = _isManagedFont;
            TmpFallbackRegistry.InstallCoverage(_options.coverageFallbackFontAssets, _options.mainFontAssets);

            _hooksInstalled = true;
        }

        /// <summary>
        /// Desinstala os hooks, mas <b>só se ainda somos o dono</b>. Sem essa checagem, um
        /// downloader saindo de cena arrancaria os hooks que outro acabou de instalar durante
        /// uma troca de cena.
        /// </summary>
        internal void UninstallHooks()
        {
            if (!_hooksInstalled)
                return;

            _hooksInstalled = false;

            if (ReferenceEquals(TmpFontRepair.IsRemoteFont, _isManagedFont))
                TmpFontRepair.IsRemoteFont = _ => false;

            if (ReferenceEquals(TmpFallbackRegistry.IsManagedRemote, _isManagedFont))
                TmpFallbackRegistry.IsManagedRemote = _ => false;

            if (ReferenceEquals(TmpFontRepair.MaterialTemplates, _options.mainFontAssets))
                TmpFontRepair.MaterialTemplates = null;
        }

        // ------------------------------------------------------------------ API

        /// <summary>
        /// True quando o idioma precisa de uma fonte que o projeto não embute — ou seja, não é
        /// script Latin (considerando as listas de override do inspector).
        /// </summary>
        internal bool NeedsRemoteFont(string language)
        {
            return !string.IsNullOrEmpty(LanguageCode.Normalize(language)) &&
                   !LanguageCode.IsLatinScript(language, _options.extraLatinPrefixes, _options.forceRemoteFontPrefixes);
        }

        /// <summary>True quando existe um bundle declarado no manifesto capaz de atender o idioma.</summary>
        internal bool CanServeLanguage(string language)
        {
            return NeedsRemoteFont(language) && FindEntry(language) != null;
        }

        /// <summary>True quando a fonte do idioma já está baixada e renderizável.</summary>
        internal bool IsServing(string language)
        {
            var entry = FindEntry(language);
            return entry != null &&
                   _fonts.TryGetValue(KeyOf(entry), out var font) &&
                   TmpFontRepair.IsUsable(font);
        }

        /// <summary>
        /// Garante que a fonte do idioma esteja baixada e instalada como fallback.
        /// O callback recebe false quando o idioma precisava de fonte remota e ela não pôde
        /// ser obtida — nesse caso quem chamou deve cair para um idioma Latin.
        ///
        /// Não reconstrói os textos: quem chama deve trocar as strings primeiro e só então
        /// chamar <see cref="RebuildCurrentTexts"/>, para o mesh ser refeito uma única vez.
        /// </summary>
        internal IEnumerator EnsureFontForLanguage(string language, Action<bool> onComplete = null)
        {
            var normalized = LanguageCode.Normalize(language);

            if (string.IsNullOrEmpty(normalized))
            {
                onComplete?.Invoke(false);
                yield break;
            }

            if (!NeedsRemoteFont(normalized))
            {
                // Idioma Latin: as fontes do projeto bastam. Tira a fonte remota do idioma
                // anterior para ela não continuar resolvendo glifos indevidamente.
                TmpFallbackRegistry.RemoveManagedRemote(null, _options.mainFontAssets, FindSceneTexts());
                Complete(normalized, true, onComplete, notify: false);
                yield break;
            }

            var entry = FindEntry(normalized);
            if (entry == null)
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] Nenhum bundle de fonte para '{normalized}'. " +
                          DescribeManifest()
                );
                Complete(normalized, false, onComplete, notify: false);
                yield break;
            }

            var key = KeyOf(entry);

            if (_fonts.TryGetValue(key, out var cached))
            {
                if (TmpFallbackRegistry.InstallRemote(cached, _options.mainFontAssets, FindSceneTexts()))
                {
                    Complete(normalized, true, onComplete, notify: false);
                    yield break;
                }

                // A fonte em cache ficou inutilizável (asset descarregado entre cenas). Descarta
                // para que a próxima tentativa volte a baixar em vez de falhar para sempre.
                _fonts.Remove(key);
            }

            if (!_loading.Add(key))
            {
                while (_loading.Contains(key))
                    yield return null;

                Complete(normalized, _fonts.ContainsKey(key), onComplete, notify: false);
                yield break;
            }

            var success = false;
            yield return DownloadAndInstall(entry, ok => success = ok);
            _loading.Remove(key);

            Complete(normalized, success, onComplete, notify: true);
        }

        /// <summary>Reconstrói o mesh de todos os TMP_Text ativos, em lotes, sem travar o frame.</summary>
        internal IEnumerator RebuildCurrentTexts()
        {
            var texts = FindSceneTexts();

            // O TMP percorre a lista global de fallbacks para todo caractere ausente na fonte
            // principal. Uma entrada quebrada ali derruba o rebuild inteiro — limpa antes.
            TmpFallbackRegistry.SanitizeGlobals();
            TmpFontRepair.ClearFallbackMaterialCache();

            var remote = CurrentRemoteFont();
            var batch = Mathf.Max(1, _options.rebuildBatchSize);
            var rebuilt = 0;
            var skipped = 0;

            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null || text.font == null || !text.gameObject.activeInHierarchy)
                {
                    skipped++;
                    continue;
                }

                if (!TmpFallbackRegistry.ValidateTree(text.font))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    text.SetAllDirty();
                    text.UpdateMeshPadding();
                    text.havePropertiesChanged = true;
                    text.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: true);

                    TmpFontRepair.NormalizeTextMaterials(text, remote);

                    text.SetMaterialDirty();
                    text.ForceMeshUpdate(ignoreActiveState: false, forceTextReparsing: false);
                    rebuilt++;
                }
                catch (Exception ex)
                {
                    // Um TMP_Text quebrado (material nulo, objeto destruído no meio do frame)
                    // nunca pode abortar o rebuild dos outros.
                    skipped++;
                    FineLocalizationLogger.LogWarning(
                        () => $"[FineLocalization] Rebuild ignorado em '{text.name}': {ex.GetType().Name}: {ex.Message}"
                    );
                }

                if (rebuilt > 0 && rebuilt % batch == 0)
                    yield return null;
            }

            FineLocalizationLogger.Log(() => $"[FineLocalization] Rebuild de textos: {rebuilt} reconstruídos, {skipped} ignorados.");
        }

        // -------------------------------------------------------------- Download

        private IEnumerator DownloadAndInstall(FontBundleManifest.Entry entry, Action<bool> onComplete)
        {
            var key = KeyOf(entry);
            var url = BuildBundleUrl(entry);

            if (string.IsNullOrWhiteSpace(url))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] Base Bundle URL vazia — não é possível baixar a fonte de '{key}'."
                );
                onComplete(false);
                yield break;
            }

            FineLocalizationLogger.Log(() => $"[FineLocalization] Baixando fonte de '{key}': {url}");

            using var request = UnityWebRequestAssetBundle.GetAssetBundle(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[FineLocalization] Falha ao baixar a fonte de '{key}'. HTTP {request.responseCode}, erro '{request.error}', URL {url}");
                onComplete(false);
                yield break;
            }

            var bundle = DownloadHandlerAssetBundle.GetContent(request);
            if (bundle == null)
            {
                Debug.LogError($"[FineLocalization] O bundle de fonte de '{key}' baixou mas não pôde ser aberto. URL {url}");
                onComplete(false);
                yield break;
            }

            var fontAssetName = entry.fontAssetName;
            TMP_FontAsset font = null;

            if (!string.IsNullOrWhiteSpace(fontAssetName))
            {
                var byName = bundle.LoadAssetAsync<TMP_FontAsset>(fontAssetName);
                yield return byName;
                font = byName.asset as TMP_FontAsset;
            }

            // Carrega tudo mesmo já tendo a fonte: o material e o atlas vêm como assets
            // separados e precisam ficar vivos enquanto a fonte estiver instalada.
            var all = bundle.LoadAllAssetsAsync();
            yield return all;
            TmpFontRepair.KeepAlive(all.allAssets, _keepAlive);

            font ??= FirstFontAsset(all.allAssets, fontAssetName);
            var bundleMaterial = BestMaterial(all.allAssets, font);

            if (font == null)
            {
                Debug.LogError(
                    $"[FineLocalization] Nenhum TMP_FontAsset no bundle de '{key}'. Esperado '{fontAssetName}'. " +
                    $"Assets no bundle: {string.Join(", ", bundle.GetAllAssetNames())}"
                );
                bundle.Unload(false);
                onComplete(false);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(fontAssetName) &&
                !font.name.Equals(fontAssetName, StringComparison.OrdinalIgnoreCase))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] Bundle de '{key}' traz '{font.name}', mas o manifesto diz '{fontAssetName}'. Usando o que veio."
                );
            }

            // Registra antes de reparar: TmpFontRepair.IsRemoteFont consulta _fonts para saber
            // que pode recriar o material e reescrever as métricas SDF desta fonte.
            _fonts[key] = font;

            TmpFontRepair.Repair(font, bundleMaterial);
            TmpFontRepair.ReadDefinition(font);
            TmpFontRepair.KeepAtlasesAlive(font);
            font.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            var installed = TmpFallbackRegistry.InstallRemote(font, _options.mainFontAssets, FindSceneTexts());
            if (!installed)
            {
                _fonts.Remove(key);
                FineLocalizationLogger.LogWarning(() => $"[FineLocalization] Fonte de '{key}' inutilizável após reparo: {TmpFontRepair.Describe(font)}");
            }
            else
            {
                FineLocalizationLogger.Log(() => $"[FineLocalization] Fonte de '{key}' instalada: {TmpFontRepair.Describe(font)}");
            }

            // Unload(false) libera o container do bundle mantendo os assets já carregados.
            bundle.Unload(false);
            onComplete(installed);
        }

        // ------------------------------------------------------------- Resolução

        /// <summary>
        /// Entrada do manifesto que atende o idioma, ou null. Usa a mesma escada de candidatos que
        /// escolhe a coluna do CSV, então um host que manda "cn" acha o bundle de "zh-cn" e um
        /// pedido de "zh-hk" prefere o bundle Tradicional ao Simplificado.
        /// </summary>
        private FontBundleManifest.Entry FindEntry(string language)
        {
            var manifest = FontBundleManifest.Load();
            return manifest?.Find(language);
        }

        /// <summary>
        /// URL do bundle. O último segmento é o <b>nome do arquivo que o Bundle Builder gerou</b>,
        /// não uma remontagem de prefixo + extensão — foi assim que a v3.1 conseguia pedir
        /// <c>font_x.ft</c> para um arquivo chamado <c>x.ft</c> e tomar 404 só em Play.
        /// </summary>
        private string BuildBundleUrl(FontBundleManifest.Entry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.bundleFileName))
                return string.Empty;

            var fileName = entry.bundleFileName.Trim();

#if UNITY_EDITOR
            if (_options.useLocalBundlesInEditor)
                return BuildLocalBundleUrl(fileName);
#endif

            if (string.IsNullOrWhiteSpace(_options.baseBundleUrl))
                return string.Empty;

            var url = _options.baseBundleUrl.Trim();

            var segment = _options.ResolveGameId();
            if (!string.IsNullOrEmpty(segment))
                url = Combine(url, segment);

            return Combine(url, fileName);
        }

#if UNITY_EDITOR
        /// <summary>
        /// URL <c>file://</c> para a pasta de saída do Bundle Builder, que fica <b>fora</b> de
        /// Assets/ e portanto não é asset do projeto. Mesmo caminho de código do CDN — o
        /// UnityWebRequest resolve file:// igual, então o download local exercita exatamente o
        /// mesmo fluxo de reparo e instalação da fonte.
        ///
        /// AssetBundle é específico de plataforma: o Editor só abre bundle da plataforma ativa em
        /// Build Settings. Como estes são construídos para WebGL, o teste local exige o target
        /// WebGL ativo — o inspector avisa quando não está.
        /// </summary>
        private string BuildLocalBundleUrl(string fileName)
        {
            var projectRoot = System.IO.Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
                return string.Empty;

            var full = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(projectRoot.FullName, _options.ResolveLocalBundleFolder(), fileName)
            );

            if (!System.IO.File.Exists(full))
            {
                FineLocalizationLogger.LogWarning(
                    () => $"[FineLocalization] Teste local ligado, mas o bundle não existe: {full}. " +
                          "Rode Build Bundles ou desligue Use Local Bundles In Editor."
                );
                return string.Empty;
            }

            FineLocalizationLogger.Log(() => $"[FineLocalization] Teste local: lendo bundle de {full}");
            return "file:///" + full.Replace('\\', '/');
        }
#endif

        private static string Combine(string baseUrl, string segment)
        {
            return baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl + segment : baseUrl + "/" + segment;
        }

        /// <summary>Chave de cache de uma entrada: o idioma que ela atende, normalizado.</summary>
        private static string KeyOf(FontBundleManifest.Entry entry)
        {
            return LanguageCode.Normalize(entry.language);
        }

        /// <summary>Fonte remota do idioma atualmente aplicado, ou null.</summary>
        private TMP_FontAsset CurrentRemoteFont()
        {
            var entry = FindEntry(LocalizationManager.Language);
            return entry != null && _fonts.TryGetValue(KeyOf(entry), out var font) ? font : null;
        }

        private bool IsManagedFont(TMP_FontAsset font)
        {
            if (font == null)
                return false;

            foreach (var loaded in _fonts.Values)
            {
                if (loaded == font)
                    return true;
            }

            var manifest = FontBundleManifest.Load();
            if (manifest?.entries == null)
                return false;

            for (int i = 0; i < manifest.entries.Count; i++)
            {
                var configured = manifest.entries[i]?.fontAssetName;
                if (!string.IsNullOrWhiteSpace(configured) &&
                    font.name.Equals(configured.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private string DescribeManifest()
        {
            var manifest = FontBundleManifest.Load();
            if (manifest == null)
                return "Nenhum manifesto encontrado — rode Build Bundles e commite " +
                       $"Resources/{FontBundleManifest.AssetName}.asset.";

            if (!manifest.HasAnyBundle)
                return "O manifesto está vazio — rode Build Bundles.";

            var languages = new List<string>(manifest.entries.Count);
            for (int i = 0; i < manifest.entries.Count; i++)
            {
                if (manifest.entries[i] != null)
                    languages.Add(manifest.entries[i].language);
            }

            return $"Bundles no manifesto: [{string.Join(", ", languages)}].";
        }

        private void Complete(string language, bool success, Action<bool> onComplete, bool notify)
        {
            LastLanguage = language;
            Ready = success;

            if (notify)
                Localization.RaiseFontReady(language, success);

            onComplete?.Invoke(success);
        }

        private static TMP_Text[] FindSceneTexts()
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<TMP_Text>(true);
#endif
        }

        private static TMP_FontAsset FirstFontAsset(Object[] assets, string preferredName)
        {
            TMP_FontAsset first = null;

            for (int i = 0; assets != null && i < assets.Length; i++)
            {
                if (assets[i] is not TMP_FontAsset font)
                    continue;

                first ??= font;

                if (!string.IsNullOrWhiteSpace(preferredName) &&
                    font.name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                    return font;
            }

            return first;
        }

        private static Material BestMaterial(Object[] assets, TMP_FontAsset font)
        {
            Material first = null;
            Material named = null;

            for (int i = 0; assets != null && i < assets.Length; i++)
            {
                if (assets[i] is not Material material)
                    continue;

                first ??= material;

                if (font != null && material.name.IndexOf(font.name, StringComparison.OrdinalIgnoreCase) >= 0)
                    named = material;
            }

            return named != null ? named : first;
        }
    }
}
