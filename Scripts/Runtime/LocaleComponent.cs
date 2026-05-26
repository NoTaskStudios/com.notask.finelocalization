using TMPro;
using UnityEngine;

namespace FineLocalization.Runtime
{
    /// <summary>
    /// Replaces the TMP_Text content with the localized value for <see cref="key"/>.
    /// Runs at execution order -100 to make sure the localized string is set BEFORE
    /// any Canvas/LayoutGroup measures preferredHeight — otherwise TMP can fire a
    /// "missing glyph" warning for whatever placeholder text was saved in the prefab.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class LocaleComponent : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private TMP_Text text;
        [SerializeField] private bool shouldAlign;

        [Tooltip("If true, clears the TMP_Text content immediately on Awake (before localization runs). " +
                 "Use this if your prefab is saved with foreign-language placeholder text and you want to " +
                 "avoid TMP warnings during the 1-frame transition window.")]
        [SerializeField] private bool clearPlaceholderOnAwake = true;

        private void Awake()
        {
            if (!text) text = GetComponent<TMP_Text>();

            // Wipe the prefab's stored text BEFORE the first layout measurement so TMP doesn't
            // try to render any foreign glyphs that may have been saved as designer placeholder.
            if (clearPlaceholderOnAwake && text != null)
                text.SetText(string.Empty);

            SetText();
            LocalizationManager.OnLocalizationChanged += SetText;
        }

        private void SetText()
        {
            if (!text) text = GetComponent<TMP_Text>();

            HandleJustified();
            
            text.SetText(LocalizationManager.Localize(key));
        }

        private void OnDestroy()
        {
            LocalizationManager.OnLocalizationChanged -= SetText;
        }
        
        public void ChangeKey(string newKey)
        {
            key = newKey;
            SetText();
        }
        
        private void HandleJustified()
        {
            if (!shouldAlign) return;

            var languageThai = "th-th";
            
            text.alignment = LocalizationManager.Language.Equals(languageThai) ?
                             text.alignment = TextAlignmentOptions.Left : 
                             text.alignment = TextAlignmentOptions.Justified;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!text) text = GetComponent<TMP_Text>();
        }
#endif
    }
}