using System;

namespace FineLocalization.Runtime
{
    public static class LocalizationStringExtensions
    {
        public static (string title, string description) ParseToTitleDescription(this string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return (string.Empty, string.Empty);

            var normalized = value.Trim();

            if (TryParseLabeled(normalized, out var labeled))
                return labeled;

            var separatorIndex = normalized.IndexOf('|');

            if (separatorIndex < 0)
            {
                separatorIndex = normalized.IndexOf("\r\n", StringComparison.Ordinal);
                if (separatorIndex >= 0)
                    return Split(normalized, separatorIndex, 2);
            }

            if (separatorIndex < 0)
            {
                separatorIndex = normalized.IndexOf('\n');
                if (separatorIndex >= 0)
                    return Split(normalized, separatorIndex, 1);
            }

            if (separatorIndex < 0)
                return (string.Empty, normalized);

            return Split(normalized, separatorIndex, 1);
        }

        private static bool TryParseLabeled(string value, out (string title, string description) result)
        {
            result = (string.Empty, string.Empty);

            const string titleLabel = "title:";
            const string descriptionLabel = "description:";

            var titleIndex = value.IndexOf(titleLabel, StringComparison.OrdinalIgnoreCase);
            var descriptionIndex = value.IndexOf(descriptionLabel, StringComparison.OrdinalIgnoreCase);

            // Sem nenhum rótulo: não é o formato rotulado.
            if (titleIndex < 0 && descriptionIndex < 0)
                return false;

            string title;
            if (titleIndex >= 0 && (descriptionIndex < 0 || descriptionIndex > titleIndex))
            {
                var titleStart = titleIndex + titleLabel.Length;
                var titleEnd = descriptionIndex > titleStart ? descriptionIndex : value.Length;
                title = value.Substring(titleStart, titleEnd - titleStart).Trim();
            }
            else
            {
                title = string.Empty;
            }

            string description;
            if (descriptionIndex >= 0)
            {
                var descriptionStart = descriptionIndex + descriptionLabel.Length;
                description = value.Substring(descriptionStart).Trim();
            }
            else
            {
                description = string.Empty;
            }

            result = (title, description);
            return true;
        }

        private static (string title, string description) Split(string value, int separatorIndex, int separatorLength)
        {
            var title = value.Substring(0, separatorIndex).Trim();
            var descriptionStart = separatorIndex + separatorLength;
            var description = descriptionStart < value.Length
                ? value.Substring(descriptionStart).Trim()
                : string.Empty;

            return (title, description);
        }
    }
}
