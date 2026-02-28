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
                    name = "genexus_validate",
                    description = "Surgical syntax check using native SDK engine. Call before saving to ensure logic is sound. Returns SDK diagnostics.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            name = new { type = "string", description = "Object name." },
                            part = new { type = "string", description = "Part name.", @default = "Source" },
                            code = new { type = "string", description = "Code to validate." }
                        },
                        required = new[] { "name", "code" }
                    }
                },
                new {
                    name = "genexus_test",
                    description = "Executes GXtest Unit Tests via native runner. Returns real-time feedback and assertion results.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Unit Test object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_scaffold",
                    description = "Creates a new object from template (CRUD Procedure or Transaction). Streamlines common patterns.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            type = new { type = "string", description = "Prc or Trn." },
                            name = new { type = "string", description = "New object name." },
                            description = new { type = "string", description = "Object description." },
                            code = new { type = "string", description = "Initial code." }
                        },
                        required = new[] { "type", "name" }
                    }
                },
                new {
                    name = "genexus_build",
                    description = "Executes Build operations (Build, Sync, Reorg). High-level compile task.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            action = new { type = "string", description = "Build, RebuildAll, Sync, Reorg." },
                            target = new { type = "string", description = "Environment or Object name." }
                        },
                        required = new[] { "action" }
                    }
                },
                new {
                    name = "genexus_visualize",
                    description = "Generates an interactive HTML dependency graph for a domain or the whole KB.",
                    inputSchema = new {
                        type = "object",
                        properties = new { domain = new { type = "string", description = "Optional domain filter." } }
                    }
                },
                new {
                    name = "genexus_health_report",
                    description = "Holistic KB health monitoring: dead code, circular dependencies, complexity hotspots.",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new {
                    name = "genexus_get_pattern_sample",
                    description = "Returns a representative object of a given type to serve as a coding style reference.",
                    inputSchema = new {
                        type = "object",
                        properties = new { type = new { type = "string", description = "Type of object (e.g. Transaction, Procedure)." } },
                        required = new[] { "type" }
                    }
                },
                new {
                    name = "genexus_doctor",
                    description = "Analyzes build logs to diagnose errors and suggest fixes. Use this when builds fail.",
                    inputSchema = new {
                        type = "object",
                        properties = new { logPath = new { type = "string", description = "Optional path to MSBuild log file." } }
                    }
                },
                new {
                    name = "genexus_wiki",
                    description = "Generates technical documentation in Wiki format for the object.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string" } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_history",
                    description = "Lists object revision history or restores a previous version.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            name = new { type = "string" },
                            action = new { type = "string", description = "List, Restore" }
                        },
                        required = new[] { "name", "action" }
                    }
                },
                new {
                    name = "genexus_bulk_index",
                    description = "Full KB crawl to rebuild the search index. Mandatory after large changes. Updates 'parm' rules and snippets.",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new {
                    name = "genexus_self_test",
                    description = "Runs internal unit/integration tests to verify MCP reliability and feature availability.",
                    inputSchema = new { type = "object", properties = new { } }
                }
            };
        }

        public object ConvertToolCall(string toolName, JObject args)
        {
            switch (toolName)
            {
                case "genexus_validate":
                    return new { module = "Validation", action = "Check", target = args?["name"]?.ToString(), part = args?["part"]?.ToString() ?? "Source", payload = args?["code"]?.ToString() };
                case "genexus_test":
                    return new { module = "Test", action = "Run", target = args?["name"]?.ToString() };
                case "genexus_scaffold":
                    return new { module = "Forge", action = "Scaffold", target = args?["type"]?.ToString(), payload = args?["name"]?.ToString(), code = args?["code"]?.ToString(), description = args?["description"]?.ToString() };
                case "genexus_build":
                    return new { module = "Build", action = args?["action"]?.ToString(), target = args?["target"]?.ToString() };
                case "genexus_visualize":
                    return new { module = "Visualizer", action = "Generate", target = args?["domain"]?.ToString() };
                case "genexus_health_report":
                    return new { module = "Health", action = "GetReport" };
                case "genexus_get_pattern_sample":
                    return new { module = "Pattern", action = "GetSample", target = args?["type"]?.ToString() };
                case "genexus_doctor":
                    return new { module = "Doctor", action = "Diagnose", target = args?["logPath"]?.ToString() };
                case "genexus_wiki":
                    return new { module = "Wiki", action = "Generate", target = args?["name"]?.ToString() };
                case "genexus_history":
                    return new { module = "History", action = args?["action"]?.ToString(), target = args?["name"]?.ToString() };
                case "genexus_bulk_index":
                    return new { module = "KB", action = "BulkIndex" };
                case "genexus_self_test":
                    return new { module = "KB", action = "SelfTest" };
                default:
                    return null;
            }
        }
    }
}
