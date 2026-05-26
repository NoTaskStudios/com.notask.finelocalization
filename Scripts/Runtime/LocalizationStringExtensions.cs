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
