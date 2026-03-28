using System;
using System.ComponentModel;
using System.Diagnostics;
using Newtonsoft.Json;

namespace KaabliKataloog.Models
{
    public class WireData : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isDirty;
        private string _wireName;
        private string _material;
        private string _fireReactionClass;
        private string _halogenFree;
        private string _conductorCount;
        private string _wireSize;
        private string _uvKindel;
        private string _koosKaitsejuhiga;
        private double _currentCapacity;
        private string _välisläbimõõt;
        private string _juhiKategooria;
        private string _painderaadius;

        [JsonIgnore]
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

        public string WireName
        {
            get => _wireName;
            set { if (_wireName != value) { _wireName = value; IsDirty = true; OnPropertyChanged(nameof(WireName)); OnPropertyChanged(nameof(DisplayName)); } }
        }

        public string Material
        {
            get => _material;
            set { if (_material != value) { _material = value; IsDirty = true; OnPropertyChanged(nameof(Material)); } }
        }

        public string FireReactionClass
        {
            get => _fireReactionClass;
            set { if (_fireReactionClass != value) { _fireReactionClass = value; IsDirty = true; OnPropertyChanged(nameof(FireReactionClass)); } }
        }

        public string HalogenFree
        {
            get => _halogenFree;
            set { if (_halogenFree != value) { _halogenFree = value; IsDirty = true; OnPropertyChanged(nameof(HalogenFree)); } }
        }

        public string ConductorCount
        {
            get => _conductorCount;
            set { if (_conductorCount != value) { _conductorCount = value; IsDirty = true; OnPropertyChanged(nameof(ConductorCount)); OnPropertyChanged(nameof(DisplayName)); } }
        }

        public string WireSize
        {
            get => _wireSize;
            set { if (_wireSize != value) { _wireSize = value; IsDirty = true; OnPropertyChanged(nameof(WireSize)); OnPropertyChanged(nameof(DisplayName)); } }
        }

        public string UvKindel
        {
            get => _uvKindel;
            set { if (_uvKindel != value) { _uvKindel = value; IsDirty = true; OnPropertyChanged(nameof(UvKindel)); } }
        }

        public string KoosKaitsejuhiga
        {
            get => _koosKaitsejuhiga;
            set { if (_koosKaitsejuhiga != value) { _koosKaitsejuhiga = value; IsDirty = true; OnPropertyChanged(nameof(KoosKaitsejuhiga)); OnPropertyChanged(nameof(DisplayName)); } }
        }

        public double CurrentCapacity
        {
            get => _currentCapacity;
            set { if (_currentCapacity != value) { _currentCapacity = value; IsDirty = true; OnPropertyChanged(nameof(CurrentCapacity)); } }
        }

        public string Välisläbimõõt
        {
            get => _välisläbimõõt;
            set { if (_välisläbimõõt != value) { _välisläbimõõt = value; IsDirty = true; OnPropertyChanged(nameof(Välisläbimõõt)); } }
        }

        public string JuhiKategooria
        {
            get => _juhiKategooria;
            set { if (_juhiKategooria != value) { _juhiKategooria = value; IsDirty = true; OnPropertyChanged(nameof(JuhiKategooria)); } }
        }

        public string Painderaadius
        {
            get => _painderaadius;
            set { if (_painderaadius != value) { _painderaadius = value; IsDirty = true; OnPropertyChanged(nameof(Painderaadius)); } }
        }

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                string baseName = WireName;
                if (!string.IsNullOrEmpty(baseName))
                {
                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, "pro", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, "firetuf\\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    baseName = baseName.Trim();
                }
                string gOrX = (KoosKaitsejuhiga ?? "").Equals("Jah", System.StringComparison.OrdinalIgnoreCase) ? "G" : "X";
                return $"{baseName} {ConductorCount}{gOrX}{WireSize}";
            }
        }

        [JsonIgnore]
        public bool IsDirty
        {
            get => _isDirty;
            set { if (_isDirty != value) { _isDirty = value; OnPropertyChanged(nameof(IsDirty)); } }
        }

        public void ClearDirty() => IsDirty = false;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

