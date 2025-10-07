using TMPro;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public class LocaleComponent : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private TMP_Text text;

        private void Awake()
        {
            SetText();
            LocalizationManager.OnLocalizationChanged += SetText;
        }

        private void SetText()
        {
            if (!text) text = GetComponent<TMP_Text>();
            text.SetText(LocalizationManager.Localize(key));
        }
        
        public void ChangeKey(string newKey)
        {
            key = newKey;
            SetText();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!text) text = GetComponent<TMP_Text>();
            //text.SetText(string.Empty);
        }
#endif
    }
}