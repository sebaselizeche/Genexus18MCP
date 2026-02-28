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
                    description = "Static analysis: complexity, anti-patterns, and business logic insights.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_ui_context",
                    description = "Returns controls and layout structure for Web Panels/Transactions.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_data_context",
                    description = "Returns base table, extended table, and relationships for an object.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_hierarchy",
                    description = "Returns call tree (callers/callees).",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string" } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_linter",
                    description = "Checks source code for anti-patterns (commits in loops, etc).",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name to lint." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_translate_to",
                    description = "Translates a GeneXus object structure/logic to another language (C#, TS, etc).",
                    inputSchema = new {
                        type = "object",
                        properties = new { 
                            name = new { type = "string", description = "Object name." },
                            language = new { type = "string", description = "Target language (cs, ts)." }
                        },
                        required = new[] { "name", "language" }
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
                case "genexus_translate_to":
                    return new { module = "Analyze", action = "TranslateTo", target = args?["name"]?.ToString(), language = args?["language"]?.ToString() };
                default:
                    return null;
            }
        }
    }
}
