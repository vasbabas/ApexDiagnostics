using System;
using System.Windows;

namespace ApexDiagnostics.Helpers
{
    public static class LanguageManager
    {
        public static string CurrentLanguage { get; private set; } = "EN";
        public static event Action? LanguageChanged;

        public static void SetLanguage(string langCode)
        {
            var app = Application.Current;
            if (app == null) return;

            CurrentLanguage = langCode.ToUpper();
            LanguageChanged?.Invoke();

            // Find and remove any custom language dictionaries (but keep en.xaml as base fallback)
            var toRemove = new System.Collections.Generic.List<ResourceDictionary>();
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                if (dict.Source != null && dict.Source.OriginalString.Contains("Resources/Strings.") && 
                    !dict.Source.OriginalString.Contains("Resources/Strings.en.xaml"))
                {
                    toRemove.Add(dict);
                }
            }

            foreach (var dict in toRemove)
            {
                app.Resources.MergedDictionaries.Remove(dict);
            }

            // Load new resource dictionary if it's not English
            if (CurrentLanguage != "EN")
            {
                try
                {
                    var newDict = new ResourceDictionary();
                    string uriString = $"pack://application:,,,/ApexDiagnostics;component/Resources/Strings.{langCode.ToLower()}.xaml";
                    newDict.Source = new Uri(uriString, UriKind.Absolute);

                    app.Resources.MergedDictionaries.Add(newDict);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load language {langCode}: {ex.Message}");
                }
            }
        }
    }
}
