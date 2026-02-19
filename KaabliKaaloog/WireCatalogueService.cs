using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Windows;
using System.Diagnostics; // ✅ Debugging
using System.ComponentModel;
using KaabliKataloog.Models;
#if NET48
using System.Data.OleDb;
using DbConn = System.Data.OleDb.OleDbConnection;
using DbCmd = System.Data.OleDb.OleDbCommand;
using DbReader = System.Data.OleDb.OleDbDataReader;
#else
using System.Data.OleDb; // Use OleDb under .NET 8 in Revit host
using DbConn = System.Data.OleDb.OleDbConnection;
using DbCmd = System.Data.OleDb.OleDbCommand;
using DbReader = System.Data.OleDb.OleDbDataReader;
#endif

namespace KaabliKataloog.Services
{
    public class WireCatalogueService
    {
        private readonly string _databasePath =
            $@"C:\Users\{Environment.UserName}\EULE Dropbox\0_EULE  Team folder (kogu kollektiiv)\02_EULE REVIT TEMPLATE\099-scriptid\Pluginad\Installerid\EL_Kaablid.accdb";

        private List<WireData> _allWires = new List<WireData>();

        public List<string> ConductorCounts { get; private set; } = new List<string>();
        public List<string> WireSizes { get; private set; } = new List<string>();
        public List<WireData> GetAllWires() => _allWires;

        public WireCatalogueService()
        {
            LoadWireData(); // ✅ Load all tables on initialization
        }

        // ---------- Provider-agnostic helpers ----------

        private string BuildOleDbConnectionString(string path, string provider)
            => $@"Provider={provider};Data Source={path};Persist Security Info=False;";

        private string BuildOdbcConnectionString(string path)
            => $@"Driver={{Microsoft Access Driver (*.mdb, *.accdb)}};Dbq={path};Uid=Admin;Pwd=;";

        private DbConn TryOpenOleDb(string path, string provider)
        {
            try
            {
                var cs = BuildOleDbConnectionString(path, provider);
                var conn = new DbConn(cs);
                conn.Open();
                return conn;
            }
            catch
            {
                return null;
            }
        }

