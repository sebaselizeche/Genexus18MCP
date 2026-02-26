using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;

namespace GxMcp.Worker.Parsers
{
    public class TableDslParser : IDslParser
    {
        public void Serialize(KBObject obj, StringBuilder sb)
        {
            if (obj is Table tbl)
            {
                try {
                    dynamic dStructure = ((dynamic)tbl).TableStructure;
                    foreach (dynamic attr in dStructure.Attributes)
                    {
                        string keyMarker = (bool)attr.IsKey ? "*" : "";
                        string typeStr = "Unknown";
                        string desc = "";
                        string formula = "";
                        bool isNullable = false;

                        try {
                            if (attr.Attribute != null) {
                                if (attr.Attribute.Type != null) typeStr = attr.Attribute.Type.ToString();
                                int len = attr.Attribute.Length;
                                int dec = attr.Attribute.Decimals;
                                if (len > 0) {
                                    if (dec > 0) typeStr += $"({len},{dec})";
                                    else typeStr += $"({len})";
                                }
                                desc = attr.Attribute.Description?.ToString() ?? "";
                                formula = attr.Attribute.Formula?.ToString() ?? "";
                                try {
                                    int nVal = (int)attr.IsNullable;
                                    isNullable = (nVal == 1);
                                } catch { }
                            }
                        } catch { }

                        var lineElements = new List<string>();
                        lineElements.Add(string.Format("{0}{1} : {2}", attr.Name, keyMarker, typeStr));
                        if (!string.IsNullOrEmpty(desc) && !desc.Equals(attr.Name, StringComparison.OrdinalIgnoreCase)) lineElements.Add(string.Format("\"{0}\"", desc));
                        if (!string.IsNullOrEmpty(formula)) lineElements.Add(string.Format("[Formula: {0}]", formula));
                        if (isNullable) lineElements.Add("[Nullable]");

                        string extraInfo = lineElements.Count > 1 ? " // " + string.Join(", ", lineElements.Skip(1)) : "";
                        sb.AppendLine(string.Format("{0}{1}", lineElements[0], extraInfo));
                    }
                } catch (Exception ex) {
                    sb.AppendLine("// Error serializing table: " + ex.Message);
                }
            }
        }

        public void Parse(KBObject obj, string text)
        {
            // Table DSL is currently read-only in the mirror
        }
    }
}
