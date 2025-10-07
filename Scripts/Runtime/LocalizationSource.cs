using System;
using System.Collections;
using System.Collections.Generic;
using FineLocalization.Utils;
using UnityEngine;

[Serializable]
public class LocalizationSource
{
    public string TableId;
    public List<Sheet> Sheets = new();
}
