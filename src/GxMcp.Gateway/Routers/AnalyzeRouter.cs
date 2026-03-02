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
                },
                new {
                    name = "genexus_get_signature",
                    description = "Returns the parm() rule and signature for an object.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_conversion_context",
                    description = "Consolidates all metadata (Source, Rules, Conditions, Variables) into one call for AI analysis/conversion.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_structure",
                    description = "Extracts logical structure (Subs and Events) from object source code. Helps orient before reading full logic.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_navigation",
                    description = "Returns the navigation report (plan) for an object, including Base Table, Order, and Index used.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_pattern_metadata",
                    description = "Extracts Pattern-specific properties (templates, tab structures, grids) for WWP objects.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name (Transaction or WWP Instance)." } },
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
                case "genexus_translate_to":
                    return new { module = "Analyze", action = "TranslateTo", target = args?["name"]?.ToString(), language = args?["language"]?.ToString() };
                case "genexus_get_signature":
                    return new { module = "Analyze", action = "GetParameters", target = args?["name"]?.ToString() };
                case "genexus_get_conversion_context":
                    return new { module = "Analyze", action = "GetConversionContext", target = args?["name"]?.ToString() };
                case "genexus_get_structure":
                    return new { module = "Structure", action = "GetLogicStructure", target = args?["name"]?.ToString() };
                case "genexus_get_navigation":
                    return new { module = "Analyze", action = "GetNavigation", target = args?["name"]?.ToString() };
                case "genexus_get_pattern_metadata":
                    return new { module = "Analyze", action = "GetPatternMetadata", target = args?["name"]?.ToString() };
                default:
                    return null;
            }
        }
    }
}
