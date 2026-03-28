using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Newtonsoft.Json;

namespace KaabliKataloog.Services
{
    public class ThemeManager
    {
        private const string ConfigFilePath = @"C:\ProgramData\RK Tools\KaabliKataloog\config.json";
        private readonly Window _window;
        private bool _isDarkMode = true;
        public event EventHandler ThemeChanged;
        public bool IsDarkMode => _isDarkMode;

        public ThemeManager(Window window)
        {
            _window = window;
            LoadThemeState();
            ApplyTheme();
        }
        public void ToggleTheme()
        {
            _isDarkMode = !_isDarkMode;
            ApplyTheme();
            SaveThemeState();

            ThemeChanged?.Invoke(this, EventArgs.Empty); // ✅ Notify subscribers (like PanelInfo)
        }
        public void ApplyTheme()
        {
            var assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
            var themeUri = _isDarkMode
                ? $"pack://application:,,,/{assemblyName};component/UI/Themes/DarkTheme.xaml"
                : $"pack://application:,,,/{assemblyName};component/UI/Themes/LightTheme.xaml";

            try
            {
                var resourceDict = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Absolute) };
                _window.Resources.MergedDictionaries.Clear();
                _window.Resources.MergedDictionaries.Add(resourceDict);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load theme: {ex.Message}", "Theme Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LoadThemeState()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var config = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
                    if (config != null && config.ContainsKey("IsDarkMode"))
                    {
                        _isDarkMode = config["IsDarkMode"];
                    }
                }
            }
            catch { /* Ignore failures */ }
        }

        public void SaveThemeState()
        {
            try
            {
                var config = new { IsDarkMode = _isDarkMode };
                var json = JsonConvert.SerializeObject(config);
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath));
                File.WriteAllText(ConfigFilePath, json);
            }
            catch { /* Ignore failures */ }
        }
    }
}
