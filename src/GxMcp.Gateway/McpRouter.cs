using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using GxMcp.Gateway.Routers;

namespace GxMcp.Gateway
{
    public class McpRouter
    {
        private static readonly Dictionary<string, IMcpModuleRouter> _toolMap;
        private static readonly List<IMcpModuleRouter> _routers;

        static McpRouter()
        {
            _routers = new List<IMcpModuleRouter>
            {
                new SearchRouter(),
                new ObjectRouter(),
                new AnalyzeRouter(),
                new SystemRouter()
            };

            // Mapeia cada ferramenta para seu roteador para busca O(1)
            _toolMap = new Dictionary<string, IMcpModuleRouter>();
            foreach (var router in _routers)
            {
                foreach (dynamic tool in router.GetToolDefinitions())
                {
                    string name = tool.name;
                    if (!_toolMap.ContainsKey(name)) _toolMap[name] = router;
                }
            }
        }

        public static object Handle(JObject request)
        {
            string method = request["method"]?.ToString();
            switch (method)
            {
                case "initialize":
                    return new {
                        protocolVersion = "2025-03-26",
                        capabilities = new { tools = new { listChanged = true } },
                        serverInfo = new { name = "genexus-mcp-server", version = "3.5.0" }
                    };
                case "tools/list":
                    return new { tools = _routers.SelectMany(r => r.GetToolDefinitions()).ToArray() };
                case "notifications/initialized": return null;
                case "ping": return new { };
                default: return null;
            }
        }

        public static object ConvertToolCall(JObject request)
        {
            string method = request["method"]?.ToString();
            if (method != "tools/call") return null;
            
            var paramsObj = request["params"] as JObject;
            string toolName = paramsObj?["name"]?.ToString();
            var args = paramsObj?["arguments"] as JObject;

            if (string.IsNullOrEmpty(toolName)) return null;

            // Busca O(1) em vez de loop sequencial
            if (_toolMap.TryGetValue(toolName, out var router))
            {
                return router.ConvertToolCall(toolName, args);
            }

            return null;
        }
    }
}
