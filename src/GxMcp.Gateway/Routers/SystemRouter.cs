using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace GxMcp.Gateway.Routers
{
    public class SystemRouter : IMcpModuleRouter
    {
        public string ModuleName => "System";

        public object[] GetToolDefinitions()
        {
            return new object[]
            {
                new {
                    name = "genexus_lifecycle",
                    description = "KB state management: build, validate, sync, and indexing.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            action = new { type = "string", @enum = new[] { "build", "rebuild", "validate", "sync", "index", "status" }, description = "Lifecycle action." },
                            target = new { type = "string", description = "Object or environment name." },
                            code = new { type = "string", description = "For validation: the code to check." }
                        },
                        required = new[] { "action" }
                    }
                },
                new {
                    name = "genexus_forge",
                    description = "Generation of new code and structures from templates or translations.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            action = new { type = "string", @enum = new[] { "scaffold", "translate", "sample" }, description = "Generation action." },
                            type = new { type = "string", description = "Object type (e.g. Prc, Trn)." },
                            name = new { type = "string", description = "Object name." },
                            content = new { type = "string", description = "Initial code or target language." }
                        },
                        required = new[] { "action" }
                    }
                },
                new {
                    name = "genexus_test",
                    description = "Execution of native GeneXus tests (GXtest).",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Test object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_doc",
                    description = "Knowledge extraction and documentation generation.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            action = new { type = "string", @enum = new[] { "wiki", "visualize", "health" }, description = "Documentation action." },
                            target = new { type = "string", description = "Object or domain name." }
                        },
                        required = new[] { "action" }
                    }
                }
            };
        }

        public object ConvertToolCall(string toolName, JObject args)
        {
            string action = args?["action"]?.ToString();
            string target = args?["target"]?.ToString();

            switch (toolName)
            {
                case "genexus_lifecycle":
                    switch (action)
                    {
                        case "build": return new { module = "Build", action = "Build", target = target };
                        case "rebuild": return new { module = "Build", action = "RebuildAll", target = target };
                        case "validate": return new { module = "Validation", action = "Check", target = target, payload = args?["code"]?.ToString() };
                        case "sync": return new { module = "Build", action = "Sync", target = target };
                        case "index": return new { module = "KB", action = "BulkIndex" };
                        case "status": return new { module = "KB", action = "GetIndexStatus" };
                        default: return null;
                    }

                case "genexus_forge":
                    switch (action)
                    {
                        case "scaffold": return new { module = "Forge", action = "Scaffold", target = args?["type"]?.ToString(), payload = args?["name"]?.ToString(), code = args?["content"]?.ToString() };
                        case "translate": return new { module = "Analyze", action = "TranslateTo", target = args?["name"]?.ToString(), language = args?["content"]?.ToString() };
                        case "sample": return new { module = "Pattern", action = "GetSample", target = args?["type"]?.ToString() };
                        default: return null;
                    }

                case "genexus_doc":
                    switch (action)
                    {
                        case "wiki": return new { module = "Wiki", action = "Generate", target = target };
                        case "visualize": return new { module = "Visualizer", action = "Generate", target = target };
                        case "health": return new { module = "Health", action = "GetReport" };
                        default: return null;
                    }

                case "genexus_test":
                    return new { module = "Test", action = "Run", target = args?["name"]?.ToString() };
                
                // Legados
                case "genexus_validate":
                    return new { module = "Validation", action = "Check", target = target, payload = args?["code"]?.ToString() };
                case "genexus_build":
                    return new { module = "Build", action = args?["action"]?.ToString(), target = target };
                case "genexus_history":
                    return new { module = "History", action = args?["action"]?.ToString(), target = args?["name"]?.ToString() };

                default:
                    return null;
            }
        }
    }
}
