using System.Collections.Generic;

namespace com.notask.finelocalization.Scripts.Runtime.Utils
{
    public static class Constants
    {
        public const string LocalizationEditorUrl = "";
        public const string SheetResolverUrl = "https://script.google.com/macros/s/AKfycbycW2dsGZhc2xJh2Fs8yu9KUEqdM-ssOiK1AlES3crLqQa1lkDrI4mZgP7sJhmFlGAD/exec";
        public const string TableUrlPattern = "https://docs.google.com/spreadsheets/d/{0}";
        public const string ExampleTableId = "1L4qI2ZwKMcQrMySZbkftQ2w2Ebxu5HtRDfuxUDF8wYs";
        public static readonly Dictionary<string, int> ExampleSheets = new() { { "Template", 2130180146 } };
    }
}