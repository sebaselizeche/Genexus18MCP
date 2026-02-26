using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace GxMcp.Gateway.Routers
{
    public class ObjectRouter : IMcpModuleRouter
    {
        public string ModuleName => "Object";

        public object[] GetToolDefinitions()
        {
            return new object[]
            {
                new {
                    name = "genexus_read_object",
                    description = "Reads full object metadata (GUID, Type, and all available Parts list). Use for structural inspection.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_read_source",
                    description = "Reads source code with SMART context. Automatically includes metadata for variables used in the snippet. Supports pagination via offset/limit to save tokens.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            name = new { type = "string", description = "Object name." },
                            part = new { type = "string", description = "Part (Source, Rules, Events).", @default = "Source" },
                            offset = new { type = "integer", description = "Start line (0-based) for pagination." },
                            limit = new { type = "integer", description = "Number of lines to read." }
                        },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_write_object",
                    description = "Directly updates object source code. Use with caution for large objects. Automatically handles variable injection based on KB attributes.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            name = new { type = "string", description = "Object name." },
                            part = new { type = "string", description = "Part name.", @default = "Source" },
                            code = new { type = "string", description = "Full source code to write." }
                        },
                        required = new[] { "name", "code" }
                    }
                },
                new {
                    name = "genexus_patch",
                    description = "Surgical text editing. High-precision replacement or insertion within an object part. Use unique 'context' (old_string) to target specific lines safely. Preserves indentation.",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            name = new { type = "string", description = "Object name." },
                            part = new { type = "string", description = "Part name (Source, Rules, Events).", @default = "Source" },
                            operation = new { type = "string", description = "Operation: Replace, Insert_After, Append." },
                            content = new { type = "string", description = "New text to insert or replacement text." },
                            context = new { type = "string", description = "The exact 'old_string' to match. Must be unique unless expectedCount is set." },
                            expectedCount = new { type = "integer", description = "Number of replacements expected. Defaults to 1.", @default = 1 }
                        },
                        required = new[] { "name", "operation", "content" }
                    }
                },
                new {
                    name = "genexus_get_variables",
                    description = "Lists all variables defined in an object, including their types.",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Object name." } },
                        required = new[] { "name" }
                    }
                },
                new {
                    name = "genexus_get_attribute",
                    description = "Returns metadata for a specific GeneXus attribute (Type, Length, Domain, Table).",
                    inputSchema = new {
                        type = "object",
                        properties = new { name = new { type = "string", description = "Attribute name." } },
                        required = new[] { "name" }
                    }
                }
            };
        }

        public object ConvertToolCall(string toolName, JObject args)
        {
            switch (toolName)
            {
                case "genexus_read_object":
                    return new { module = "Read", action = "Export", target = args?["name"]?.ToString() };
                case "genexus_read_source":
                    return new { 
                        module = "Read", 
                        action = "ExtractSource", 
                        target = args?["name"]?.ToString(), 
                        part = args?["part"]?.ToString() ?? "Source",
                        offset = args?["offset"]?.ToObject<int?>(),
                        limit = args?["limit"]?.ToObject<int?>()
                    };
                case "genexus_write_object":
                    return new { module = "Write", action = args?["part"]?.ToString() ?? "Source", target = args?["name"]?.ToString(), payload = args?["code"]?.ToString() };
                case "genexus_patch":
                    return new { 
                        module = "Patch", 
                        action = "Apply", 
                        target = args?["name"]?.ToString(),
                        part = args?["part"]?.ToString() ?? "Source",
                        operation = args?["operation"]?.ToString(),
                        content = args?["content"]?.ToString(),
                        context = args?["context"]?.ToString(),
                        expectedCount = args?["expectedCount"]?.ToObject<int?>() ?? 1
                    };
                case "genexus_get_variables":
                    return new { module = "Read", action = "GetVariables", target = args?["name"]?.ToString() };
                case "genexus_get_attribute":
                    return new { module = "Read", action = "GetAttribute", target = args?["name"]?.ToString() };
                default:
                    return null;
            }
        }
    }
}
