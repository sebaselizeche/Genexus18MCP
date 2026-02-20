using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GxMcp.Gateway
{
    public class WorkerProcess
    {
        private Process? _process;
        private readonly Configuration _config;

        public event Action<string>? OnRpcResponse;

        public WorkerProcess(Configuration config)
        {
            _config = config;
        }

        public void Start()
        {
            string workerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _config.GeneXus.WorkerExecutable);
            
            // If not found in local bin, check if we are in dev mode and it is in sibling project
            if (!File.Exists(workerPath))
            {
            // Development fallback: src/GxMcp.Worker/bin/Debug/GxMcp.Worker.exe
            // Validated via terminal: 5 levels up to project root, then down to worker
            string devPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\src\GxMcp.Worker\bin\Debug\GxMcp.Worker.exe"));
             if (File.Exists(devPath)) workerPath = devPath;
        }

        if (!File.Exists(workerPath))
        {
            // Try one more common location: just ../../../src/... if running from bin/Debug/net8.0
             string altPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\src\GxMcp.Worker\bin\Debug\GxMcp.Worker.exe"));
             if (File.Exists(altPath)) workerPath = altPath;
        }

        if (!File.Exists(workerPath))
            throw new FileNotFoundException($"Worker Executable not found. Searched at: {workerPath}. BaseDir: {AppDomain.CurrentDomain.BaseDirectory}");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // Provide the GeneXus Environment variable
                EnvironmentVariables = {
                    ["GX_PROGRAM_DIR"] = _config.GeneXus.InstallationPath
                }
            };

            _process = new Process { StartInfo = startInfo };
            
            _process.OutputDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    // Detect if line is JSON-RPC response
                    if (e.Data.TrimStart().StartsWith("{") && e.Data.Contains("\"jsonrpc\""))
                    {
                        OnRpcResponse?.Invoke(e.Data);
                    }
                    else 
                        Program.Log($"[Worker StdOut] {e.Data}");
                }
            };
            
            _process.ErrorDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                   Program.Log($"[Worker StdErr] {e.Data}");
                }
            };

            _process.Start();
            _process.StandardInput.AutoFlush = true;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public async Task SendCommandAsync(string jsonRpc)
        {
            // Ensure process is running
            if (_process == null || _process.HasExited)
            {
                Console.Error.WriteLine("[Gateway] Worker not running. Starting...");
                Start();
            }
            
            try 
            {
                await _process.StandardInput.WriteLineAsync(jsonRpc);
                await _process.StandardInput.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
            {
                Console.Error.WriteLine($"[Gateway] Worker communication failed: {ex.Message}. Restarting and retrying...");
                
                // Circuit Breaker: Restart
                Stop();
                Start();
                
                // Retry once
                try 
                {
                     await _process.StandardInput.WriteLineAsync(jsonRpc);
                     await _process.StandardInput.FlushAsync();
                     Console.Error.WriteLine("[Gateway] Retry successful.");
                }
                catch (Exception retryEx)
                {
                    Console.Error.WriteLine($"[Gateway] Retry failed: {retryEx.Message}");
                    throw; // Give up
                }
            }
        }

        public void Stop()
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.Dispose();
            }
        }
    }
}
