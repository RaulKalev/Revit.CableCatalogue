namespace KaabliKataloog.Models
{
    public class AppConfig
    {
        public bool IsDarkMode { get; set; } = true;

        /// <summary>
        /// User-overridden path to cables.json.
        /// Null means "auto-detect from Dropbox".
        /// </summary>
        public string CablesJsonPath { get; set; }
    }
}
