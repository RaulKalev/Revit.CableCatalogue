using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using KaabliKataloog.Models;
using KaabliKataloog.Services;
using MaterialDesignThemes.Wpf;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace KaabliKataloog
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly ThemeManager _themeManager;
        private readonly WireCatalogueService _wireCatalogueService;
        private readonly ExternalEvent _createWireTypesEvent;
        private readonly CreateWireTypesHandler _createWireTypesHandler;

        /// <summary>Wires whose PropertyChanged we listen to (selection + unsaved state).</summary>
        private readonly HashSet<WireData> _observed = new HashSet<WireData>();

        private CheckBox _selectAllCheck;
        private bool _isUpdatingFilters;
        private bool _closeConfirmed;
        private DispatcherTimer _statusTimer;

        public ObservableCollection<WireData> FilteredWireResults { get; } = new ObservableCollection<WireData>();

        public MainWindow(UIDocument uiDoc, Document doc, View currentView)
        {
            InitializeComponent();

            _wireCatalogueService = new WireCatalogueService();
            _createWireTypesHandler = new CreateWireTypesHandler { UiDoc = uiDoc, Doc = doc };
            // No document = opened outside a Revit command (UI previews); creating types is then unavailable.
            if (uiDoc != null) _createWireTypesEvent = ExternalEvent.Create(_createWireTypesHandler);
            _createWireTypesHandler.Completed = result => Dispatcher.BeginInvoke(new Action(() => OnWireTypesCreated(result)));

            _themeManager = new ThemeManager(this);
            _themeManager.ThemeChanged += (s, e) => UpdateThemeButton();
            UpdateThemeButton();

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            DataContext = this;

            StateChanged += (s, e) => UpdateMaximizedState();
            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;

            LoadDropdownOptions();
            RefreshCatalogueInfo();
            ApplyFilters();
        }

        // ================================================================== state for the view

        private string _resultSummary;
        public string ResultSummary { get => _resultSummary; private set => Set(ref _resultSummary, value); }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            private set { if (Set(ref _selectedCount, value)) OnPropertyChanged(nameof(HasSelection)); }
        }
        public bool HasSelection => SelectedCount > 0;

        private string _selectionSummary = "Vali tabelist kaablid";
        public string SelectionSummary { get => _selectionSummary; private set => Set(ref _selectionSummary, value); }

        private int _dirtyCount;
        public int DirtyCount
        {
            get => _dirtyCount;
            private set
            {
                if (!Set(ref _dirtyCount, value)) return;
                OnPropertyChanged(nameof(HasDirty));
                OnPropertyChanged(nameof(DirtySummary));
            }
        }
        public bool HasDirty => DirtyCount > 0;
        public string DirtySummary => DirtyCount == 1 ? "1 rida salvestamata" : $"{DirtyCount} rida salvestamata";

        private bool _isFilterActive;
        public bool IsFilterActive { get => _isFilterActive; private set => Set(ref _isFilterActive, value); }

        private string _catalogueFileName;
        public string CatalogueFileName { get => _catalogueFileName; private set => Set(ref _catalogueFileName, value); }

        private string _cataloguePathTip;
        public string CataloguePathTip { get => _cataloguePathTip; private set => Set(ref _cataloguePathTip, value); }

        private bool _isEmpty;
        public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

        private bool _isCatalogueMissing;
        public bool IsCatalogueMissing { get => _isCatalogueMissing; private set => Set(ref _isCatalogueMissing, value); }

        private string _emptyTitle;
        public string EmptyTitle { get => _emptyTitle; private set => Set(ref _emptyTitle, value); }

        private string _emptyMessage;
        public string EmptyMessage { get => _emptyMessage; private set => Set(ref _emptyMessage, value); }

        private PackIconKind _emptyIcon = PackIconKind.Magnify;
        public PackIconKind EmptyIcon { get => _emptyIcon; private set => Set(ref _emptyIcon, value); }

        private string _statusMessage;
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        private bool _statusIsError;
        public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

        // ================================================================== filtering

        private void LoadDropdownOptions()
        {
            _isUpdatingFilters = true;
            try
            {
                var material = MaterialComboBox.SelectedItem as string;
                var reactionClass = ReactionClassComboBox.SelectedItem as string;
                var conductorCount = ConductorCountComboBox.SelectedItem as string;

                MaterialComboBox.ItemsSource = _wireCatalogueService.GetUniqueMaterials();
                ReactionClassComboBox.ItemsSource = _wireCatalogueService.GetUniqueFireReactionClasses();
                ConductorCountComboBox.ItemsSource = _wireCatalogueService.ConductorCounts;
                WireSizeComboBox.ItemsSource = _wireCatalogueService.WireSizes;

                // Keep what the user had chosen when it still exists after a reload / save.
                MaterialComboBox.SelectedItem = material;
                ReactionClassComboBox.SelectedItem = reactionClass;
                ConductorCountComboBox.SelectedItem = conductorCount;
            }
            finally
            {
                _isUpdatingFilters = false;
            }
        }

        private void OnInputChanged(object sender, EventArgs e)
        {
            // Filter controls raise events while InitializeComponent is still running.
            if (_isUpdatingFilters || _wireCatalogueService == null) return;
            ApplyFilters();
        }

        /// <summary>
        /// Rebuilds the visible rows from the whole catalogue. Ticked rows stay ticked when a filter hides them
        /// (the footer says how many are hidden), so narrowing the list never silently loses a selection.
        /// </summary>
        private void ApplyFilters()
        {
            string material = MaterialComboBox.SelectedItem?.ToString() ?? "";
            string reactionClass = ReactionClassComboBox.SelectedItem?.ToString() ?? "";
            string uvKindel = UvKindelCheckBox.IsChecked == true ? "Jah" : "";
            string koosKaitsejuhiga = KoosKaitsejuhigaCheckBox.IsChecked == true ? "Jah" : "";
            string conductorCount = ConductorCountComboBox.SelectedItem?.ToString() ?? "";
            string wireSize = WireSizeComboBox.SelectedItem?.ToString() ?? "";

            // Only offer cross-sections that exist for the other filters.
            var validWireSizes = _wireCatalogueService.GetFilteredWireSizes(material, reactionClass, uvKindel, koosKaitsejuhiga, conductorCount);
            UpdateWireSizeDropdown(validWireSizes);
            if (!validWireSizes.Contains(wireSize)) wireSize = "";

            var allWires = _wireCatalogueService.GetAllWires();
            var filteredWires = allWires
                .Where(w =>
                    (string.IsNullOrEmpty(material) || w.Material == material) &&
                    (string.IsNullOrEmpty(reactionClass) || w.FireReactionClass == reactionClass) &&
                    (string.IsNullOrEmpty(uvKindel) || w.UvKindel == uvKindel) &&
                    (string.IsNullOrEmpty(koosKaitsejuhiga) || w.KoosKaitsejuhiga == koosKaitsejuhiga) &&
                    (string.IsNullOrEmpty(conductorCount) || w.ConductorCount == conductorCount) &&
                    (string.IsNullOrEmpty(wireSize) || w.WireSize == wireSize));

            var searchText = WireSearchBox.Text.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(searchText))
            {
                filteredWires = filteredWires.Where(w =>
                    (w.WireName ?? "").ToLowerInvariant().Contains(searchText) ||
                    (w.DisplayName ?? "").ToLowerInvariant().Contains(searchText) ||
                    (w.Material ?? "").ToLowerInvariant().Contains(searchText) ||
                    (w.FireReactionClass ?? "").ToLowerInvariant().Contains(searchText) ||
                    (w.ConductorCount ?? "").ToLowerInvariant().Contains(searchText) ||
                    (w.WireSize ?? "").ToLowerInvariant().Contains(searchText));
            }

            FilteredWireResults.Clear();
            foreach (var wire in filteredWires)
            {
                Observe(wire);
                FilteredWireResults.Add(wire);
            }

            IsFilterActive = !string.IsNullOrEmpty(material) || !string.IsNullOrEmpty(reactionClass) ||
                             !string.IsNullOrEmpty(conductorCount) || WireSizeComboBox.SelectedItem != null ||
                             UvKindelCheckBox.IsChecked == true || KoosKaitsejuhigaCheckBox.IsChecked != true ||
                             !string.IsNullOrEmpty(searchText);

            int total = allWires.Count;
            ResultSummary = total == 0 ? "" :
                FilteredWireResults.Count == total ? $"{total} kaablit" : $"{FilteredWireResults.Count} / {total} kaablit";

            UpdateEmptyState();
            UpdateSelection();
            UpdateDirty();
        }

        private void UpdateWireSizeDropdown(List<string> validWireSizes)
        {
            if (validWireSizes == null || !validWireSizes.Any())
                return;

            string prevWireSize = WireSizeComboBox.SelectedItem?.ToString();
            _isUpdatingFilters = true;
            try
            {
                if (!validWireSizes.SequenceEqual(WireSizeComboBox.ItemsSource as List<string> ?? new List<string>()))
                    WireSizeComboBox.ItemsSource = validWireSizes;

                WireSizeComboBox.SelectedItem = !string.IsNullOrEmpty(prevWireSize) && validWireSizes.Contains(prevWireSize)
                    ? prevWireSize
                    : null;
            }
            finally
            {
                _isUpdatingFilters = false;
            }
        }

        private void UpdateEmptyState()
        {
            IsEmpty = FilteredWireResults.Count == 0;
            IsCatalogueMissing = _wireCatalogueService.GetAllWires().Count == 0;
            if (IsCatalogueMissing)
            {
                EmptyIcon = PackIconKind.DatabaseOffOutline;
                EmptyTitle = "Kataloog on tühi";
                EmptyMessage = _wireCatalogueService.GetCurrentJsonPath() == null
                    ? "Kataloogi faili (cables.json) ei leitud Dropboxist. Vali fail käsitsi."
                    : "Valitud failis pole kaableid. Vali teine fail või lisa kaabel.";
            }
            else
            {
                EmptyIcon = PackIconKind.Magnify;
                EmptyTitle = "Sobivaid kaableid pole";
                EmptyMessage = "Muuda otsingut või filtreid.";
            }
        }

        private void MaterialComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFilters) return;
            ResetDownstream(ReactionClassComboBox, ConductorCountComboBox, WireSizeComboBox);
        }

        private void ReactionClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFilters) return;
            ResetDownstream(ConductorCountComboBox, WireSizeComboBox);
        }

        private void ConductorCountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingFilters) return;
            ResetDownstream(WireSizeComboBox);
        }

        /// <summary>Changing a filter clears the ones that depend on it, then filters once.</summary>
        private void ResetDownstream(params ComboBox[] downstream)
        {
            _isUpdatingFilters = true;
            try { foreach (var c in downstream) c.SelectedItem = null; }
            finally { _isUpdatingFilters = false; }
            ApplyFilters();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ComboBox combo)
            {
                combo.SelectedItem = null;
                combo.Focus();
            }
        }

        private void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            _isUpdatingFilters = true;
            try
            {
                MaterialComboBox.SelectedItem = null;
                ReactionClassComboBox.SelectedItem = null;
                ConductorCountComboBox.SelectedItem = null;
                WireSizeComboBox.SelectedItem = null;
                UvKindelCheckBox.IsChecked = false;
                KoosKaitsejuhigaCheckBox.IsChecked = true;
                WireSearchBox.Clear();
            }
            finally
            {
                _isUpdatingFilters = false;
            }
            ApplyFilters();
        }

        private void WireSearch_TextChanged(object sender, TextChangedEventArgs e) => OnInputChanged(sender, e);

        private void ClearWireSearch_Click(object sender, RoutedEventArgs e)
        {
            WireSearchBox.Clear();
            WireSearchBox.Focus();
        }

        private void WireSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && WireSearchBox.Text.Length > 0)
            {
                WireSearchBox.Clear();
                e.Handled = true;
            }
        }

        // ================================================================== selection + unsaved rows

        private void Observe(WireData wire)
        {
            if (_observed.Add(wire)) wire.PropertyChanged += Wire_PropertyChanged;
        }

        private void Wire_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WireData.IsSelected)) UpdateSelection();
            else if (e.PropertyName == nameof(WireData.IsDirty)) UpdateDirty();
        }

        private IEnumerable<WireData> SelectedWires => _wireCatalogueService.GetAllWires().Where(w => w.IsSelected);

        private void UpdateSelection()
        {
            var selected = SelectedWires.ToList();
            SelectedCount = selected.Count;
            int hidden = selected.Count(w => !FilteredWireResults.Contains(w));

            SelectionSummary = SelectedCount == 0 ? "Vali tabelist kaablid"
                : (SelectedCount == 1 ? "1 valitud" : $"{SelectedCount} valitud")
                  + (hidden > 0 ? $" · {hidden} filtriga peidetud" : "");

            if (_selectAllCheck != null)
            {
                int visibleSelected = FilteredWireResults.Count(w => w.IsSelected);
                _selectAllCheck.IsChecked = visibleSelected == 0 ? false
                    : visibleSelected == FilteredWireResults.Count ? (bool?)true : null;
                _selectAllCheck.IsEnabled = FilteredWireResults.Count > 0;
            }
        }

        private void UpdateDirty() => DirtyCount = _wireCatalogueService.GetAllWires().Count(w => w.IsDirty);

        private void SelectAll_Loaded(object sender, RoutedEventArgs e)
        {
            _selectAllCheck = (CheckBox)sender;
            UpdateSelection();
        }

        /// <summary>Header box: ticks every visible row, or clears them when all are already ticked.</summary>
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool allVisibleSelected = FilteredWireResults.Count > 0 && FilteredWireResults.All(w => w.IsSelected);
            foreach (var wire in FilteredWireResults) wire.IsSelected = !allVisibleSelected;
            UpdateSelection();
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (var wire in SelectedWires.ToList()) wire.IsSelected = false;
        }

        // Numbers are stored as text; sort them by value so 10 comes after 2.5.
        private static readonly HashSet<string> NumericTextColumns = new HashSet<string>
        {
            nameof(WireData.ConductorCount), nameof(WireData.WireSize), nameof(WireData.Välisläbimõõt), nameof(WireData.Painderaadius)
        };

        private void WireDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var path = e.Column.SortMemberPath;
            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(WireDataGrid.ItemsSource);
            if (!NumericTextColumns.Contains(path))
            {
                view.CustomSort = null; // hand back to the grid's own sorting
                return;
            }

            e.Handled = true;
            var direction = e.Column.SortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            foreach (var c in WireDataGrid.Columns) if (c != e.Column) c.SortDirection = null;
            e.Column.SortDirection = direction;
            view.CustomSort = new NumericTextSort(path, direction);
        }

        private sealed class NumericTextSort : IComparer
        {
            private readonly Func<WireData, string> _get;
            private readonly int _sign;
            private readonly WireCatalogueService.NumericComparer _cmp = new WireCatalogueService.NumericComparer();

            public NumericTextSort(string path, ListSortDirection direction)
            {
                var prop = typeof(WireData).GetProperty(path);
                _get = w => prop.GetValue(w) as string ?? "";
                _sign = direction == ListSortDirection.Ascending ? 1 : -1;
            }

            public int Compare(object x, object y) => _sign * _cmp.Compare(_get((WireData)x), _get((WireData)y));
        }

        // ================================================================== actions

        private void CreateWireTypesButton_Click(object sender, RoutedEventArgs e)
        {
            var names = SelectedWires.Select(w => w.DisplayName).Distinct().ToList();
            if (names.Count == 0 || _createWireTypesEvent == null) return;

            _createWireTypesHandler.WireTypeNamesToCreate = names;
            _createWireTypesEvent.Raise();
            ShowStatus(names.Count == 1 ? "Revit loob 1 tüübi…" : $"Revit loob {names.Count} tüüpi…");
        }

        /// <summary>Revit finished (or failed) creating types: report it in a sheet, like every other result.</summary>
        private void OnWireTypesCreated(CreateWireTypesResult result)
        {
            if (result.Error != null)
            {
                ShowStatus("Tüüpe ei loodud", isError: true);
                ShowSheet("Tüüpe ei loodud", result.Error, "OK", onPrimary: null,
                          icon: PackIconKind.AlertCircleOutline, iconBrush: "Status.Error", secondaryText: null);
                return;
            }

            // One line per name: which kinds were created, or that it was already in the project.
            var details = new List<SheetDetail>();
            foreach (var name in result.CreatedWireTypes.Union(result.CreatedCableTypes))
            {
                bool wire = result.CreatedWireTypes.Contains(name), cable = result.CreatedCableTypes.Contains(name);
                details.Add(new SheetDetail { Text = name, Note = wire && cable ? "juhe ja kaabel" : wire ? "juhe" : "kaabel" });
            }
            details.AddRange(result.Existing.Select(n => new SheetDetail { Text = n, Note = "juba olemas" }));

            if (result.CreatedCount == 0)
            {
                ShowStatus("Uusi tüüpe ei loodud – kõik olid juba olemas");
                ShowSheet("Uusi tüüpe ei loodud", "Kõik valitud tüübid on projektis juba olemas.", "OK", onPrimary: null,
                          icon: PackIconKind.InformationOutline, secondaryText: null, details: details);
                return;
            }

            int names = details.Count - result.Existing.Count;
            string title = names == 1 ? "Loodi 1 tüüp" : $"Loodi {names} tüüpi";
            var parts = new List<string>();
            int w = result.CreatedWireTypes.Count, c = result.CreatedCableTypes.Count;
            if (w > 0) parts.Add(w == 1 ? "1 juhtmetüüp" : $"{w} juhtmetüüpi");
            if (c > 0) parts.Add(c == 1 ? "1 kaablitüüp" : $"{c} kaablitüüpi");
            string message = "Revitisse lisati " + string.Join(" ja ", parts) + "."
                             + (result.Existing.Count == 0 ? "" :
                                result.Existing.Count == 1 ? " 1 tüüp oli juba olemas." : $" {result.Existing.Count} tüüpi olid juba olemas.");

            ShowStatus(title);
            ShowSheet(title, message, "OK", onPrimary: null,
                      icon: PackIconKind.CheckCircleOutline, iconBrush: "Status.Ok", secondaryText: null, details: details);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveChanges();

        private bool SaveChanges()
        {
            // Commit any cell that is still being edited.
            WireDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            int count = DirtyCount;
            if (!_wireCatalogueService.SaveToJson())
            {
                ShowStatus("Muudatusi ei salvestatud – kataloogi faili ei saanud kirjutada.", isError: true);
                return false;
            }

            foreach (var wire in _wireCatalogueService.GetAllWires()) wire.ClearDirty();
            LoadDropdownOptions();
            ApplyFilters();
            ShowStatus(count == 1 ? "1 muudatus salvestatud" : $"{count} muudatust salvestatud");
            return true;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var toDelete = SelectedWires.ToList();
            if (toDelete.Count == 0) return;

            int hidden = toDelete.Count(w => !FilteredWireResults.Contains(w));
            string title = toDelete.Count == 1 ? $"Kustuta „{toDelete[0].DisplayName}“?" : $"Kustuta {toDelete.Count} kaablit?";
            string message = "Kaablid eemaldatakse kataloogi failist. Seda ei saa tagasi võtta."
                             + (hidden > 0 ? $"\n\n{hidden} neist on praegu filtriga peidetud." : "");

            ShowSheet(title, message, "Kustuta", onPrimary: () =>
            {
                foreach (var wire in toDelete)
                {
                    wire.IsSelected = false;
                    _wireCatalogueService.RemoveWire(wire);
                }
                if (!_wireCatalogueService.SaveToJson())
                {
                    ShowStatus("Kustutamist ei salvestatud – kataloogi faili ei saanud kirjutada.", isError: true);
                    return;
                }
                LoadDropdownOptions();
                ApplyFilters();
                ShowStatus(toDelete.Count == 1 ? "1 kaabel kustutatud" : $"{toDelete.Count} kaablit kustutatud");
            }, icon: PackIconKind.TrashCanOutline, destructive: true);
        }

        private void AddCable_Click(object sender, RoutedEventArgs e)
        {
            var editor = new CatalogueEditor(_wireCatalogueService) { Owner = this };
            editor.ShowDialog();
            if (editor.AddedCount == 0) return;

            LoadDropdownOptions();
            ApplyFilters();
            ShowStatus(editor.AddedCount == 1 ? "1 kaabel lisatud" : $"{editor.AddedCount} kaablit lisatud");
        }

        private void BrowseDatabase_Click(object sender, RoutedEventArgs e)
        {
            if (HasDirty)
            {
                ShowSheet("Salvesta muudatused enne teise faili avamist?",
                    DirtySummary + ". Teise faili avamisel need kaovad.",
                    "Salvesta",
                    onPrimary: () => { if (SaveChanges()) PickCatalogueFile(); },
                    icon: PackIconKind.ContentSaveAlertOutline,
                    alternateText: "Ära salvesta",
                    onAlternate: PickCatalogueFile);
                return;
            }
            PickCatalogueFile();
        }

        private void PickCatalogueFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Vali kataloogi fail (cables.json)",
                Filter = "JSON failid (*.json)|*.json|Kõik failid (*.*)|*.*",
                FilterIndex = 1
            };

            var current = _wireCatalogueService.GetCurrentJsonPath();
            if (!string.IsNullOrEmpty(current))
            {
                dlg.InitialDirectory = Path.GetDirectoryName(current);
                dlg.FileName = Path.GetFileName(current);
            }

            if (dlg.ShowDialog(this) != true) return;

            _wireCatalogueService.SetCablesJsonPath(dlg.FileName);
            foreach (var wire in _observed) wire.PropertyChanged -= Wire_PropertyChanged;
            _observed.Clear();

            LoadDropdownOptions();
            RefreshCatalogueInfo();
            ApplyFilters();
            ShowStatus($"Avatud {Path.GetFileName(dlg.FileName)} · {_wireCatalogueService.GetAllWires().Count} kaablit");
        }

        private void RefreshCatalogueInfo()
        {
            var path = _wireCatalogueService.GetCurrentJsonPath();
            CatalogueFileName = path == null ? "Fail valimata" : Path.GetFileName(path);
            CataloguePathTip = path == null
                ? "Kataloogi faili ei leitud. Klõpsa, et valida cables.json."
                : path + "\nKlõpsa, et valida teine fail.";
        }

        // ================================================================== feedback

        /// <summary>Result of the last action, shown in the footer (glyph + words). Success fades after a while.</summary>
        private void ShowStatus(string message, bool isError = false)
        {
            StatusIsError = isError;
            StatusMessage = message;
            Motion.Reveal(StatusLine, fromY: 4);

            if (_statusTimer == null)
            {
                _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                _statusTimer.Tick += (s, e) =>
                {
                    _statusTimer.Stop();
                    Motion.Dismiss(StatusLine, () => { if (!_statusTimer.IsEnabled) StatusMessage = null; }, toY: 0);
                };
            }
            _statusTimer.Stop();
            if (!isError) _statusTimer.Start();
        }

        // ================================================================== sheet (in-window confirmation)

        private Action _sheetPrimary;
        private Action _sheetAlternate;
        private Action _sheetCancel;
        private IInputElement _focusBeforeSheet;
        private bool _sheetOpen;

        /// <summary>One line in a sheet's detail list.</summary>
        public class SheetDetail
        {
            public string Text { get; set; }
            public string Note { get; set; }
        }

        /// <param name="secondaryText">Cancel button text; null for a notice that only needs acknowledging.</param>
        /// <param name="iconBrush">Palette key for the icon colour (defaults to accent, or error when destructive).</param>
        private void ShowSheet(string title, string message, string primaryText, Action onPrimary,
                               PackIconKind icon = PackIconKind.HelpCircleOutline, bool destructive = false,
                               string alternateText = null, Action onAlternate = null, Action onCancel = null,
                               string secondaryText = "Loobu", string iconBrush = null,
                               IList<SheetDetail> details = null)
        {
            _sheetPrimary = onPrimary;
            _sheetAlternate = onAlternate;
            _sheetCancel = onCancel;

            SheetTitle.Text = title;
            SheetMessage.Text = message;
            SheetMessage.Visibility = string.IsNullOrEmpty(message) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            SheetIcon.Kind = icon;
            SheetIcon.SetResourceReference(ForegroundProperty, iconBrush ?? (destructive ? "Status.Error" : "Accent.Text"));
            SheetPrimary.Content = primaryText;
            SheetPrimary.Style = (Style)FindResource(destructive ? "Button.DestructivePrimary" : "Button.Primary");
            SheetSecondary.Content = secondaryText;
            SheetSecondary.Visibility = secondaryText == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            SheetAlternate.Content = alternateText;
            SheetAlternate.Visibility = alternateText == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            SheetDetails.ItemsSource = details;
            SheetDetailsCard.Visibility = details != null && details.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            if (!_sheetOpen) _focusBeforeSheet = Keyboard.FocusedElement;
            _sheetOpen = true;
            SheetHost.Visibility = System.Windows.Visibility.Visible;
            Motion.Reveal(SheetCard, fromY: -8);

            // Destructive: the safe choice has the focus, so Enter never deletes by accident.
            Dispatcher.BeginInvoke(new Action(() => (destructive ? SheetSecondary : SheetPrimary).Focus()), DispatcherPriority.Input);
        }

        private void CloseSheet(Action then)
        {
            if (!_sheetOpen) return;
            _sheetOpen = false;
            // Leaves the way it came in (up), then the action runs on the settled window.
            Motion.Dismiss(SheetCard, () =>
            {
                if (_sheetOpen) return; // re-opened while closing
                SheetHost.Visibility = System.Windows.Visibility.Collapsed;
                _focusBeforeSheet?.Focus();
            }, toY: -8);
            then?.Invoke();
        }

        private void SheetPrimary_Click(object sender, RoutedEventArgs e) => CloseSheet(_sheetPrimary);
        private void SheetAlternate_Click(object sender, RoutedEventArgs e) => CloseSheet(_sheetAlternate);
        private void SheetSecondary_Click(object sender, RoutedEventArgs e) => CloseSheet(_sheetCancel);
        private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => CloseSheet(_sheetCancel);

        // ================================================================== keyboard

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            if (_sheetOpen)
            {
                if (key == Key.Escape)
                {
                    CloseSheet(_sheetCancel);
                    e.Handled = true;
                }
                return;
            }

            if (mods == ModifierKeys.Control && key == Key.F)
            {
                WireSearchBox.Focus();
                WireSearchBox.SelectAll();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && key == Key.S)
            {
                if (HasDirty) SaveChanges();
                e.Handled = true;
            }
        }

        // ================================================================== window

        private void OnClosing(object sender, CancelEventArgs e)
        {
            WireDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (_closeConfirmed || !HasDirty) return;

            // Unsaved edits are never thrown away silently.
            e.Cancel = true;
            ShowSheet("Salvesta muudatused enne sulgemist?",
                DirtySummary + ". Salvestamata muudatused kaovad.",
                "Salvesta",
                onPrimary: () => { if (SaveChanges()) CloseConfirmed(); },
                icon: PackIconKind.ContentSaveAlertOutline,
                alternateText: "Ära salvesta",
                onAlternate: CloseConfirmed);
        }

        private void CloseConfirmed()
        {
            _closeConfirmed = true;
            Dispatcher.BeginInvoke(new Action(Close));
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e) => _themeManager.ToggleTheme();

        private void UpdateThemeButton()
        {
            ThemeIcon.Kind = _themeManager.IsDarkMode ? PackIconKind.WeatherNight : PackIconKind.WhiteBalanceSunny;
            ThemeText.Text = _themeManager.IsDarkMode ? "Tume välimus" : "Hele välimus";
        }

        private void UpdateMaximizedState()
        {
            // A maximized WindowChrome window extends past the work area by the resize frame; pad the content back in.
            var maximized = WindowState == WindowState.Maximized;
            var frame = SystemParameters.WindowResizeBorderThickness;
            RootGrid.Margin = maximized ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4) : new Thickness(0);
            MaximizeIcon.Kind = maximized ? PackIconKind.WindowRestore : PackIconKind.WindowMaximize;
            MaximizeButton.ToolTip = maximized ? "Taasta" : "Maksimeeri";
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            foreach (var wire in _observed) wire.PropertyChanged -= Wire_PropertyChanged;
            _observed.Clear();
            _statusTimer?.Stop();
            _createWireTypesHandler.Completed = null; // a late result falls back to a TaskDialog
            _createWireTypesEvent?.Dispose();
            DataContext = null;
        }

        // ================================================================== INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