        private System.Data.Odbc.OdbcConnection TryOpenOdbc(string path)
        {
            try
            {
                var cs = BuildOdbcConnectionString(path);
                var conn = new System.Data.Odbc.OdbcConnection(cs);
                conn.Open();
                return conn;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Tries ACE 16.0 → ACE 12.0 → ODBC. Returns an open connection wrapped as DbConnection.
        /// </summary>
        private DbConnection OpenBestConnection(string path)
        {
            // Try OleDb (ACE 16)
            var ace16 = TryOpenOleDb(path, "Microsoft.ACE.OLEDB.16.0");
            if (ace16 != null) return ace16;

            // Fallback: OleDb (ACE 12)
            var ace12 = TryOpenOleDb(path, "Microsoft.ACE.OLEDB.12.0");
            if (ace12 != null) return ace12;

            // Fallback: ODBC driver (still requires Access Database Engine, but sometimes available even if ACE OleDb isn’t)
            var odbc = TryOpenOdbc(path);
            if (odbc != null) return odbc;

            // If we’re here, nothing opened
            throw new InvalidOperationException(
                "Access provider not available (ACE 16.0 / ACE 12.0 / ODBC). Install the 64-bit Microsoft Access Database Engine or enable the ODBC driver.");
        }


        private string BuildConnectionString(string path)
        {
#if NET48
            return $@"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={path};Persist Security Info=False;";
#else
            // Use ACE 16.0 (2016 x64). Install Access Database Engine x64 on the machine.
            return $@"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={path};Persist Security Info=False;";
#endif
        }
        private DbConn OpenConnection(string path)
        {
            var cs = BuildConnectionString(path);
            var conn = new DbConn(cs);
            conn.Open();
            return conn;
        }

        private List<string> GetTableNames(DbConnection connection)
        {
            var tableNames = new List<string>();
            try
            {
                // Use ADO.NET schema to list user tables and exclude system tables
                DataTable schemaTable = connection.GetSchema("Tables");
                foreach (DataRow row in schemaTable.Rows)
                {
                    string tableName = row["TABLE_NAME"].ToString();
                    string tableType = row.Table.Columns.Contains("TABLE_TYPE")
                        ? row["TABLE_TYPE"]?.ToString()
                        : string.Empty;

                    // Skip system tables and views
                    if (!tableName.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(tableType, "SYSTEM TABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        tableNames.Add(tableName);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Tabelite nimede laadimisel tekkis viga: {ex.Message}", "Andmebaasi viga", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return tableNames;
        }
        private DbCommand CreateDbCommand(string sql, DbConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd;
        }

        private void LoadWireData()
        {
            try
            {
                using (var connection = OpenBestConnection(_databasePath))
                {
                    var tableNames = GetTableNames(connection);

                    foreach (string table in tableNames)
                    {
                        string query = $"SELECT * FROM [{table}]";
                        using (var command = CreateDbCommand(query, connection))
                        using (var reader = command.ExecuteReader())
                        {
                            string currentCapacityColumn = GetMatchingColumnName(reader, new List<string> { "Koormusvool", "A", "Amp", "Ampacity" });
                            string conductorCountColumn = GetMatchingColumnName(reader, new List<string> { "Soonte arv", "Conductor Count", "Conductors" });
                            string wireSizeColumn = GetMatchingColumnName(reader, new List<string> { "Juhi rislõige", "Wire Size", "Size (mm2)" });
                            string uvKindelColumn = GetMatchingColumnName(reader, new List<string> { "UV Kindel", "UV Resistant" });
                            string koosKaitsejuhigaColumn = GetMatchingColumnName(reader, new List<string> { "Koos kaitsejuhiga", "With Protective Conductor" });
                            string välisläbimõõtColumn = GetMatchingColumnName(reader, new List<string> { "Välisläbimõõt", "Outer Diameter" });
                            string juhikategooriaColumn = GetMatchingColumnName(reader, new List<string> { "Juhi kategooria", "Conductor Category" });
                            string painderaadiusColumn = GetMatchingColumnName(reader, new List<string> { "Min Painderaadius", "Min Bend Radius" });

                            while (reader.Read())
                            {
                                _allWires.Add(new WireData
                                {
                                    WireName = table,
                                    Material = GetColumnValue(reader, "Voolujuhi materjal"),
                                    FireReactionClass = GetColumnValue(reader, "Tulereaktsiooni klass"),
                                    HalogenFree = GetColumnValue(reader, "Halogeenivaba"),
                                    UvKindel = GetColumnValue(reader, uvKindelColumn),
                                    KoosKaitsejuhiga = GetColumnValue(reader, koosKaitsejuhigaColumn),
                                    ConductorCount = GetColumnValue(reader, conductorCountColumn),
                                    WireSize = GetColumnValue(reader, wireSizeColumn),
                                    Välisläbimõõt = GetColumnValue(reader, välisläbimõõtColumn),
                                    Painderaadius = GetColumnValue(reader, painderaadiusColumn),
                                    JuhiKategooria = GetColumnValue(reader, juhikategooriaColumn),
                                    CurrentCapacity = GetDoubleColumnValue(reader, currentCapacityColumn)
                                });
                            }
                        }
                    }
                }

                // ✅ Apply sorting
                ConductorCounts = SortMixedValues(_allWires.Select(w => w.ConductorCount).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList());
                WireSizes = SortMixedValues(_allWires.Select(w => w.WireSize).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList());

                Debug.WriteLine($"Total Wires Loaded: {_allWires.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kaabliandmete laadimisel tekkis viga: {ex.Message}", "Andmebaasi viga", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- Sorting & value helpers (unchanged behavior) ----------

        private List<string> SortMixedValues(List<string> values)
            => values.OrderBy(v => v, new NumericComparer()).ToList();

        public class NumericComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == null || y == null) return 0;

                string xNormalized = x.Replace(",", ".");
                string yNormalized = y.Replace(",", ".");

                if (double.TryParse(xNormalized, out double numX) && double.TryParse(yNormalized, out double numY))
                    return numX.CompareTo(numY);

                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Use DbDataReader so it works with both OleDbDataReader and OdbcDataReader
        private string GetColumnValue(DbDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? "N/A" : reader.GetValue(ordinal)?.ToString() ?? "N/A";
            }
            catch (IndexOutOfRangeException)
            {
                return "N/A";
            }
        }

        private double GetDoubleColumnValue(DbDataReader reader, string columnName)
        {
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal)) return 0.0;

                object val = reader.GetValue(ordinal);
                if (val is double d) return d;
                if (val is float f) return f;
                if (val is decimal m) return (double)m;

                // Handle strings with comma decimals
                if (val is string s && double.TryParse(s.Replace(",", "."), out double parsed))
                    return parsed;

                return Convert.ToDouble(val);
            }
            catch (IndexOutOfRangeException)
            {
                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private string GetMatchingColumnName(DbDataReader reader, List<string> possibleNames)
        {
            try
            {
                DataTable schemaTable = reader.GetSchemaTable();
                if (schemaTable == null) return "Unknown";

                List<string> columnNames = schemaTable.Rows.Cast<DataRow>()
                    .Select(row => row["ColumnName"].ToString())
                    .ToList();

                foreach (string possibleName in possibleNames)
                {
                    string match = columnNames.FirstOrDefault(name =>
                        name.IndexOf(possibleName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!string.IsNullOrEmpty(match))
                        return match;
                }
            }
            catch
            {
                return "Unknown";
            }
            return "Unknown";
        }

        // ---------- Public API (unchanged behavior) ----------

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
                .Where(w => string.IsNullOrEmpty(material) || w.Material.Equals(material, StringComparison.OrdinalIgnoreCase))
                .Where(w => string.IsNullOrEmpty(reactionClass) || w.FireReactionClass.Equals(reactionClass, StringComparison.OrdinalIgnoreCase))
                .Where(w => string.IsNullOrEmpty(uvKindel) || w.UvKindel.Equals(uvKindel, StringComparison.OrdinalIgnoreCase))
                .Where(w => string.IsNullOrEmpty(koosKaitsejuhiga) || w.KoosKaitsejuhiga.Equals(koosKaitsejuhiga, StringComparison.OrdinalIgnoreCase))
                .Where(w => string.IsNullOrEmpty(conductorCount) || w.ConductorCount.Equals(conductorCount, StringComparison.OrdinalIgnoreCase))
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
