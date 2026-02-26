using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace GxMcp.Gateway.Routers
{
    public class AnalyzeRouter : IMcpModuleRouter
    {
        public string ModuleName => "Analyze";

        public object[] GetToolDefinitions()
        {
            return new object[]
            {
                new {
                    name = "genexus_analyze",
                    description = "Deep static analysis: identifies complexity, COMMIT in loops, unfiltered loops, and business logic insights.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_ui_context",
                    description = "UI Vision: returns control list and layout structure for Web Panels and Transactions.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_data_context",
                    description = "Holistic Data Vision: returns base table, extended table, and parent/child relationships for an object.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_hierarchy",
                    description = "Retrieves the call tree (who calls this, who is called by this).",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string" } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_linter",
                    description = "Analyzes object source code for anti-patterns (commits in loops, unfiltered loops, etc).",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name to lint." } },
                        required = new[] { "name" }
                    }
                }
            };
        }

        public object ConvertToolCall(string toolName, JObject args)
        {
            switch (toolName)
            {
                case "genexus_analyze":
                    return new { module = "Analyze", action = "Analyze", target = args?["name"]?.ToString() };
                case "genexus_get_data_context":
                    return new { module = "Analyze", action = "GetDataContext", target = args?["name"]?.ToString() };
                case "genexus_get_ui_context":
                    return new { module = "UI", action = "GetUIContext", target = args?["name"]?.ToString() };
                case "genexus_get_hierarchy":
                    return new { module = "Analyze", action = "GetHierarchy", target = args?["name"]?.ToString() };
                case "genexus_linter":
                    return new { module = "Linter", action = "Analyze", target = args?["name"]?.ToString() };
                default:
                    return null;
            }
        }
    }
}
