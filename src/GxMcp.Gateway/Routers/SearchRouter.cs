using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace GxMcp.Gateway.Routers
{
    public class SearchRouter : IMcpModuleRouter
    {
        public string ModuleName => "Search";

        public object[] GetToolDefinitions()
        {
            return new object[]
            {
                new {
                    name = "genexus_list_objects",
                    description = "Fast KB discovery. Unified search by Name, Type (Procedure, Transaction, WebPanel), or Description. Returns enriched results with 'parm' rules and code snippets.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            filter = new { type = "string", description = "Search term (e.g. 'Prc:MyProc', 'Customer', 'Transaction')." },
                            limit = new { type = "integer", description = "Maximum objects to return.", @default = 50 }
                        }
                    }
                },
                new {
                    name = "genexus_search",
                    description = "Advanced semantic search and impact analysis. Use 'usedby:TableName' to find all references. Returns connection counts, 'parm' signatures, and source snippets.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            query = new { type = "string", description = "Search query or 'usedby:TargetName'." }
                        },
                        required = new[] { "query" }
                    }
                }
            };
        }

        public object ConvertToolCall(string toolName, JObject args)
        {
            switch (toolName)
            {
                case "genexus_list_objects":
                case "genexus_search":
                    string q = args?["query"]?.ToString() ?? args?["filter"]?.ToString() ?? "";
                    return new { module = "Search", action = "Query", target = q, limit = args?["limit"]?.ToObject<int?>() ?? 50 };
                default:
                    return null;
            }
        }
    }
}
