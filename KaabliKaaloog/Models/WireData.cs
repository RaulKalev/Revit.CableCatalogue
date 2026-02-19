using System;
using System.ComponentModel;
using System.Diagnostics;

namespace KaabliKataloog.Models
{
    public class WireData : INotifyPropertyChanged
    {
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    Debug.WriteLine($"[WireData] {DisplayName} -> IsSelected = {_isSelected}");
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string WireName { get; set; }
        public string Material { get; set; }
        public string FireReactionClass { get; set; }
        public string HalogenFree { get; set; }
        public string ConductorCount { get; set; }
        public string WireSize { get; set; }
        public string UvKindel { get; set; }
        public string KoosKaitsejuhiga { get; set; }
        public double CurrentCapacity { get; set; }
        public string Välisläbimõõt { get; set; }
        public string JuhiKategooria { get; set; }
        public string Painderaadius { get; set; }

        public string DisplayName
        {
            get
            {
                string baseName = WireName;

                if (!string.IsNullOrEmpty(baseName))
                {
                    // Remove case-insensitive "PRO"
                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, "pro", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    // Remove case-insensitive "FIRETUF " (note the space)
                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, "firetuf\\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    baseName = baseName.Trim(); // Clean up leading/trailing spaces
                }

                string gOrX = KoosKaitsejuhiga.Equals("Jah", System.StringComparison.OrdinalIgnoreCase) ? "G" : "X";
                return $"{baseName} {ConductorCount}{gOrX}{WireSize}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
