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
                    name = "genexus_query",
                    description = "Semantic search for objects, references, and snippets. Supports prefixes like 'usedby:Name', 'type:Type', and 'description:Text'.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            query = new { type = "string", description = "The search query." },
                            limit = new { type = "integer", description = "Max results (default 50).", @default = 50 }
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
                case "genexus_query":
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
