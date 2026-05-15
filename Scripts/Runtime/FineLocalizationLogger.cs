using System;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public static class FineLocalizationLogger
    {
        public static bool Enabled
        {
            get
            {
                try
                {
                    return LocalizationSettings.Instance != null &&
                           LocalizationSettings.Instance.EnableLogs;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Log(string message)
        {
            if (Enabled)
                Debug.Log(message);
        }

        public static void Log(Func<string> messageFactory)
        {
            if (Enabled)
                Debug.Log(messageFactory());
        }

        public static void LogWarning(string message)
        {
            if (Enabled)
                Debug.LogWarning(message);
        }

        public static void LogWarning(Func<string> messageFactory)
        {
            if (Enabled)
                Debug.LogWarning(messageFactory());
        }

        public static void LogError(string message)
        {
            if (Enabled)
                Debug.LogError(message);
        }

        public static void LogError(Func<string> messageFactory)
        {
            if (Enabled)
                Debug.LogError(messageFactory());
        }
    }
}
