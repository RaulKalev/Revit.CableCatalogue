using Autodesk.Revit.UI;
using ricaun.Revit.UI;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Newtonsoft.Json;

namespace KaabliKataloog
{
    [AppLoader]
    public class App : IExternalApplication
    {
        private RibbonPanel ribbonPanel;

        public Result OnStartup(UIControlledApplication application)
        {
#if !NET48
            // Register encoding provider for .NET Core / .NET 5+ (Revit 2025/2026)
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // ⚠️ NUCLEAR FIX: Force load the Windows-specific DLLs from the plugin directory
            // This prevents the runtime from resolving to the generic "reference assembly" which throws PlatformNotSupportedException.
            try 
            {
                var assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                var oledbPath = Path.Combine(assemblyDir, "System.Data.OleDb.dll");
                var odbcPath = Path.Combine(assemblyDir, "System.Data.Odbc.dll");

                if (File.Exists(oledbPath)) System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(oledbPath);
                if (File.Exists(odbcPath)) System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(odbcPath);
            }
            catch (Exception ex)
            {
                // Just log to debug, don't crash startup
                Debug.WriteLine($"Error force loading assemblies: {ex.Message}");
            }
#endif
            // Define the custom tab name
            string tabName = "RK Tools";

            // Try to create the custom tab (avoid exception if it already exists)
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
                // Tab already exists; continue without throwing an error
            }

            // Create Ribbon Panel on the custom tab
            ribbonPanel = application.CreateOrSelectPanel(tabName, "EL");

            // Create PushButton with embedded resource
            ribbonPanel.CreatePushButton<Class1>()
                .SetLargeImage("Assets/KaabliKataloog.tiff")
                .SetText("Kaabli\nKataloog")
                .SetToolTip("Kaabli Kataloog on Revitile loodud plugin, mis aitab inseneridel" +
                " ja projekteerijatel valida sobivaima kaabli vastavalt määratud tehnilistele nõuetele.\n" +
                "Plugin võimaldab kasutajal määrata olulisi parameetreid, mille põhjal süsteem filtreerib " +
                "ja valib parima võimaliku kaabli.\n\n" +
                "Funktsionaalsus:\r\n\n" +
                "🔹 Dünaamiline filtreerimine – Kaabli valikud kohanduvad automaatselt vastavalt kasutaja sisestatud kriteeriumitele.\r\n" +
                "🔹 Täpne sorteerimine.\r\n" +
                "🔹 Parameetrite põhjal valik – Kasutaja saab määrata järgmised kriteeriumid:\r\n" +
                "   🔹 Materjal\r\n" +
                "   🔹 Tulekindlusklass\r\n" +
                "   🔹 UV-kindlus\r\n" +
                "   🔹 Soonte arv\r\n" +
                "   🔹 Ristlõige\r\n\n" +
                "Autor ja kontakt:\r\n\n" +
                "Plugin on loodud Raul Kalev-i poolt. " +
                "Kui avastad vigu või puudusi, palun võta ühendust: raul.kalev@eule.ee")
                .SetContextualHelp("https://raulkalev.github.io/rktools/");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // Trigger the update check
            ribbonPanel?.Remove();
            return Result.Succeeded;
        }

    }
}
