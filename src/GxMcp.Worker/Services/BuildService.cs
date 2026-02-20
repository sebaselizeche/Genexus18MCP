using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class BuildService
    {
        private string _msbuildPath;
        private string _gxDir;
        private KbService _kbService;

        public BuildService()
        {
            _gxDir = @"C:\Program Files (x86)\GeneXus\GeneXus18";
            
            string[] searchPaths = new[] {
                Path.Combine(_gxDir, "MSBuild.exe"),
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
                @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
            };

            foreach (var p in searchPaths)
            {
                if (File.Exists(p))
                {
                    _msbuildPath = p;
                    break;
                }
            }

            if (string.IsNullOrEmpty(_msbuildPath))
            {
                _msbuildPath = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe";
            }
        }

        public void SetKbService(KbService kbService)
        {
            _kbService = kbService;
        }

        public string Build(string action, string target)
        {
            return Execute(action, target);
        }

        public string Doctor(string logPath)
        {
            return "{\"status\": \"Doctor not implemented\"}";
        }

        public string Execute(string action, string target)
        {
            // Simplified for now but needs implementation if build is needed
            return "{\"status\": \"Build action '" + action + "' accepted but execution via MSBuild is currently being restored\"}";
        }

        public string GetKBPath()
        {
            try 
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new[] {
                    Path.Combine(baseDir, "config.json"),
                    Path.GetFullPath(Path.Combine(baseDir, @"..\config.json")),
                    Path.GetFullPath(Path.Combine(baseDir, @"..\..\config.json")),
                    Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\config.json"))
                };

                string configPath = null;
                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        configPath = c;
                        break;
                    }
                }

                if (configPath != null)
                {
                    var json = File.ReadAllText(configPath);
                    var cfg = Newtonsoft.Json.Linq.JObject.Parse(json);
                    string path = cfg["Environment"]?["KBPath"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
            }
            catch { }
            
            return ""; 
        }
    }
}
