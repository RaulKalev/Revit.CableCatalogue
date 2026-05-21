using System;
using System.IO;
using Newtonsoft.Json;
using KaabliKataloog.Models;

namespace KaabliKataloog.Services
{
    public static class AppConfigService
    {
        private const string ConfigFilePath = @"C:\ProgramData\RK Tools\KaabliKataloog\config.json";

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);
                    if (config != null) return config;
                }
            }
            catch { /* use defaults on any read failure */ }

            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath));
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch { /* ignore save failures silently */ }
        }
    }
}
