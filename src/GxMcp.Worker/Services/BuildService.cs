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

        public BuildService()
        {
            // Auto-detect GX Path
            // For now hardcoded or derived
            _gxDir = @"C:\Program Files (x86)\GeneXus\GeneXus18";
            
            // Try to locate MSBuild
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
                Console.Error.WriteLine("[BuildService] WARNING: MSBuild.exe not found in standard locations. Defaulting to .NET Framework path, hoping it exists.");
                _msbuildPath = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe";
            }
            
            Console.Error.WriteLine($"[BuildService] Using MSBuild: {_msbuildPath}");
        }

        public string Execute(string action, string target)
        {
            // Porting logic from gx_ops.ps1
            string targetsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "standard_build.targets");
            // We need to ensure standard_build.targets exists or create it. 
            // In the legacy system it was created dynamically? 
            // gx_ops.ps1 says: $targets = Join-Path $cfg.Environment.BaseDirectory "standard_build.targets"
            
            // Let's create a temp targets file for safety
            string tempTargets = Path.GetTempFileName() + ".targets";
            
            try 
            {
                string args = "";
                
                if (action == "UpdateFromServer" || action == "Sync")
                {
                    // /t:UpdateFromServer /p:ServerUserName=...
                    // Ignoring auth for now, assuming Windows Auth works or handled by tasks
                    File.WriteAllText(tempTargets, GenerateTargetsContent("UpdateFromServer"));
                    args = $"/t:UpdateFromServer /nologo /v:m \"{tempTargets}\"";
                }
                else if (action == "Reorganize" || action == "Reorg")
                {
                    File.WriteAllText(tempTargets, GenerateTargetsContent("Reorganize"));
                    args = $"/t:Reorganize /nologo /v:m \"{tempTargets}\"";
                }
                else if (action == "Build")
                {
                    File.WriteAllText(tempTargets, GenerateTargetsContent("Build"));
                    args = $"/t:Build /nologo /v:m \"{tempTargets}\"";
                }
                else 
                {
                    return "{\"error\": \"Unknown Build Action\"}";
                }

                Console.Error.WriteLine($"[BuildService] Running MSBuild: {args}");
                
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _msbuildPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Stopwatch sw = Stopwatch.StartNew();
                using (var p = Process.Start(psi))
                {
                    string output = "";
                    p.OutputDataReceived += (s, e) => { 
                        if (e.Data != null) { 
                            Logger.Info($"[MSBuild] {e.Data}"); 
                            output += e.Data + "\n"; 
                        } 
                    };
                    p.ErrorDataReceived += (s, e) => { 
                        if (e.Data != null) {
                            Logger.Error($"[MSBuild Error] {e.Data}");
                        }
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    sw.Stop();

                    return ParseBuildOutput(output, p.ExitCode == 0, sw.Elapsed);
                }
            }
            finally
            {
                if (File.Exists(tempTargets)) File.Delete(tempTargets);
            }
        }

        private string ParseBuildOutput(string log, bool success, TimeSpan duration)
        {
            var errors = new System.Collections.Generic.List<string>();
            var lines = log.Split('\n');
            int warningCount = 0;

            foreach (var line in lines)
            {
                if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    errors.Add(CommandDispatcher.EscapeJsonString(line.Trim()));
                }
                if (line.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    warningCount++;
                }
            }

            string errorsJson = "[" + string.Join(",", errors.Select(e => "\"" + e + "\"")) + "]";
            
            return "{\"status\": \"" + (success ? "Success" : "Failure") + "\"," +
                   "\"duration\": \"" + duration.ToString(@"hh\:mm\:ss") + "\"," +
                   "\"errorCount\": " + errors.Count + "," +
                   "\"warningCount\": " + warningCount + "," +
                   "\"errors\": " + errorsJson + "}";
        }

        public string GetKBPath()
        {
            // Simple config reader
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            // If not found, try traversing up (Dev mode)
            if (!File.Exists(configPath)) configPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\config.json"));

            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                int idx = json.IndexOf("\"KBPath\"");
                if (idx > -1)
                {
                    // "KBPath" length is 8 (including quotes).
                    int keyEnd = idx + 8; 
                    // Find opening quote of value (skipping colon and whitespace)
                    int valueStartQuote = json.IndexOf("\"", keyEnd);
                    if (valueStartQuote > -1)
                    {
                        int valueStart = valueStartQuote + 1;
                        int valueEndQuote = json.IndexOf("\"", valueStart);
                        if (valueEndQuote > -1)
                        {
                            string path = json.Substring(valueStart, valueEndQuote - valueStart).Replace("\\\\", "\\");
                            
                            // Validate configured path
                            if (!Directory.Exists(path))
                                throw new DirectoryNotFoundException($"Configured GeneXus KB Path not found: {path}");
                                
                            return path;
                        }
                    }
                }
            }
            
            string defaultPath = @"C:\Models\MyKnowledgeBase";
            if (!Directory.Exists(defaultPath))
                throw new DirectoryNotFoundException($"GeneXus KB Path not found. Please configure 'KBPath' in config.json. Tried default: {defaultPath}");
                
            return defaultPath;
        }

        public string GenerateExportTarget(string exportFile, string objects)
        {
             return $@"<Project DefaultTargets='Export' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                        <Import Project='{_gxDir}\Genexus.Tasks.targets' />
                        <Target Name='Export'>
                          <OpenKnowledgeBase Directory='{GetKBPath()}' />
                          <Export File='{exportFile}' Objects='{objects}' />
                        </Target>
                      </Project>";
        }

        public string RunMSBuild(string targetsFile, string targetName)
        {
            string args = $"/t:{targetName} /nologo /v:m \"{targetsFile}\"";
            Console.Error.WriteLine($"[BuildService] Running MSBuild: {args}");
                
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _msbuildPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var p = Process.Start(psi))
            {
                string output = "";
                p.OutputDataReceived += (s, e) => { 
                    if(e.Data != null) { 
                        Logger.Info($"[MSBuild] {e.Data}"); 
                        output += e.Data + "\n"; 
                    } 
                };
                p.ErrorDataReceived += (s, e) => { 
                    if(e.Data != null) {
                        Logger.Error($"[MSBuild Error] {e.Data}");
                    }
                };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    throw new Exception($"MSBuild failed with exit code {p.ExitCode}. Output: {output}");
                }
                return output;
            }
        }

        private string GenerateTargetsContent(string targetName)
        {
            // Minimal targets file to import Genexus.Tasks
            return $@"<Project DefaultTargets='{targetName}' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                        <Import Project='{_gxDir}\Genexus.Tasks.targets' />
                        <Target Name='{targetName}'>
                            <OpenKnowledgeBase Directory='{GetKBPath()}' />
                            <{targetName} />
                        </Target>
                      </Project>";
        }
    }
}
