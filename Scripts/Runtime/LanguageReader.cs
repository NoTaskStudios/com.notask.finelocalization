namespace FineLocalization.Runtime
{
    /// <summary>
    /// Traduz um código pedido (pelo jogo, pelo host, pela URL) na coluna de idioma que existe de
    /// verdade nas planilhas carregadas. É o único funil dessa decisão: passam por aqui
    /// <see cref="LocalizationManager.SetLanguage"/>, <c>ReloadAll</c> e <c>Initialize</c>.
    /// </summary>
    public static class LanguageReader
    {
        /// <summary>
        /// Coluna de idioma que atende <paramref name="language"/>. Nunca devolve null nem vazio:
        /// sem candidato compatível, devolve o próprio código pedido e deixa
        /// <see cref="LocalizationManager"/> aplicar o fallback.
        /// </summary>
        public static string GetLanguageKey(string language)
        {
            var languages = LocalizationManager.Dictionary;
            var requested = string.IsNullOrWhiteSpace(language)
                ? LocalizationManager.Language
                : LanguageCode.Normalize(language);

            if (languages == null || languages.Count == 0)
                return requested;

            // Caminho rápido: a coluna existe exatamente como foi pedida. Idêntico à v3.0.0.
            if (languages.ContainsKey(requested))
                return requested;

            // Escada de candidatos: conserta região no lugar de idioma ("cn" → "zh-cn"), código
            // depreciado ("iw" → "he") e escolhe a região certa dentro do mesmo script
            // ("zh-hk" → "zh-tw", nunca "zh-cn"). O resultado não depende da ordem das colunas.
            var resolved = LanguageCode.SelectBest(requested, languages);
            if (!string.IsNullOrEmpty(resolved))
                return resolved;

            FineLocalizationLogger.LogWarning(
                () => $"[FineLocalization] language key not found: {requested}. " +
                      LanguageCode.Explain(requested, languages.Keys));

            return requested;
        }
    }
}
