using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace FineLocalization.Runtime
{
    public class FiltersGroupLocale : MonoBehaviour
    {
        [SerializeField] private TMP_Text dateText;
        [SerializeField] private TMP_Text betText;
        [SerializeField] private TMP_Text oddText;
        [SerializeField] private TMP_Text cashoutText;


        private void Start()
        {
            dateText.SetText(LocalizationManager.Localize("date"));
            betText.SetText($"{LocalizationManager.Localize("bet")},{CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol}");
            oddText.SetText(LocalizationManager.Localize("multiplier"));
            cashoutText.SetText($"{LocalizationManager.Localize("collect")},{CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol}");
        }
    }
}
