
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using Newtonsoft.Json;
using Autodesk.Revit.DB;
using System.Windows.Media;
using System.Linq;
using Autodesk.Revit.UI.Selection;
using System.Runtime.InteropServices;
using Autodesk.Revit.UI.Events;
using Autodesk.Revit.DB.Electrical;
using System.ComponentModel;
using System.Windows.Media.Animation;
using KaabliKataloog.Models;
using KaabliKataloog.Services;



using System.Collections.ObjectModel;
using System.Diagnostics;


namespace KaabliKataloog
{
    public partial class MainWindow : Window
    {

        private const string ConfigFilePath = @"C:\ProgramData\RK Tools\KaabliKataloog\config.json";
        private readonly WindowResizer _windowResizer;
        private ThemeManager _themeManager;
        private UIDocument _uiDoc;
        private Document _doc;
        private View _currentView;
        // Store selected element coordinates in memory
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private readonly WireCatalogueService _wireCatalogueService;
        public List<string> FireReactionClasses { get; set; } // 🔹 Bind to XAML ComboBox
        private ObservableCollection<WireData> _filteredWireResults = new ObservableCollection<WireData>();
        public ObservableCollection<WireData> FilteredWireResults
        {
            get => _filteredWireResults;
            set
            {
                _filteredWireResults = value;
                OnPropertyChanged(nameof(FilteredWireResults));
            }
        }
        private ExternalEvent _createWireTypesEvent;
        private CreateWireTypesHandler _createWireTypesHandler;


        public MainWindow(UIDocument uiDoc, Document doc, View currentView)

