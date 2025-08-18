using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public class MultiLocale : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private List<TMP_Text> texts;

        private void Awake()
        {
            foreach (var txt in texts) txt.SetText(LocalizationManager.Localize(key));
        }
    }
}