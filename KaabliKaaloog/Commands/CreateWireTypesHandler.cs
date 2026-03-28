using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KaabliKataloog
{
    public class CreateWireTypesHandler : IExternalEventHandler
    {
        public UIDocument UiDoc { get; set; }
        public Document Doc { get; set; }
        public List<string> WireTypeNamesToCreate { get; set; } = new List<string>();

        public void Execute(UIApplication app)
        {
            if (WireTypeNamesToCreate == null || !WireTypeNamesToCreate.Any())
                return;

            List<string> createdWireTypes = new List<string>();

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
                    TaskDialog.Show("Viga", "Ühtegi olemasolevat kaablitüüpi ei leitud, mida kopeerida.");
                    return;
                }

                foreach (string typeName in WireTypeNamesToCreate)
                {
                    bool exists = new FilteredElementCollector(Doc)
                        .OfClass(typeof(WireType))
                        .Cast<WireType>()
                        .Any(wt => wt.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                    if (!exists)
                    {
                         baseWireType.Duplicate(typeName);
                         createdWireTypes.Add(typeName + " (Wire)");
                    }
                }

#if !NET48
                // Revit 2026: Also create CableType
                CableType baseCableType = new FilteredElementCollector(Doc)
                    .OfClass(typeof(CableType))
                    .Cast<CableType>()
                    .FirstOrDefault();

                if (baseCableType != null)
                {
                    foreach (string typeName in WireTypeNamesToCreate)
                    {
                        bool exists = new FilteredElementCollector(Doc)
                            .OfClass(typeof(CableType))
                            .Cast<CableType>()
                            .Any(ct => ct.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                        if (!exists)
                        {
                            baseCableType.Duplicate(typeName);
                            createdWireTypes.Add(typeName + " (Cable)");
                        }
                    }
                }
#endif

                tx.Commit();
            }

            if (createdWireTypes.Count > 0)
            {
                string message = $"Loodi {createdWireTypes.Count} tüüpi:\n\n" +
                                 string.Join("\n", createdWireTypes);
                TaskDialog.Show("Tüübid loodud", message);
            }
            else
            {
                TaskDialog.Show("Uusi tüüpe ei loodud", "Kõik valitud kaablitüübid on juba olemas.");
            }
        }

        public string GetName() => "CreateWireTypesHandler";
    }
}
