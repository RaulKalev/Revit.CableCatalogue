using System;
using System.Windows;

namespace KaabliKataloog.Services
{
    public class ThemeManager
    {
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
            var elementStylesUri = $"pack://application:,,,/{assemblyName};component/UI/Themes/ElementStyles.xaml";

            try
            {
                _window.Resources.MergedDictionaries.Clear();
                _window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.DataGrid.xaml", UriKind.Absolute)
                });
                _window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themeUri, UriKind.Absolute) });
                _window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(elementStylesUri, UriKind.Absolute) });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load theme: {ex.Message}", "Theme Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LoadThemeState()
        {
            var config = AppConfigService.Load();
            _isDarkMode = config.IsDarkMode;
        }

        public void SaveThemeState()
        {
            var config = AppConfigService.Load();
            config.IsDarkMode = _isDarkMode;
            AppConfigService.Save(config);
        }
    }
}
