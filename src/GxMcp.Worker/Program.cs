using System;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GxMcp.Worker.Services;
using GxMcp.Worker.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace GxMcp.Worker
{
    class Program
    {
        private static readonly BlockingCollection<string> CommandQueue = new BlockingCollection<string>();
        private static CommandDispatcher _dispatcher;

        [STAThread]
        static void Main(string[] args)
        {
            try {
                AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                    Logger.Error("FATAL: " + (e.ExceptionObject as Exception)?.Message);
                };

                Console.WriteLine("WORKER_HANDSHAKE_START");
                Logger.Info("Worker process started (STA Mode).");

                string gxPath = Environment.GetEnvironmentVariable("GX_PROGRAM_DIR") ?? @"C:\Program Files (x86)\GeneXus\GeneXus18";
                
                AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) => {
                    try {
                        string assemblyName = new AssemblyName(resolveArgs.Name).Name + ".dll";
                        string assemblyPath = Path.Combine(gxPath, assemblyName);
                        if (File.Exists(assemblyPath)) return Assembly.LoadFrom(assemblyPath);
                    } catch { }
                    return null;
                };

                InitializeSdk(gxPath);
                _dispatcher = CommandDispatcher.Instance;
                Logger.Info("Worker SDK ready.");

                var readerThread = new Thread(() => {
                    using (var reader = new StreamReader(Console.OpenStandardInput())) {
                        while (true) {
                            string line = reader.ReadLine();
                            if (line == null) break;
                            if (!string.IsNullOrWhiteSpace(line)) CommandQueue.Add(line);
                        }
                    }
                    CommandQueue.CompleteAdding();
                }) { IsBackground = true };
                readerThread.Start();

                foreach (string line in CommandQueue.GetConsumingEnumerable())
                {
                    ProcessCommand(line);
                }
            } catch (Exception ex) {
                Logger.Error($"Main FATAL: {ex.Message}");
            }
        }

        private static void InitializeSdk(string gxPath)
        {
            try {
                Directory.SetCurrentDirectory(gxPath);
                
                var connAsm = Assembly.LoadFrom(Path.Combine(gxPath, "Connector.dll"));
                var connType = connAsm.GetType("Artech.Core.Connector");
                connType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                connType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);

                var uiAsm = Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Architecture.UI.Framework.dll"));
                var uiType = uiAsm.GetType("Artech.Architecture.UI.Framework.Services.UIServices");
                uiType.GetMethod("SetDisableUI", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { true });
                uiType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);

                var commonAsm = Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Genexus.Common.dll"));
                var initType = commonAsm.GetType("Artech.Genexus.Common.KBModelObjectsInitializer");
                initType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);

                var kbType = Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Architecture.Common.dll")).GetType("Artech.Architecture.Common.Objects.KnowledgeBase");
                var kbFactoryProp = kbType.GetProperty("KBFactory", BindingFlags.Public | BindingFlags.Static);
                kbFactoryProp.SetValue(null, Activator.CreateInstance(connAsm.GetType("Connector.KBFactory")));
                
                Logger.Info("Surgical Init Success.");
            } catch (Exception ex) { Logger.Error("Init Error: " + ex.Message); }
        }

        private static void ProcessCommand(string line)
        {
            try {
                var obj = JObject.Parse(line);
                string idJson = obj["id"]?.ToString() ?? "null";

                string result = _dispatcher.Dispatch(line);
                SendResponse(result, idJson);
            } catch (Exception ex) { Logger.Error("ProcessCommand Error: " + ex.Message); }
        }

        private static void SendResponse(string result, string id)
        {
            try {
                // CRITICAL: Ensure result is treated as a raw JToken if it's already JSON
                // to avoid double escaping, then serialize to ONE LINE.
                object resultObj;
                try { resultObj = JToken.Parse(result); }
                catch { resultObj = result; }

                var response = new {
                    jsonrpc = "2.0",
                    result = resultObj,
                    id = id
                };

                string json = JsonConvert.SerializeObject(response, Formatting.None);
                
                lock (Console.Out) { 
                    Console.WriteLine(json); 
                    Console.Out.Flush(); 
                }
            } catch (Exception ex) { Logger.Error("SendResponse Error: " + ex.Message); }
        }
    }
}
