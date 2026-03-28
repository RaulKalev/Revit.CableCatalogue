using System;
using System.Windows;
using System.Windows.Input;
using KaabliKataloog.Models;
using KaabliKataloog.Services;

namespace KaabliKataloog
{
    public partial class CatalogueEditor : Window
    {
        private readonly WireCatalogueService _service;
        private readonly ThemeManager _themeManager;

        public CatalogueEditor(WireCatalogueService service)
        {
            InitializeComponent();
            _service = service;
            _themeManager = new ThemeManager(this);
            _themeManager.ApplyTheme();
            UpdateFooter();
        }

        // ─── Title bar ────────────────────────────────────────────────────────

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ─── Add entry ────────────────────────────────────────────────────────

        private void BtnAddEntry_Click(object sender, RoutedEventArgs e)
        {
            TxtValidation.Visibility = Visibility.Collapsed;

            string wireName      = TxtWireName.Text.Trim();
            string material      = (CmbMaterial.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            string conductorCount= TxtConductorCount.Text.Trim();
            string wireSize      = TxtWireSize.Text.Trim();

            if (string.IsNullOrEmpty(wireName) || string.IsNullOrEmpty(material) ||
                string.IsNullOrEmpty(conductorCount) || string.IsNullOrEmpty(wireSize))
            {
                TxtValidation.Text = "Tärniga (*) väljad on kohustuslikud: Kaabli tüüp, Materjal, Soonte arv, Ristlõige.";
                TxtValidation.Visibility = Visibility.Visible;
                return;
            }

            double.TryParse(TxtCurrentCapacity.Text.Trim().Replace(",", "."), out double capacity);

            var wire = new WireData
            {
                WireName          = wireName,
                Material          = material,
                FireReactionClass = TxtFireClass.Text.Trim(),
                HalogenFree       = (CmbHalogenFree.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? string.Empty,
                UvKindel          = (CmbUvKindel.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? string.Empty,
                KoosKaitsejuhiga  = (CmbKoosKaitsejuhiga.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? string.Empty,
                ConductorCount    = conductorCount,
                WireSize          = wireSize,
                Välisläbimõõt     = TxtVälisläbimõõt.Text.Trim(),
                Painderaadius     = TxtPainderaadius.Text.Trim(),
                CurrentCapacity   = capacity
            };

            _service.AddWire(wire);
            _service.SaveToJson();
            ClearForm();
            UpdateFooter();

            MessageBox.Show($"Kaabel '{wire.DisplayName}' lisati kataloogi.", "Lisatud",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private void ClearForm()
        {
            TxtWireName.Text = TxtFireClass.Text =
            TxtConductorCount.Text =
            TxtWireSize.Text = TxtVälisläbimõõt.Text =
            TxtPainderaadius.Text = TxtCurrentCapacity.Text = string.Empty;
            CmbMaterial.SelectedItem = null;
            CmbHalogenFree.SelectedItem = null;
            CmbUvKindel.SelectedItem = null;
            CmbKoosKaitsejuhiga.SelectedItem = null;
        }

        private void UpdateFooter()
        {
            int count = _service.GetAllWires().Count;
            TxtFooter.Text = $"Kataloogis kokku: {count} kaablit  |  cables.json";
        }
    }
}
