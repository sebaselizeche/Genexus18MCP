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
                        capabilities = new { 
                            tools = new { listChanged = true },
                            resources = new { listChanged = true, subscribe = false }
                        },
                        serverInfo = new { name = "genexus-mcp-server", version = "3.5.0" }
                    };
                case "tools/list":
                    return new { tools = _routers.SelectMany(r => r.GetToolDefinitions()).ToArray() };
                case "resources/list":
                    return new { 
                        resources = new[] {
                            new { uri = "genexus://objects", name = "GeneXus Objects Index", description = "Browsable index of all objects in the KB." },
                            new { uri = "genexus://attributes", name = "GeneXus Attributes", description = "Browsable list of all attributes." }
                        }
                    };
                case "resources/read":
                    return null; // Handled in ConvertResourceCall or directly in Program
                case "notifications/initialized": return null;
                case "ping": return new { };
                default: return null;
            }
        }

        public static object ConvertResourceCall(JObject request)
        {
            string uri = request["params"]?["uri"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(uri)) return null;

            if (uri.StartsWith("genexus://objects/"))
            {
                var parts = uri.Replace("genexus://objects/", "").Split('/');
                if (parts.Length >= 2)
                    return new { module = "Read", action = "ExtractSource", target = $"{parts[0]}:{parts[1]}", part = "Source" };
            }
            if (uri.StartsWith("genexus://attributes/"))
            {
                var name = uri.Replace("genexus://attributes/", "");
                return new { module = "Read", action = "GetAttribute", target = name };
            }
            if (uri == "genexus://objects") return new { module = "ListObjects", action = "Query", target = "", limit = 200 };
            if (uri == "genexus://attributes") return new { module = "Search", action = "Query", target = "type:Attribute", limit = 200 };

            return null;
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

            // Fallback O(n) in case dynamic reflection failed during initialization
            foreach (var r in _routers)
            {
                var converted = r.ConvertToolCall(toolName, args);
                if (converted != null) return converted;
            }

            return null;
        }
    }
}
