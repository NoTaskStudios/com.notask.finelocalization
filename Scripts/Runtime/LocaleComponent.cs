using TMPro;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public class LocaleComponent : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private TMP_Text text;
        [SerializeField] private bool shouldAlign;
        public bool localizeOnEnable;

        private void Awake()
        {
            SetText();
            LocalizationManager.OnLocalizationChanged += SetText;
        }
        private void OnEnable()
        {
            if (localizeOnEnable)
                SetText();
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

            // Compare por raiz: era Equals("th-th") exato e case-sensitive, então "th",
            // "th-TH" e "TH-TH" caíam no alinhamento errado.
            text.alignment = LanguageCode.IsSameOrRoot(LocalizationManager.Language, "th")
                ? TextAlignmentOptions.Left
                : TextAlignmentOptions.Justified;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!text) text = GetComponent<TMP_Text>();
        }
#endif
    }
}