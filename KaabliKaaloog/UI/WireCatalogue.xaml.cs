using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KaabliKataloog.Services;

namespace KaabliKataloog
{
    public partial class WireCatalogue : Window
    {
        private readonly ThemeManager _themeManager;
        private readonly Window _mainWindow;
        private readonly WireCatalogueService _wireCatalogueService;

        public List<string> FireReactionClasses { get; set; } // 🔹 Bind to XAML ComboBox

        public WireCatalogue(Window mainWindow, bool isDarkMode)
        {
            InitializeComponent();

            _themeManager = new ThemeManager(this);
            _themeManager.ApplyTheme();
            _themeManager.ThemeChanged += OnThemeChanged;

            _mainWindow = mainWindow;
            _mainWindow.LocationChanged += UpdatePosition;
            _mainWindow.SizeChanged += UpdatePosition;

            // ✅ Ensure service is initialized BEFORE setting event listeners
            _wireCatalogueService = new WireCatalogueService();
            System.Diagnostics.Debug.WriteLine("✅ _wireCatalogueService has been initialized!");

            // ✅ Load dropdown values AFTER initializing service
            LoadDropdownOptions();
            LoadMaterialOptions();
            LoadFireReactionClasses();

            UpdatePosition(null, null);
            DataContext = this;

            // ✅ Force a UI update AFTER everything is set up
            Dispatcher.BeginInvoke(new Action(() => OnInputChanged(null, null)), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            _themeManager.ApplyTheme();
        }

        private void UpdatePosition(object sender, EventArgs e)
        {
            if (_mainWindow == null || !IsLoaded) return;

            // ✅ Attach WireCatalogue immediately to the right of MainWindow
            this.Left = _mainWindow.Left + _mainWindow.Width + 2; // Attach to the right
            this.Top = _mainWindow.Top + 30; // Align vertically
        }

        private void LoadMaterialOptions()
        {
            var materials = _wireCatalogueService.GetUniqueMaterials();
            MaterialComboBox.ItemsSource = materials;
        }

        private void LoadFireReactionClasses()
        {
            // ✅ Fetch unique fire reaction classes from the database
            FireReactionClasses = _wireCatalogueService.GetUniqueFireReactionClasses();
        }
        private void LoadDropdownOptions()
        {
            MaterialComboBox.ItemsSource = _wireCatalogueService.GetUniqueMaterials();
            ReactionClassComboBox.ItemsSource = _wireCatalogueService.GetUniqueFireReactionClasses();
            ConductorCountComboBox.ItemsSource = _wireCatalogueService.ConductorCounts;
            WireSizeComboBox.ItemsSource = _wireCatalogueService.WireSizes;
        }
        private void OnInputChanged(object sender, EventArgs e)
        {
            if (_wireCatalogueService == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ _wireCatalogueService is NULL. Skipping input change handling.");
                return;
            }

            string material = MaterialComboBox.SelectedItem?.ToString() ?? "";
            string reactionClass = ReactionClassComboBox.SelectedItem?.ToString() ?? "";
            string uvKindel = UvKindelCheckBox.IsChecked == true ? "Jah" : "Ei";
            string koosKaitsejuhiga = KoosKaitsejuhigaCheckBox.IsChecked == true ? "Jah" : "Ei";
            string conductorCount = ConductorCountComboBox.SelectedItem?.ToString() ?? "";
            string wireSize = WireSizeComboBox.SelectedItem?.ToString() ?? "";

            var validWireSizes = _wireCatalogueService.GetFilteredWireSizes(material, reactionClass, uvKindel, koosKaitsejuhiga, conductorCount);
            UpdateWireSizeDropdown(validWireSizes);

            if (string.IsNullOrEmpty(wireSize) && validWireSizes.Any())
            {
                wireSize = validWireSizes.First();
                WireSizeComboBox.SelectedItem = wireSize;
            }

            var matchingWires = _wireCatalogueService.GetFilteredWires(material, reactionClass, uvKindel, koosKaitsejuhiga, conductorCount, wireSize);

            if (matchingWires.Count > 0)
            {
                var bestMatch = matchingWires.First();

                // ✅ Convert "Jah" → "G" and "Ei" → "x"
                string koosKaitsejuhigaDisplay = bestMatch.KoosKaitsejuhiga.Equals("Jah", StringComparison.OrdinalIgnoreCase) ? "G" : "X";

                BestMatchResult.Text = $"Parim vaste:\n\n" +
                                       $"{bestMatch.WireName} " + $"{bestMatch.ConductorCount}" + $"{koosKaitsejuhigaDisplay}" + $"{bestMatch.WireSize}\n" +
                                       $"Materjal: {bestMatch.Material}\n" +
                                       $"Klass: {bestMatch.FireReactionClass}\n" +
                                       $"Halogeenivaba: {bestMatch.HalogenFree}\n" +
                                       $"Juhi kategooria: {bestMatch.JuhiKategooria}\n" +
                                       $"UV-kindel: {bestMatch.UvKindel}\n" + 
                                       $"Välisläbimõõt: {bestMatch.Välisläbimõõt} mm\n" +
                                       $"Min painderaadius: {bestMatch.Painderaadius} mm\n" +
                                       $"Koormusvool: {bestMatch.CurrentCapacity}A";
            }
            else
            {
                BestMatchResult.Text = "Sobivat kaablit ei leitud.";
            }
        }

        // ✅ **Function to Update Wire Size ComboBox**
        private void UpdateWireSizeDropdown(List<string> validWireSizes)
        {
            if (validWireSizes == null || !validWireSizes.Any())
                return;

            string prevWireSize = WireSizeComboBox.SelectedItem?.ToString();

            if (!validWireSizes.SequenceEqual(WireSizeComboBox.ItemsSource as List<string> ?? new List<string>()))
            {
                WireSizeComboBox.ItemsSource = validWireSizes;
            }

            if (validWireSizes.Contains(prevWireSize))
            {
                WireSizeComboBox.SelectedItem = prevWireSize;
            }
            else if (validWireSizes.Any())
            {
                WireSizeComboBox.SelectedIndex = 0;
            }

            System.Diagnostics.Debug.WriteLine($"Updated Wire Sizes: {string.Join(", ", validWireSizes)}");
        }

    }
}