        {
            InitializeComponent();
            _uiDoc = uiDoc;
            _doc = doc;
            _currentView = currentView;
            _wireCatalogueService = new WireCatalogueService();
            Loaded += MainWindow_Loaded; // Hook up the Loaded event
            _createWireTypesHandler = new CreateWireTypesHandler
            {
                UiDoc = _uiDoc,
                Doc = _doc
            };
            _createWireTypesEvent = ExternalEvent.Create(_createWireTypesHandler);

            Topmost = false;

            this.Closed += MainWindow_Closed;

            _windowResizer = new WindowResizer(this);

            // Global mouse events for resizing
            this.MouseMove += Window_MouseMove;
            this.MouseLeftButtonUp += Window_MouseLeftButtonUp;

            if (Application.ResourceAssembly == null)
            {
                Application.ResourceAssembly = Assembly.GetExecutingAssembly();
            }

            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            

            _themeManager = new ThemeManager(this);
            _themeManager.LoadThemeState(); // ✅ Load the saved theme state
            ThemeToggleButton.IsChecked = _themeManager.IsDarkMode;


            LoadDropdownOptions();
            LoadMaterialOptions();
            LoadFireReactionClasses();


            DataContext = this; // Bind the data context to the MainWindow instance

            // ✅ Force a UI update AFTER everything is set up
            Dispatcher.BeginInvoke(new Action(() => OnInputChanged(null, null)), System.Windows.Threading.DispatcherPriority.Background);

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
            if (_wireCatalogueService == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ _wireCatalogueService is NULL. Skipping LoadDropdownOptions.");
                return;
            }

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

            // Grab user selections
            string material = MaterialComboBox.SelectedItem?.ToString() ?? "";
            string reactionClass = ReactionClassComboBox.SelectedItem?.ToString() ?? "";
            string uvKindel = UvKindelCheckBox.IsChecked == true ? "Jah" : "";
            string koosKaitsejuhiga = KoosKaitsejuhigaCheckBox.IsChecked == true ? "Jah" : "";
            string conductorCount = ConductorCountComboBox.SelectedItem?.ToString() ?? "";
            string wireSize = WireSizeComboBox.SelectedItem?.ToString() ?? "";

            // Dynamically filter valid wire sizes
            var validWireSizes = _wireCatalogueService.GetFilteredWireSizes(material, reactionClass, uvKindel, koosKaitsejuhiga, conductorCount);
            UpdateWireSizeDropdown(validWireSizes);

            // Only use selected wire size if it’s still valid
            if (!validWireSizes.Contains(wireSize))
            {
                wireSize = "";
            }

            // ✅ Save currently selected wires by DisplayName
            var previouslySelected = FilteredWireResults
                .Where(w => w.IsSelected)
                .Select(w => w.DisplayName)
                .ToHashSet();

            // 🚀 Always query against the full database
            var allWires = _wireCatalogueService.GetAllWires();

            // Apply filtering
            var filteredWires = allWires
                .Where(w =>
                    (string.IsNullOrEmpty(material) || w.Material == material) &&
                    (string.IsNullOrEmpty(reactionClass) || w.FireReactionClass == reactionClass) &&
                    (string.IsNullOrEmpty(uvKindel) || w.UvKindel == uvKindel) &&
                    (string.IsNullOrEmpty(koosKaitsejuhiga) || w.KoosKaitsejuhiga == koosKaitsejuhiga) &&
                    (string.IsNullOrEmpty(conductorCount) || w.ConductorCount == conductorCount) &&
                    (string.IsNullOrEmpty(wireSize) || w.WireSize == wireSize))
                .ToList();

            // 🧠 Refresh the observable collection, restoring IsSelected where applicable
            foreach (var wire in filteredWires)
            {
                wire.IsSelected = previouslySelected.Contains(wire.DisplayName);
            }
            FilteredWireResults.Clear();
            foreach (var wire in filteredWires)
            {
                // Restore selection state if it was previously selected
                wire.IsSelected = previouslySelected.Contains(wire.DisplayName);
                FilteredWireResults.Add(wire);
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

            // Only re-select previous value if we’re not intentionally resetting
            if (!string.IsNullOrEmpty(prevWireSize) && validWireSizes.Contains(prevWireSize))
            {
                WireSizeComboBox.SelectedItem = prevWireSize;
            }
            else
            {
                WireSizeComboBox.SelectedItem = null; // 👈 this keeps it empty if reset by user
            }

            System.Diagnostics.Debug.WriteLine($"Updated Wire Sizes: {string.Join(", ", validWireSizes)}");
        }

        private void CreateWireTypesButton_Click(object sender, RoutedEventArgs e)
        {
            int countSelected = 0;
            foreach (var wire in FilteredWireResults)
            {
                Debug.WriteLine($"[CreateWireTypes] {wire.DisplayName} | Selected: {wire.IsSelected}");
                if (wire.IsSelected) countSelected++;
            }

            if (countSelected == 0)
            {
                MessageBox.Show("Palun vali vähemalt üks kaabel (✓)!", "Teade", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedWireNames = FilteredWireResults
                .Where(w => w.IsSelected)
                .Select(w => w.DisplayName)
                .Distinct()
                .ToList();

            _createWireTypesHandler.WireTypeNamesToCreate = selectedWireNames;
            _createWireTypesEvent.Raise();
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _themeManager.ToggleTheme();
            _themeManager.SaveThemeState(); // ✅ Save after toggle
            ThemeToggleButton.IsChecked = _themeManager.IsDarkMode;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _themeManager.ApplyTheme();

            // 💡 Preload all wires into the DataGrid
            FilteredWireResults.Clear();
            foreach (var wire in _wireCatalogueService.GetAllWires())
            {
                FilteredWireResults.Add(wire);
            }
        }



        private void MainWindow_Closed(object sender, System.EventArgs e)
        {

            _themeManager.SaveThemeState();

        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = System.Windows.WindowState.Minimized;
        private void LeftEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeWE; // Horizontal resize cursor for Left Edge
        private void RightEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeWE; // Horizontal resize cursor for Right Edge
        private void BottomEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNS; // Vertical resize cursor for Bottom Edge
        private void Edge_MouseLeave(object sender, MouseEventArgs e) => this.Cursor = Cursors.Arrow; // Reset to default cursor
        private void BottomLeftCorner_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNESW; // Diagonal resize cursor (↙) for Bottom-Left Corner
        private void BottomRightCorner_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNWSE; // Diagonal resize cursor (↘) for Bottom-Right Corner
        private void Window_MouseMove(object sender, MouseEventArgs e) => _windowResizer.ResizeWindow(e);
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _windowResizer.StopResizing();
        private void LeftEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Left);
        private void RightEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Right);
        private void BottomEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Bottom);
        private void BottomLeftCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomLeft);
        private void BottomRightCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomRight);

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // Unregister any event handlers to prevent memory leaks
            this.Closed -= MainWindow_Closed;
            this.MouseMove -= Window_MouseMove;
            this.MouseLeftButtonUp -= Window_MouseLeftButtonUp;

            // Clear DataContext to release bound objects
            DataContext = null;

            // Call GC to force garbage collection (optional, not always necessary)
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        private void MaterialComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ReactionClassComboBox.SelectedItem = null;
            ConductorCountComboBox.SelectedItem = null;
            WireSizeComboBox.SelectedItem = null;
            OnInputChanged(sender, e);
        }

        private void ReactionClassComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ConductorCountComboBox.SelectedItem = null;
            WireSizeComboBox.SelectedItem = null;
            OnInputChanged(sender, e);
        }
        private void ConductorCountComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            WireSizeComboBox.SelectedItem = null;
            OnInputChanged(sender, e);
        }

    }
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


}