using System;
using System.Collections.Generic;
using System.Linq;

namespace GxMcp.Worker.Helpers
{
    public static class DslParserUtils
    {
        public class ParsedNode
        {
            public string Name { get; set; }
            public string TypeStr { get; set; }
            public bool IsCollection { get; set; }
            public bool IsCompound { get; set; }
            public bool IsKey { get; set; }
            public List<ParsedNode> Children { get; set; } = new List<ParsedNode>();
        }

        public static List<ParsedNode> ParseLinesIntoNodes(List<string> lines)
        {
            var rootNodes = new List<ParsedNode>();
            var stack = new Stack<(ParsedNode Node, int Indent)>();

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                int indent = line.TakeWhile(c => c == ' ').Count();
                string trimmed = line.Trim();
                
                int commentIndex = trimmed.IndexOf("//");
                if (commentIndex >= 0) trimmed = trimmed.Substring(0, commentIndex).Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                
                if (trimmed == "{" || trimmed == "}") {
                    if (trimmed == "}" && stack.Count > 0) stack.Pop();
                    continue;
                }

                var node = new ParsedNode();
                if (trimmed.EndsWith("Collection", StringComparison.OrdinalIgnoreCase)) {
                    trimmed = trimmed.Substring(0, trimmed.Length - 10).Trim();
                    node.IsCollection = true;
                }

                if (trimmed.EndsWith("*")) {
                    node.IsKey = true;
                    trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
                }

                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex > 0) {
                    node.Name = trimmed.Substring(0, colonIndex).Trim();
                    node.TypeStr = trimmed.Substring(colonIndex + 1).Trim();
                    node.IsCompound = false;
                } else {
                    node.Name = trimmed;
                    node.IsCompound = true;
                    if (i + 1 < lines.Count && lines[i + 1].Trim() != "{") {
                        node.IsCompound = false;
                        node.TypeStr = "Unknown";
                    }
                }

                while (stack.Count > 0 && stack.Peek().Indent >= indent) stack.Pop();
                if (stack.Count == 0) rootNodes.Add(node);
                else stack.Peek().Node.Children.Add(node);
                if (node.IsCompound) stack.Push((node, indent));
            }
            return rootNodes;
        }
    }
}
