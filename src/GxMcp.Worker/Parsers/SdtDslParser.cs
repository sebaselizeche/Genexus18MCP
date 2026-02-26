using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Parsers
{
    public class SdtDslParser : IDslParser
    {
        private static readonly Guid SDT_STRUCTURE_PART = Guid.Parse("8597371d-1941-4c12-9c17-48df9911e2f3");

        public void Serialize(KBObject obj, StringBuilder sb)
        {
            if (obj.TypeDescriptor.Name.Equals("SDT", StringComparison.OrdinalIgnoreCase))
            {
                dynamic sdt = obj;
                dynamic structure = sdt.Parts.Get(SDT_STRUCTURE_PART);
                if (structure != null && structure.Root != null)
                {
                    foreach (dynamic child in structure.Root.Children) SerializeLevel(child, sb, 0);
                }
            }
        }

        public void Parse(KBObject obj, string text)
        {
            if (obj.TypeDescriptor.Name.Equals("SDT", StringComparison.OrdinalIgnoreCase))
            {
                dynamic sdt = obj;
                dynamic structure = sdt.Parts.Get(SDT_STRUCTURE_PART);
                if (structure != null && structure.Root != null)
                {
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Where(l => !string.IsNullOrWhiteSpace(l))
                                    .ToList();
                    var parsedNodes = DslParserUtils.ParseLinesIntoNodes(lines);
                    SyncSDTNodes(structure.Root, parsedNodes);
                }
            }
        }

        private void SerializeLevel(dynamic level, StringBuilder sb, int indent)
        {
            string indentStr = new string(' ', indent * 4);
            string collectionMarker = level.IsCollection ? " Collection" : "";
            if (level.IsCompound)
            {
                sb.AppendLine($"{indentStr}{level.Name}{collectionMarker}");
                sb.AppendLine($"{indentStr}{{");
                foreach (dynamic child in level.Children) SerializeLevel(child, sb, indent + 1);
                sb.AppendLine($"{indentStr}}}");
            }
            else
            {
                string typeStr = level.Type != null ? level.Type.ToString() : "Unknown";
                sb.AppendLine($"{indentStr}{level.Name} : {typeStr}{collectionMarker}");
            }
        }

        private void SyncSDTNodes(dynamic sdkLevel, List<DslParserUtils.ParsedNode> parsedNodes)
        {
            var existingItems = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            if (sdkLevel.Children != null) { foreach (dynamic child in sdkLevel.Children) existingItems[child.Name] = child; }

            var toRemove = new List<dynamic>();
            foreach (dynamic child in sdkLevel.Children) {
                if (!parsedNodes.Any(p => p.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase))) toRemove.Add(child);
            }
            foreach (dynamic dead in toRemove) { try { sdkLevel.Children.Remove(dead); } catch {} }

            foreach (var pNode in parsedNodes)
            {
                dynamic targetChild = null;
                if (existingItems.TryGetValue(pNode.Name, out var existing)) targetChild = existing;
                else
                {
                    Type sdtItemType = sdkLevel.GetType().Assembly.GetType("Artech.Genexus.Common.Parts.SDTItem");
                    if (sdtItemType != null) {
                        targetChild = Activator.CreateInstance(sdtItemType, new object[] { sdkLevel });
                        targetChild.Name = pNode.Name;
                        sdkLevel.Children.Add(targetChild);
                    }
                }

                if (targetChild != null)
                {
                    targetChild.IsCollection = pNode.IsCollection;
                    if (pNode.IsCompound) SyncSDTNodes(targetChild, pNode.Children);
                    else
                    {
                        try {
                            Type eDBType = targetChild.GetType().Assembly.GetType("Artech.Genexus.Common.eDBType");
                            if (pNode.TypeStr.StartsWith("Numeric", StringComparison.OrdinalIgnoreCase)) targetChild.Type = Enum.Parse(eDBType, "NUMERIC");
                            else if (pNode.TypeStr.StartsWith("Char", StringComparison.OrdinalIgnoreCase)) targetChild.Type = Enum.Parse(eDBType, "VARCHAR");
                            else if (pNode.TypeStr.StartsWith("Date", StringComparison.OrdinalIgnoreCase)) targetChild.Type = Enum.Parse(eDBType, "DATE");
                            else if (pNode.TypeStr.StartsWith("Bool", StringComparison.OrdinalIgnoreCase)) targetChild.Type = Enum.Parse(eDBType, "Boolean");
                            else targetChild.Type = Enum.Parse(eDBType, "VARCHAR");
                        } catch { }
                    }
                }
            }
        }
    }
}
