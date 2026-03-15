using TMPro;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public class LocaleComponent : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private TMP_Text text;
        [SerializeField] private bool shouldAlign;

        private void Awake()
        {
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