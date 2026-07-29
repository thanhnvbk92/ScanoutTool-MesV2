using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using ScanOutTool.Models;
using System;
using System.Diagnostics;
using System.IO;

namespace ScanOutTool.Services
{
    public class ConfigService : IConfigService
    {
        private string _configPath;

        public AppConfig Config { get; private set; }

        public ConfigService(IConfiguration configuration)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string exeDirectory = Path.GetDirectoryName(exePath);
            _configPath = configuration["ConfigFilePath"] ?? Path.Combine(exeDirectory, "Resources/appsettings.json");
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    Config = new AppConfig();
                    return;
                }

                var json = File.ReadAllText(_configPath);
                var root = JsonConvert.DeserializeObject<RootConfig>(json);
                Config = root?.AppConfig ?? new AppConfig();
            }
            catch (Exception)
            {
                Config = new AppConfig();
            }
        }

        public void Save()
        {
            var root = new RootConfig { AppConfig = Config };
            var json = JsonConvert.SerializeObject(root, Formatting.Indented);

            var directory = Path.GetDirectoryName(_configPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_configPath, json);
        }

        public void Reload()
        {
            LoadConfig();
        }

        private class RootConfig
        {
            public AppConfig AppConfig { get; set; }
        }
    }
}
