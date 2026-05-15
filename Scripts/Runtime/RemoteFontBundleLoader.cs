using System;
using System.Collections;
using System.Collections.Generic;
using FineLocalization.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class RemoteFontBundleLoader : MonoBehaviour
{
    [Serializable]
    private class RemoteFontBundleConfig
    {
        [Tooltip("Ex: zh, ja, ko, th")]
        public string languagePrefix;

        [Tooltip("Opcional. Se vazio, usa Base Bundle Url + languagePrefix.")]
        public string bundleUrlOverride;

        [Tooltip("Nome exato do TMP_FontAsset dentro do bundle")]
        public string fontAssetName;
    }

    [Header("Remote Font Bundles")]
    [Tooltip("URL base dos bundles. Ex: https://cdn.site.com/fonts/ ou https://cdn.site.com/fonts/font_")]
    [SerializeField] private string baseBundleUrl;

    [Tooltip("Extensão adicionada depois do prefixo quando usar a URL base. Ex: .bundle")]
    [SerializeField] private string bundleFileExtension = ".bundle";

    [SerializeField] private List<RemoteFontBundleConfig> bundles = new();

    [Header("Fallback Target")]
    [Tooltip("Se marcado, adiciona nos fallbacks globais do TMP Settings.")]
    [SerializeField] private bool addToGlobalTmpFallbacks = true;

    [Tooltip("Opcional: fonte principal do projeto para receber o fallback diretamente.")]
    [SerializeField] private TMP_FontAsset mainFontAsset;

    [Header("Localization Integration")]
    [Tooltip("Carrega o bundle de fonte automaticamente quando LocalizationManager.Language mudar.")]
    [SerializeField] private bool loadOnLocalizationChanged = true;

    [Tooltip("Tenta carregar a fonte do idioma atual quando este componente for habilitado.")]
    [SerializeField] private bool loadCurrentLanguageOnEnable = true;

    private readonly HashSet<string> _loadedLanguages = new();
    private readonly HashSet<string> _loadingLanguages = new();

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

    public IEnumerator EnsureFontForLanguage(
        string language,
        Action<bool> onComplete = null
    )
    {
        FineLocalizationLogger.Log(() => $"[RemoteFontBundleLoader] Solicitado idioma: {language}");

        if (string.IsNullOrWhiteSpace(language))
        {
            FineLocalizationLogger.LogWarning("[RemoteFontBundleLoader] Idioma vazio. Nenhum bundle será carregado.");
            onComplete?.Invoke(true);
            yield break;
        }

        var normalizedLanguage = language.Trim().ToLowerInvariant();
        var config = FindConfig(normalizedLanguage);

        if (config == null)
        {
            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] Nenhum bundle configurado para '{normalizedLanguage}'."
            );

            onComplete?.Invoke(true);
            yield break;
        }

        var prefix = config.languagePrefix.Trim().ToLowerInvariant();
        var bundleUrl = GetBundleUrl(config, prefix);

        FineLocalizationLogger.Log(
            () => $"[RemoteFontBundleLoader] Config encontrada. " +
                  $"Idioma: {normalizedLanguage} | " +
                  $"Prefix: {prefix} | " +
                  $"URL: {bundleUrl} | " +
                  $"FontAsset: {config.fontAssetName}"
        );

        if (_loadedLanguages.Contains(prefix))
        {
            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] Fonte de '{prefix}' já estava carregada."
            );

            onComplete?.Invoke(true);
            yield break;
        }

        if (!_loadingLanguages.Add(prefix))
        {
            FineLocalizationLogger.Log(
                () => $"[RemoteFontBundleLoader] Fonte de '{prefix}' já está carregando."
            );

            onComplete?.Invoke(true);
            yield break;
        }

        FineLocalizationLogger.Log(
            () => $"[RemoteFontBundleLoader] Iniciando download do AssetBundle: {bundleUrl}"
        );

        if (string.IsNullOrWhiteSpace(bundleUrl))
        {
            FineLocalizationLogger.LogWarning(
                () => $"[RemoteFontBundleLoader] URL do bundle vazia para '{prefix}'."
            );

            _loadingLanguages.Remove(prefix);
            onComplete?.Invoke(false);
            yield break;
        }

        using var request = UnityWebRequestAssetBundle.GetAssetBundle(bundleUrl);

        yield return request.SendWebRequest();

        FineLocalizationLogger.Log(
            () => $"[RemoteFontBundleLoader] Requisição finalizada. " +
                  $"Result: {request.result} | Erro: {request.error}"
        );

        if (request.result != UnityWebRequest.Result.Success)
        {
            FineLocalizationLogger.LogWarning(
                () => $"[RemoteFontBundleLoader] Falha ao baixar bundle de fonte '{language}'. " +
                      $"Erro: {request.error}"
            );

            _loadingLanguages.Remove(prefix);
            onComplete?.Invoke(false);
            yield break;
        }

        var bundle = DownloadHandlerAssetBundle.GetContent(request);

        if (bundle == null)
        {
            FineLocalizationLogger.LogWarning(
                () => $"[RemoteFontBundleLoader] Bundle retornou null para '{language}'."
            );

            _loadingLanguages.Remove(prefix);
            onComplete?.Invoke(false);
            yield break;
        }

        FineLocalizationLogger.Log(
            () => $"[RemoteFontBundleLoader] AssetBundle carregado com sucesso para '{language}'."
        );

        var fontLoadRequest =
            bundle.LoadAssetAsync<TMP_FontAsset>(config.fontAssetName);

        yield return fontLoadRequest;

        var fontAsset = fontLoadRequest.asset as TMP_FontAsset;

        if (fontAsset == null)
        {
            FineLocalizationLogger.LogWarning(
                () => $"[RemoteFontBundleLoader] TMP_FontAsset '{config.fontAssetName}' não encontrado no bundle."
            );

            bundle.Unload(false);
            _loadingLanguages.Remove(prefix);
            onComplete?.Invoke(false);
            yield break;
        }

        FineLocalizationLogger.Log(
            () => $"[RemoteFontBundleLoader] TMP_FontAsset encontrado: {fontAsset.name} | " +
                  $"Character Count: {fontAsset.characterTable?.Count ?? 0} | " +
                  $"Glyph Count: {fontAsset.glyphTable?.Count ?? 0}"
        );

        RegisterFallback(fontAsset);

        FineLocalizationLogger.Log(
            () => $"[RemoteFontBundleLoader] Fonte registrada como fallback: {fontAsset.name}"
        );

        _loadedLanguages.Add(prefix);
        _loadingLanguages.Remove(prefix);

        bundle.Unload(false);

        onComplete?.Invoke(true);
    }

    private RemoteFontBundleConfig FindConfig(string language)
    {
        foreach (var config in bundles)
        {
            if (string.IsNullOrWhiteSpace(config.languagePrefix))
                continue;

            var prefix = config.languagePrefix.Trim().ToLowerInvariant();

            if (language == prefix || language.StartsWith(prefix + "-"))
                return config;
        }

        return null;
    }

    private string GetBundleUrl(RemoteFontBundleConfig config, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(config.bundleUrlOverride))
            return config.bundleUrlOverride.Trim();

        if (string.IsNullOrWhiteSpace(baseBundleUrl))
            return string.Empty;

        return baseBundleUrl.Trim() + prefix + bundleFileExtension;
    }

    private void RegisterFallback(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null)
            return;

        if (addToGlobalTmpFallbacks)
        {
            if (!TMP_Settings.fallbackFontAssets.Contains(fontAsset))
                TMP_Settings.fallbackFontAssets.Add(fontAsset);
        }

        if (mainFontAsset != null)
        {
            mainFontAsset.fallbackFontAssetTable ??= new List<TMP_FontAsset>();

            if (!mainFontAsset.fallbackFontAssetTable.Contains(fontAsset))
                mainFontAsset.fallbackFontAssetTable.Add(fontAsset);
        }
    }
}
