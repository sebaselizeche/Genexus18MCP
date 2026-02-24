using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace GxMcp.Gateway
{
    public class Configuration
    {
        [JsonProperty("GeneXus")]
        public GeneXusConfig? GeneXus { get; set; }

        [JsonProperty("Server")]
        public ServerConfig? Server { get; set; }

        [JsonProperty("Logging")]
        public LoggingConfig? Logging { get; set; }

        [JsonProperty("Environment")]
        public EnvironmentConfig? Environment { get; set; }

        public static Configuration Load()
        {
            // Reliable path discovery: look for config.json starting from .exe up to root
            string? currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string? configPath = null;

            while (currentDir != null)
            {
                string check = Path.Combine(currentDir, "config.json");
                if (File.Exists(check)) { configPath = check; break; }
                currentDir = Path.GetDirectoryName(currentDir);
            }

            if (configPath == null)
            {
                if (File.Exists("config.json")) configPath = Path.GetFullPath("config.json");
                else throw new FileNotFoundException("Could not find config.json in any parent directory.");
            }

            Console.Error.WriteLine($"[Gateway] Loading config from: {configPath}");
            string json = File.ReadAllText(configPath);
            var config = JsonConvert.DeserializeObject<Configuration>(json);
            
            if (config == null) throw new Exception("Failed to parse config.json");
            
            // Critical Validation
            if (string.IsNullOrEmpty(config.Environment?.KBPath))
            {
                Console.Error.WriteLine("[Gateway] WARNING: Environment.KBPath is missing in config.json!");
            }
            else 
            {
                Console.Error.WriteLine($"[Gateway] KB Path configured: {config.Environment.KBPath}");
            }

            return config;
        }
    }

    public class GeneXusConfig
    {
        public string? InstallationPath { get; set; }
        public string? WorkerExecutable { get; set; }
    }

    public class ServerConfig
    {
        public int HttpPort { get; set; } = 5000;
        public bool McpStdio { get; set; } = true;
    }

    public class LoggingConfig
    {
        public string? Level { get; set; }
        public string? Path { get; set; }
    }

    public class EnvironmentConfig
    {
        public string? KBPath { get; set; }
        public string? GX_SHADOW_PATH { get; set; }
    }
}
