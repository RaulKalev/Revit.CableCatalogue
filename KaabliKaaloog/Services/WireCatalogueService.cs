using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Diagnostics;
using KaabliKataloog.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KaabliKataloog.Services
{
    public class WireCatalogueService
    {
        // Relative sub-path inside the Dropbox root to the shared catalogue file.
        private const string DropboxRelativePath =
            @"0_EULE  Team folder (kogu kollektiiv)\02_EULE REVIT TEMPLATE\cables.json";

        private List<WireData> _allWires = new List<WireData>();

        /// <summary>
        /// Returns the resolved path to cables.json:
        ///   1. User-configured path saved in config.json
        ///   2. Auto-detected from Dropbox info.json (any account type)
        ///   3. null if neither source is available
        /// </summary>
        private string GetJsonPath()
        {
            // 1 — user override stored in config
            var cfg = AppConfigService.Load();
            if (!string.IsNullOrWhiteSpace(cfg.CablesJsonPath))
                return cfg.CablesJsonPath;

            // 2 — auto-detect Dropbox root from Dropbox's own info.json
            string detected = TryDetectDropboxPath();
            if (detected != null)
                return detected;

            return null;
        }

        /// <summary>Reads Dropbox's info.json to find the Dropbox root folder.</summary>
        private string TryDetectDropboxPath()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),  "Dropbox", "info.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dropbox", "info.json")
            };

            foreach (var infoFile in candidates)
            {
                if (!File.Exists(infoFile)) continue;
                try
                {
                    var obj = JObject.Parse(File.ReadAllText(infoFile));
                    // Try every account type (business, personal, etc.)
                    foreach (var token in obj.Children<JProperty>())
                    {
                        var root = token.Value?["path"]?.ToString();
                        if (string.IsNullOrEmpty(root)) continue;
                        var candidate = Path.Combine(root, DropboxRelativePath);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
                catch { /* malformed info.json — skip */ }
            }
            return null;
        }

        /// <summary>
        /// Persist a user-chosen path and reload the catalogue.
        /// Pass null to clear the override (reverts to auto-detect).
        /// </summary>
        public void SetCablesJsonPath(string path)
        {
            var cfg = AppConfigService.Load();
            cfg.CablesJsonPath = string.IsNullOrWhiteSpace(path) ? null : path;
            AppConfigService.Save(cfg);
            Reload();
        }

        /// <summary>Returns the currently active path (resolved), or null if unresolvable.</summary>
        public string GetCurrentJsonPath() => GetJsonPath();

        public List<string> ConductorCounts { get; private set; } = new List<string>();
        public List<string> WireSizes { get; private set; } = new List<string>();

        public List<WireData> GetAllWires() => _allWires;

        public WireCatalogueService()
        {
            LoadFromJson();
        }

        // Public mutating API

        public void AddWire(WireData wire)
        {
            _allWires.Add(wire);
            RefreshSortedLists();
        }

        public void RemoveWire(WireData wire)
        {
            _allWires.Remove(wire);
            RefreshSortedLists();
        }

        /// <summary>Writes the catalogue to cables.json. Returns false (after telling the user why) when it could not.</summary>
        public bool SaveToJson()
        {
            var path = GetJsonPath();
            if (path == null)
            {
                MessageBox.Show("cables.json asukoht on määramata. Palun sea faili asukoht seadetest.", "Viga", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var json = JsonConvert.SerializeObject(_allWires, Formatting.Indented);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Andmete salvestamine ebaonnestus: {ex.Message}", "Viga", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void Reload()
        {
            _allWires.Clear();
            LoadFromJson();
        }

        // Import from Access (.accdb) - net48 only

#if NET48
        public int ImportFromAccess(string accdbPath)
        {
            var imported = new List<WireData>();

            using (var connection = OpenBestConnection(accdbPath))
            {
                var tableNames = GetTableNames(connection);

                foreach (string table in tableNames)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = $"SELECT * FROM [{table}]";
                        using (var reader = command.ExecuteReader())
                        {
                            string currentCapacityColumn = GetMatchingColumnName(reader, new List<string> { "Koormusvool", "A", "Amp", "Ampacity" });
                            string conductorCountColumn   = GetMatchingColumnName(reader, new List<string> { "Soonte arv", "Conductor Count", "Conductors" });
                            string wireSizeColumn         = GetMatchingColumnName(reader, new List<string> { "Juhi risl", "Juhi ristl", "rislõige", "ristlõige", "Wire Size", "Size (mm2)" });
                            string uvKindelColumn         = GetMatchingColumnName(reader, new List<string> { "UV Kindel", "UV Resistant" });
                            string koosKaitsejuhigaColumn = GetMatchingColumnName(reader, new List<string> { "Koos kaitsejuhiga", "With Protective Conductor" });
                            string välisläbimootColumn    = GetMatchingColumnName(reader, new List<string> { "Välisläbim", "läbimõõt", "läbimoot", "Outer Diameter", "Diameeter" });
                            string juhikategooriaColumn   = GetMatchingColumnName(reader, new List<string> { "Juhi kategooria", "Conductor Category" });
                            string painderaadiusColumn    = GetMatchingColumnName(reader, new List<string> { "Min Painderaadius", "Painderaadius", "Min Bend Radius" });

                            while (reader.Read())
                            {
                                imported.Add(new WireData
                                {
                                    WireName          = table,
                                    Material          = GetColumnValue(reader, "Voolujuhi materjal"),
                                    FireReactionClass = GetColumnValue(reader, "Tulereaktsiooni klass"),
                                    HalogenFree       = GetColumnValue(reader, "Halogeenivaba"),
                                    UvKindel          = GetColumnValue(reader, uvKindelColumn),
                                    KoosKaitsejuhiga  = GetColumnValue(reader, koosKaitsejuhigaColumn),
                                    ConductorCount    = GetColumnValue(reader, conductorCountColumn),
                                    WireSize          = GetColumnValue(reader, wireSizeColumn),
                                    Välisläbimõõt     = GetColumnValue(reader, välisläbimootColumn),
                                    Painderaadius     = GetColumnValue(reader, painderaadiusColumn),
                                    JuhiKategooria    = GetColumnValue(reader, juhikategooriaColumn),
                                    CurrentCapacity   = GetDoubleColumnValue(reader, currentCapacityColumn)
                                });
                            }
                        }
                    }
                }
            }

            _allWires = imported;
            RefreshSortedLists();
            return imported.Count;
        }

        private System.Data.Common.DbConnection OpenBestConnection(string path)
        {
            var errors = new List<string>();
            var ace16 = TryOpenOleDb(path, "Microsoft.ACE.OLEDB.16.0", errors);
            if (ace16 != null) return ace16;
            var ace12 = TryOpenOleDb(path, "Microsoft.ACE.OLEDB.12.0", errors);
            if (ace12 != null) return ace12;
            var odbc = TryOpenOdbc(path, errors);
            if (odbc != null) return odbc;
            throw new InvalidOperationException("Access provider not available. Errors:\n" + string.Join("\n", errors));
        }

        private System.Data.OleDb.OleDbConnection TryOpenOleDb(string path, string provider, List<string> errors)
        {
            try
            {
                var conn = new System.Data.OleDb.OleDbConnection($@"Provider={provider};Data Source={path};Persist Security Info=False;");
                conn.Open();
                return conn;
            }
            catch (Exception ex) { errors.Add($"[{provider}] {ex.Message}"); return null; }
        }

        private System.Data.Odbc.OdbcConnection TryOpenOdbc(string path, List<string> errors)
        {
            try
            {
                var conn = new System.Data.Odbc.OdbcConnection($@"Driver={{Microsoft Access Driver (*.mdb, *.accdb)}};Dbq={path};Uid=Admin;Pwd=;");
                conn.Open();
                return conn;
            }
            catch (Exception ex) { errors.Add($"[ODBC] {ex.Message}"); return null; }
        }

        private List<string> GetTableNames(System.Data.Common.DbConnection connection)
        {
            var tableNames = new List<string>();
            try
            {
                var schemaTable = connection.GetSchema("Tables");
                foreach (System.Data.DataRow row in schemaTable.Rows)
                {
                    string tableName = row["TABLE_NAME"].ToString();
                    string tableType = schemaTable.Columns.Contains("TABLE_TYPE") ? row["TABLE_TYPE"]?.ToString() : string.Empty;
                    if (!tableName.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(tableType, "SYSTEM TABLE", StringComparison.OrdinalIgnoreCase))
                        tableNames.Add(tableName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Tabelite nimede laadimisel tekkis viga: {ex.Message}", "Andmebaasi viga", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return tableNames;
        }

        private string GetColumnValue(System.Data.Common.DbDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? "N/A" : reader.GetValue(ordinal)?.ToString() ?? "N/A";
            }
            catch (IndexOutOfRangeException) { return "N/A"; }
        }

        private double GetDoubleColumnValue(System.Data.Common.DbDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal)) return 0.0;
                object val = reader.GetValue(ordinal);
                if (val is double d) return d;
                if (val is float f) return f;
                if (val is decimal m) return (double)m;
                if (val is string s && double.TryParse(s.Replace(",", "."), out double parsed)) return parsed;
                return Convert.ToDouble(val);
            }
            catch { return 0.0; }
        }

        private string GetMatchingColumnName(System.Data.Common.DbDataReader reader, List<string> possibleNames)
        {
            try
            {
                var schemaTable = reader.GetSchemaTable();
                if (schemaTable == null) return "Unknown";
                var columnNames = schemaTable.Rows.Cast<System.Data.DataRow>().Select(r => r["ColumnName"].ToString()).ToList();
                foreach (string name in possibleNames)
                {
                    string match = columnNames.FirstOrDefault(c => c.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!string.IsNullOrEmpty(match)) return match;
                }
            }
            catch { }
            return "Unknown";
        }
#else
        public int ImportFromAccess(string accdbPath)
        {
            throw new NotSupportedException(
                "Import from Access on toetatud ainult Revit 2024 (net48). " +
                "Palun kasuta importimiseks Revit 2024.");
        }
#endif

        // JSON persistence

        private void LoadFromJson()
        {
            var path = GetJsonPath();
            if (path == null)
            {
                Debug.WriteLine("cables.json path could not be resolved - starting with empty catalogue.");
                return;
            }
            try
            {
                if (!File.Exists(path))
                {
                    Debug.WriteLine($"cables.json not found at '{path}' - starting with empty catalogue.");
                    return;
                }
                var json = File.ReadAllText(path);
                _allWires = JsonConvert.DeserializeObject<List<WireData>>(json) ?? new List<WireData>();
                RefreshSortedLists();
                foreach (var w in _allWires) w.ClearDirty();
                Debug.WriteLine($"Loaded {_allWires.Count} wires from '{path}'");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kaabliandmete laadimisel tekkis viga: {ex.Message}", "Viga", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Sorting helpers

        private void RefreshSortedLists()
        {
            ConductorCounts = SortMixedValues(_allWires.Select(w => w.ConductorCount).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList());
            WireSizes       = SortMixedValues(_allWires.Select(w => w.WireSize).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList());
        }

        private List<string> SortMixedValues(List<string> values)
            => values.OrderBy(v => v, new NumericComparer()).ToList();

        public class NumericComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == null || y == null) return 0;
                if (double.TryParse(x.Replace(",", "."), out double nx) && double.TryParse(y.Replace(",", "."), out double ny))
                    return nx.CompareTo(ny);
                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Public filter API (unchanged signatures)

        public List<WireData> GetFilteredWires(string material, string reactionClass, string uvKindel, string koosKaitsejuhiga, string conductorCount, string wireSize)
        {
            return _allWires
                .Where(w => w.Material.Equals(material, StringComparison.OrdinalIgnoreCase))
                .Where(w => w.FireReactionClass.Equals(reactionClass, StringComparison.OrdinalIgnoreCase))
                .Where(w => w.UvKindel.Equals(uvKindel, StringComparison.OrdinalIgnoreCase))
                .Where(w => w.KoosKaitsejuhiga.Equals(koosKaitsejuhiga, StringComparison.OrdinalIgnoreCase))
                .Where(w => w.ConductorCount.Equals(conductorCount, StringComparison.OrdinalIgnoreCase))
                .Where(w => w.WireSize.Equals(wireSize, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public List<string> GetFilteredWireSizes(string material, string reactionClass, string uvKindel, string koosKaitsejuhiga, string conductorCount)
        {
            return _allWires
                .Where(w => string.IsNullOrEmpty(material)         || w.Material.Equals(material, StringComparison.OrdinalIgnoreCase))
                .Where(w => string.IsNullOrEmpty(reactionClass)    || w.FireReactionClass.Equals(reactionClass, StringComparison.OrdinalIgnoreCase))
                .Where(w => string.IsNullOrEmpty(uvKindel)         || w.UvKindel.Equals(uvKindel, StringComparison.OrdinalIgnoreCase))
                .Where(w => string.IsNullOrEmpty(koosKaitsejuhiga) || w.KoosKaitsejuhiga.Equals(koosKaitsejuhiga, StringComparison.OrdinalIgnoreCase))
                .Where(w => string.IsNullOrEmpty(conductorCount)   || w.ConductorCount.Equals(conductorCount, StringComparison.OrdinalIgnoreCase))
                .Select(w => w.WireSize)
                .Distinct()
                .Where(s => !string.IsNullOrEmpty(s))
                .OrderBy(s => s, new NumericComparer())
                .ToList();
        }

        public List<string> GetUniqueMaterials()
            => SortMixedValues(_allWires.Select(w => w.Material).Distinct().ToList());

        public List<string> GetUniqueFireReactionClasses()
            => SortMixedValues(_allWires.Select(w => w.FireReactionClass).Distinct().ToList());
    }
}
