using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KaabliKataloog.Models;
using KaabliKataloog.Services;
using MaterialDesignThemes.Wpf;

namespace KaabliKataloog
{
    /// <summary>
    /// Adds cables to the catalogue. Stays open after each add (the form clears and the preview shows the next name),
    /// so several cables can be entered in a row; <see cref="AddedCount"/> tells the caller what changed.
    /// </summary>
    public partial class CatalogueEditor : Window
    {
        private readonly WireCatalogueService _service;
        private readonly ThemeManager _themeManager;

        public int AddedCount { get; private set; }

        public CatalogueEditor(WireCatalogueService service)
        {
            InitializeComponent();
            _service = service;
            _themeManager = new ThemeManager(this);

            // Materials already in the catalogue first, then the usual ones.
            CmbMaterial.ItemsSource = service.GetUniqueMaterials()
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Union(new[] { "Vask", "Alumiinium" }, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ShowCount();
            UpdatePreview();
            Loaded += (s, e) => TxtWireName.Focus();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ─── Add entry ────────────────────────────────────────────────────────

        private void BtnAddEntry_Click(object sender, RoutedEventArgs e)
        {
            string wireName = TxtWireName.Text.Trim();
            string material = CmbMaterial.SelectedItem as string ?? string.Empty;
            string conductorCount = TxtConductorCount.Text.Trim();
            string wireSize = TxtWireSize.Text.Trim().Replace(",", ".");

            // Required fields: mark each missing one in place and move focus to the first.
            var missing = new Control[]
            {
                string.IsNullOrEmpty(wireName) ? TxtWireName : null,
                string.IsNullOrEmpty(material) ? CmbMaterial : null,
                string.IsNullOrEmpty(conductorCount) ? TxtConductorCount : null,
                string.IsNullOrEmpty(wireSize) ? TxtWireSize : null
            }.Where(c => c != null).ToList();

            foreach (var c in new Control[] { TxtWireName, CmbMaterial, TxtConductorCount, TxtWireSize })
                MarkInvalid(c, missing.Contains(c));

            double capacity = 0;
            var capacityText = TxtCurrentCapacity.Text.Trim().Replace(",", ".");
            bool capacityInvalid = capacityText.Length > 0 &&
                !double.TryParse(capacityText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out capacity);
            MarkInvalid(TxtCurrentCapacity, capacityInvalid);

            if (missing.Count > 0)
            {
                ShowFeedback("Täida tärniga (*) väljad.", isError: true);
                missing[0].Focus();
                return;
            }
            if (capacityInvalid)
            {
                ShowFeedback("Koormusvool peab olema number, nt 13.5.", isError: true);
                TxtCurrentCapacity.Focus();
                return;
            }

            var wire = new WireData
            {
                WireName = wireName,
                Material = material,
                FireReactionClass = TxtFireClass.Text.Trim(),
                HalogenFree = ChkHalogenFree.IsChecked == true ? "Jah" : "Ei",
                UvKindel = ChkUvKindel.IsChecked == true ? "Jah" : "Ei",
                KoosKaitsejuhiga = ChkKoosKaitsejuhiga.IsChecked == true ? "Jah" : "Ei",
                ConductorCount = conductorCount,
                WireSize = wireSize,
                Välisläbimõõt = TxtVälisläbimõõt.Text.Trim(),
                Painderaadius = TxtPainderaadius.Text.Trim(),
                CurrentCapacity = capacity
            };

            _service.AddWire(wire);
            if (!_service.SaveToJson())
            {
                _service.RemoveWire(wire);
                ShowFeedback("Kaablit ei lisatud – kataloogi faili ei saanud kirjutada.", isError: true);
                return;
            }
            wire.ClearDirty();
            AddedCount++;

            ClearForm();
            ShowFeedback($"„{wire.DisplayName}“ lisatud · kataloogis {_service.GetAllWires().Count} kaablit", isError: false);
            TxtWireName.Focus();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private void Field_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized) return;
            if (sender is Control c) MarkInvalid(c, false);
            UpdatePreview();
        }

        /// <summary>Same naming rule as the catalogue (WireData.DisplayName), shown while typing.</summary>
        private void UpdatePreview()
        {
            if (PreviewName == null) return;
            var probe = new WireData
            {
                WireName = TxtWireName.Text.Trim(),
                ConductorCount = TxtConductorCount.Text.Trim(),
                WireSize = TxtWireSize.Text.Trim().Replace(",", "."),
                KoosKaitsejuhiga = ChkKoosKaitsejuhiga.IsChecked == true ? "Jah" : "Ei"
            };
            bool enough = probe.WireName.Length > 0 || probe.ConductorCount.Length > 0 || probe.WireSize.Length > 0;
            PreviewName.Text = enough ? probe.DisplayName.Trim() : "—";
        }

        private static void MarkInvalid(Control c, bool invalid)
        {
            if (invalid) c.SetResourceReference(BorderBrushProperty, "Status.Error");
            else c.ClearValue(BorderBrushProperty);
        }

        private void ShowFeedback(string text, bool isError)
        {
            TxtFeedback.Text = text;
            FeedbackIcon.Visibility = Visibility.Visible;
            FeedbackIcon.Kind = isError ? PackIconKind.AlertCircleOutline : PackIconKind.CheckCircleOutline;
            FeedbackIcon.SetResourceReference(ForegroundProperty, isError ? "Status.Error" : "Status.Ok");
            Motion.Reveal(FeedbackLine, fromY: 4);
        }

        private void ShowCount()
        {
            FeedbackIcon.Visibility = Visibility.Collapsed;
            TxtFeedback.Text = $"Kataloogis {_service.GetAllWires().Count} kaablit";
        }

        private void ClearForm()
        {
            TxtWireName.Text = TxtFireClass.Text =
            TxtConductorCount.Text = TxtWireSize.Text = TxtVälisläbimõõt.Text =
            TxtPainderaadius.Text = TxtCurrentCapacity.Text = string.Empty;
            // Material and the yes/no options usually repeat for the next cable, so they stay as they are.
            UpdatePreview();
        }
    }
}
