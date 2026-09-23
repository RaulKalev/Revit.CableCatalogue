using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace KaabliKataloog.Services
{
    /// <summary>
    /// Applies the appearance to a window (same model as Sentinel): Dark/Light palette, plus the Windows
    /// accessibility preferences
    ///  - High contrast       → palette built from the system high-contrast colours,
    ///  - Transparency off    → solid materials (no gradients/shadows),
    ///  - Animations off      → <see cref="ReducedMotion"/> (Motion uses opacity only).
    /// Only the palette dictionaries are swapped; shared styles stay merged and reference colours dynamically.
    /// The Dark/Light choice persists in config.json.
    /// </summary>
    public class ThemeManager
    {
        private const string PaletteRoot = "pack://application:,,,/KaabliKataloog;component/UI/Themes/";

        private readonly Window _window;
        private readonly List<ResourceDictionary> _applied = new List<ResourceDictionary>();
        private bool _isDarkMode = true;

        public event EventHandler ThemeChanged;
        public bool IsDarkMode => _isDarkMode;

        public ThemeManager(Window window)
        {
            _window = window;
            LoadThemeState();
            ApplyTheme();
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            window.Closed += (s, e) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        }

        // ------------------------------------------------------------------ accessibility preferences

        public static bool HighContrast => SystemParameters.HighContrast;

        /// <summary>Windows "Transparency effects" (Settings → Personalization → Colors) switched off.</summary>
        public static bool SolidMaterials
        {
            get
            {
                if (SystemParameters.HighContrast) return true;
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        var v = key?.GetValue("EnableTransparency");
                        return v is int && (int)v == 0;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Windows "Animation effects" switched off.</summary>
        public static bool ReducedMotion => !SystemParameters.ClientAreaAnimation;

        private void OnSystemParametersChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast) || e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyTheme();
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }));
            }
        }

        // ------------------------------------------------------------------ apply

        public void ToggleTheme()
        {
            _isDarkMode = !_isDarkMode;
            ApplyTheme();
            SaveThemeState();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyTheme()
        {
            try
            {
                var target = _window.Resources;
                foreach (var d in _applied) target.MergedDictionaries.Remove(d);
                _applied.Clear();
                // Palette dictionaries merged from XAML at parse time (design-time fallback).
                foreach (var d in target.MergedDictionaries.Where(IsPaletteSource).ToList()) target.MergedDictionaries.Remove(d);

                var dicts = BuildDictionaries();
                for (int i = 0; i < dicts.Length; i++) target.MergedDictionaries.Insert(i, dicts[i]);
                _applied.AddRange(dicts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Applying theme failed: {ex}");
            }
        }

        private static bool IsPaletteSource(ResourceDictionary d)
        {
            var s = d.Source?.OriginalString;
            return s != null && (s.EndsWith("DarkTheme.xaml") || s.EndsWith("LightTheme.xaml"));
        }

        private ResourceDictionary[] BuildDictionaries()
        {
            if (HighContrast) return new[] { BuildHighContrast() };

            var palette = new ResourceDictionary
            {
                Source = new Uri(PaletteRoot + (_isDarkMode ? "DarkTheme.xaml" : "LightTheme.xaml"), UriKind.Absolute)
            };
            var materials = SolidMaterials
                ? BuildSolidMaterials(palette)
                : new ResourceDictionary { ["Shadow.Opacity"] = _isDarkMode ? 0.45 : 0.16 };
            return new[] { palette, materials };
        }

        /// <summary>Reduced transparency: materials become solid surfaces with a defined edge, no shadows.</summary>
        private static ResourceDictionary BuildSolidMaterials(ResourceDictionary palette)
        {
            var raised = palette["Surface.Raised"] as Brush;
            var stroke = palette["Surface.Stroke"] as Brush;
            var content = palette["Surface.Content"] as Brush;
            return new ResourceDictionary
            {
                ["Sidebar.Material"] = palette["Surface.Inset"],
                ["Toolbar.Material"] = palette["Window.Background"],
                ["Floating.Material"] = raised ?? content,
                ["Floating.Edge"] = stroke,
                ["Sidebar.Edge"] = stroke,
                ["Shadow.Opacity"] = 0.0
            };
        }

        /// <summary>High contrast: every brush maps to a Windows system colour so the user's HC scheme is honoured.</summary>
        private static ResourceDictionary BuildHighContrast()
        {
            Func<Color, SolidColorBrush> b = c => { var br = new SolidColorBrush(c); br.Freeze(); return br; };
            var window = b(SystemColors.WindowColor);
            var text = b(SystemColors.WindowTextColor);
            var gray = b(SystemColors.GrayTextColor);
            var highlight = b(SystemColors.HighlightColor);
            var highlightText = b(SystemColors.HighlightTextColor);
            var hot = b(SystemColors.HotTrackColor);
            var btn = b(SystemColors.ControlColor);
            var d = new ResourceDictionary();
            foreach (var k in new[] { "Window.Background", "Sidebar.Material", "Toolbar.Material", "Floating.Material", "Surface.Content",
                                      "Surface.Raised", "Surface.Inset", "Input.Fill", "Control.Fill", "PrimaryBackgroundBrush",
                                      "SecondaryBackgroundBrush", "Row.Alternate", "Status.Ok.Subtle", "Status.Info.Subtle",
                                      "Status.Warning.Subtle", "Status.Error.Subtle", "Status.Neutral.Subtle", "Accent.Subtle" })
                d[k] = window;
            foreach (var k in new[] { "Window.Stroke", "Sidebar.Edge", "Floating.Edge", "Surface.Stroke", "Separator", "Control.Stroke",
                                      "Input.Stroke", "Input.StrokeHover", "Text.Primary", "Text.Secondary", "Text.Tertiary",
                                      "ForegroundBrush", "IconBrush", "BorderBrush", "Status.Ok", "Status.Info", "Status.Warning",
                                      "Status.Error", "Status.Neutral" })
                d[k] = text;
            d["Text.Disabled"] = gray;
            foreach (var k in new[] { "Accent", "Accent.Hover", "Accent.Pressed", "Row.Selected", "Row.SelectedInactive", "Control.FillPressed" })
                d[k] = highlight;
            d["Text.OnAccent"] = highlightText;
            d["Accent.Text"] = hot;
            d["Focus.Ring"] = hot;
            d["Control.FillHover"] = btn;
            d["Control.Plain.Hover"] = btn;
            d["Control.Plain.Pressed"] = highlight;
            d["HighlightBrush"] = btn;
            d["Row.Hover"] = btn;
            d["Scrim"] = b(Color.FromArgb(0x80, 0, 0, 0));
            d["Shadow.Color"] = Colors.Black;
            d["Shadow.Opacity"] = 0.0;
            return d;
        }

        // ------------------------------------------------------------------ preferences

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
