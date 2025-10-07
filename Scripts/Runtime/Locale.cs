using System.Collections.Generic;
using UnityEngine;

namespace FineLocalization.Runtime
{
    [CreateAssetMenu(fileName = "new Language", menuName = "Localization/Locale", order = 0)]
    public class Locale : ScriptableObject
    {
        [Header("Locale Texts")]
        [SerializeField] private List<TextKeyValue> texts;
        
        public string this[string key]
        {
            get
            {
                foreach (var text in texts)
                {
                    if (text.key == key) return text.value;
                }
        
                return string.Empty;
            }
        }

        public void SetTexts(List<TextKeyValue> list) => texts = list;
    }
    
    [System.Serializable]
    public struct TextKeyValue
    {
        public string key;
        [TextArea(10, 100)] public string value;
    }
}