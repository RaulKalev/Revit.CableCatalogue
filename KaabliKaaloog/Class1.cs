using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Windows;

namespace KaabliKataloog
{
    [Transaction(TransactionMode.Manual)]
    public class Class1 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Access the current Revit document and active view
                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                Document doc = uiDoc.Document;
                View currentView = doc.ActiveView;

                // Show the MainWindow with the required parameters
                MainWindow mainWindow = new MainWindow(uiDoc, doc, currentView);
                mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                // Display the window modelessly
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(mainWindow)
                {
                    Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle
                };

                mainWindow.Show(); // Use Show() instead of ShowDialog()

                // Return success if everything goes well
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // If an error occurs, display a message and return Failed
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
