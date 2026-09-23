using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KaabliKataloog
{
    /// <summary>What happened when types were created (reported back to the window instead of a TaskDialog).</summary>
    public class CreateWireTypesResult
    {
        public List<string> CreatedWireTypes { get; } = new List<string>();
        public List<string> CreatedCableTypes { get; } = new List<string>();
        /// <summary>Requested names that already existed in the project (nothing was created for them).</summary>
        public List<string> Existing { get; } = new List<string>();
        public string Error { get; set; }

        public int CreatedCount => CreatedWireTypes.Count + CreatedCableTypes.Count;
    }

    public class CreateWireTypesHandler : IExternalEventHandler
    {
        public UIDocument UiDoc { get; set; }
        public Document Doc { get; set; }
        public List<string> WireTypeNamesToCreate { get; set; } = new List<string>();

        /// <summary>
        /// Receives the result (on Revit's UI thread). When nobody listens – the window was closed before Revit got
        /// to the request – the result is shown in a TaskDialog so it is never lost.
        /// </summary>
        public Action<CreateWireTypesResult> Completed { get; set; }

        public void Execute(UIApplication app)
        {
            if (WireTypeNamesToCreate == null || !WireTypeNamesToCreate.Any())
                return;

            var result = new CreateWireTypesResult();
            try
            {
                CreateTypes(result);
            }
            catch (Exception ex)
            {
                result.CreatedWireTypes.Clear();
                result.CreatedCableTypes.Clear();
                result.Error = ex.Message;
            }
            Report(result);
        }

        private void CreateTypes(CreateWireTypesResult result)
        {
            using (Transaction tx = new Transaction(Doc, "Create Wire Types"))
            {
                tx.Start();

                // Get an existing WireType to duplicate from
                WireType baseWireType = new FilteredElementCollector(Doc)
                    .OfClass(typeof(WireType))
                    .Cast<WireType>()
                    .FirstOrDefault();

                if (baseWireType == null)
                {
                    result.Error = "Projektis pole ühtegi juhtmetüüpi, mida kopeerida. Lisa projekti vähemalt üks juhtmetüüp.";
                    tx.RollBack();
                    return;
                }

                var existingWire = new HashSet<string>(
                    new FilteredElementCollector(Doc).OfClass(typeof(WireType)).Select(e => e.Name),
                    StringComparer.OrdinalIgnoreCase);
                var alreadyThere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string typeName in WireTypeNamesToCreate)
                {
                    if (existingWire.Contains(typeName))
                    {
                        alreadyThere.Add(typeName);
                        continue;
                    }
                    baseWireType.Duplicate(typeName);
                    existingWire.Add(typeName);
                    result.CreatedWireTypes.Add(typeName);
                }

#if !NET48
                // Revit 2026: Also create CableType
                CableType baseCableType = new FilteredElementCollector(Doc)
                    .OfClass(typeof(CableType))
                    .Cast<CableType>()
                    .FirstOrDefault();

                if (baseCableType != null)
                {
                    var existingCable = new HashSet<string>(
                        new FilteredElementCollector(Doc).OfClass(typeof(CableType)).Select(e => e.Name),
                        StringComparer.OrdinalIgnoreCase);
                    var cableAlreadyThere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (string typeName in WireTypeNamesToCreate)
                    {
                        if (existingCable.Contains(typeName))
                        {
                            cableAlreadyThere.Add(typeName);
                            continue;
                        }
                        baseCableType.Duplicate(typeName);
                        existingCable.Add(typeName);
                        result.CreatedCableTypes.Add(typeName);
                    }

                    // "Already there" means nothing new at all for that name.
                    alreadyThere.IntersectWith(cableAlreadyThere);
                }
#endif

                result.Existing.AddRange(WireTypeNamesToCreate.Where(alreadyThere.Contains));
                tx.Commit();
            }
        }

        private void Report(CreateWireTypesResult result)
        {
            if (Completed != null)
            {
                Completed(result);
                return;
            }

            if (result.Error != null)
                TaskDialog.Show("Viga", result.Error);
            else if (result.CreatedCount > 0)
                TaskDialog.Show("Tüübid loodud", $"Loodi {result.CreatedCount} tüüpi:\n\n" +
                    string.Join("\n", result.CreatedWireTypes.Select(n => n + " (Wire)")
                        .Concat(result.CreatedCableTypes.Select(n => n + " (Cable)"))));
            else
                TaskDialog.Show("Uusi tüüpe ei loodud", "Kõik valitud kaablitüübid on juba olemas.");
        }

        public string GetName() => "CreateWireTypesHandler";
    }
}
