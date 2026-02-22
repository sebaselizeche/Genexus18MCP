using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

namespace GxMcp.Gateway
{
    class Program
    {
        private static WorkerProcess? _worker;
        private static ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<string>>();
        private static readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gateway_debug.log");

        public static void Log(string msg)
        {
            try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }
            catch { }
        }

        static async Task Main(string[] args)
        {
            Console.Error.WriteLine("=== Gateway starting (Stdio Mode) ===");
            Log("=== Gateway starting (Stdio Mode) ===");

            var config = Configuration.Load();
            _worker = new WorkerProcess(config);
            _worker.OnRpcResponse += HandleWorkerResponse;
            _worker.Start();

            // HTTP Server in background
            if (config.Server?.HttpPort > 0)
            {
                _ = Task.Run(async () => {
                    try { await StartHttpServer(config); }
                    catch { }
                });
            }

            // MCP Stdio Loop
            using (var reader = new StreamReader(Console.OpenStandardInput()))
            {
                while (true)
                {
                    string? line = await reader.ReadLineAsync();
                    if (line == null) break;
                    
                    try {
                        var request = JObject.Parse(line);
                        var response = await ProcessMcpRequest(request);
                        if (response != null)
                        {
                            Console.WriteLine(response.ToString(Formatting.None));
                            Console.Out.Flush();
                        }
                    } catch (Exception ex) { Log("MCP Error: " + ex.Message); }
                }
            }
        }

        private static void HandleWorkerResponse(string json)
        {
            try {
                var val = JObject.Parse(json);
                string id = val["id"]?.ToString();
                if (!string.IsNullOrEmpty(id) && _pendingRequests.TryRemove(id, out var tcs))
                    tcs.SetResult(json);
            } catch { }
        }

        private static async Task<JObject?> ProcessMcpRequest(JObject request)
        {
            string method = request["method"]?.ToString();
            var idToken = request["id"];

            // Protocol level
            var mcpResponse = McpRouter.Handle(request);
            if (mcpResponse != null)
            {
                return new JObject { ["jsonrpc"] = "2.0", ["id"] = idToken?.DeepClone(), ["result"] = JToken.FromObject(mcpResponse) };
            }

            // Tool Calls
            if (method == "tools/call")
            {
                var workerCmd = McpRouter.ConvertToolCall(request);
                if (workerCmd != null)
                {
                    string idStr = Guid.NewGuid().ToString();
                    var tcs = new TaskCompletionSource<string>();
                    _pendingRequests[idStr] = tcs;

                    var rpcWrapper = new { jsonrpc = "2.0", id = idStr, method = "execute_command", @params = workerCmd };
                    await _worker!.SendCommandAsync(JsonConvert.SerializeObject(rpcWrapper));

                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(60000));
                    if (completedTask == tcs.Task)
                    {
                        string resultJson = await tcs.Task;
                        var resultObj = JObject.Parse(resultJson);
                        var finalResult = resultObj["result"] ?? resultObj["error"];
                        return new JObject { 
                            ["jsonrpc"] = "2.0", 
                            ["id"] = idToken?.DeepClone(), 
                            ["result"] = JToken.FromObject(new { content = new[] { new { type = "text", text = finalResult.ToString() } }, isError = resultObj["error"] != null }) 
                        };
                    }
                }
            }
            return null;
        }

        static Task StartHttpServer(Configuration config)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://*:{config.Server.HttpPort}");
            builder.Logging.ClearProviders();
            builder.Services.AddCors(options => options.AddPolicy("AllowAll", b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
            var app = builder.Build();
            app.UseCors("AllowAll");

            app.MapPost("/api/command", async (HttpRequest request) => {
                using (var reader = new StreamReader(request.Body)) {
                    string body = await reader.ReadToEndAsync();
                    var requestObj = JsonConvert.DeserializeObject<JObject>(body);
                    string requestId = Guid.NewGuid().ToString();
                    var tcs = new TaskCompletionSource<string>();
                    _pendingRequests[requestId] = tcs;

                    var rpcWrapper = new { jsonrpc = "2.0", id = requestId, method = "execute_command", @params = requestObj?["params"] ?? requestObj };
                    await _worker!.SendCommandAsync(JsonConvert.SerializeObject(rpcWrapper));

                    if (await Task.WhenAny(tcs.Task, Task.Delay(30000)) == tcs.Task) {
                        var res = JObject.Parse(await tcs.Task);
                        return Results.Content(res["result"]?.ToString() ?? await tcs.Task, "application/json");
                    }
                    return Results.BadRequest("Timeout");
                }
            });

            return app.RunAsync();
        }
    }
}
