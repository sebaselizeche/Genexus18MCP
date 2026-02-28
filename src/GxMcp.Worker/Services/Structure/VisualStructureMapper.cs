using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services.Structure
{
    public static class VisualStructureMapper
    {
        public static JObject MapAttribute(dynamic attr)
        {
            string name = "Unknown";
            bool isKey = false;
            try { name = attr.Name; } catch { }
            try { isKey = attr.IsKey; } catch { }
            
            var item = new JObject { ["name"] = name, ["isKey"] = isKey, ["isLevel"] = false };
            string typeStr = "Unknown";
            string desc = "";
            string formula = "";
            string isNullable = "No";

            try {
                // Try to get the underlying Attribute object
                Artech.Genexus.Common.Objects.Attribute attrObj = null;
                try { 
                    if (attr is TransactionAttribute trnAttr) attrObj = trnAttr.Attribute;
                    else if (attr is TableAttribute tblAttr) attrObj = tblAttr.Attribute;
                    else attrObj = attr.Attribute; 
                } catch { }

                if (attrObj != null) {
                    try { 
                        if (attrObj.Type != null) typeStr = attrObj.Type.ToString(); 
                    } catch { }

                    try { desc = attrObj.Description?.ToString() ?? ""; } catch { }
                    try { formula = attrObj.Formula?.ToString() ?? ""; } catch { }
                    
                    try {
                        int len = (int)attrObj.Length;
                        int dec = (int)attrObj.Decimals;
                        if (len > 0) typeStr += (dec > 0) ? $"({len},{dec})" : $"({len})";
                    } catch { }
                }

                // If typeStr is still Unknown, try to get it from the item itself (for Tables)
                if (typeStr == "Unknown") {
                    try { if (attr.Type != null) typeStr = attr.Type.ToString(); } catch { }
                }

                // Nullable handling
                try {
                    // Try to cast to the specific enum type we found earlier
                    int nVal = (int)attr.IsNullable;
                    isNullable = (nVal == 1) ? "Yes" : (nVal == 2 ? "Managed" : "No");
                } catch { }
            } catch (Exception ex) {
                Logger.Error($"[VisualStructureMapper] Fatal error mapping {name}: {ex.Message}");
            }

            item["type"] = typeStr;
            item["description"] = desc;
            item["formula"] = formula;
            item["nullable"] = isNullable;
            return item;
        }

        public static void SyncAttributeProperties(dynamic targetAttr, JToken vItem, HashSet<KBObject> modifiedTracker)
        {
            if (targetAttr.Attribute == null) return;

            string desc = vItem["description"]?.ToString();
            string formula = vItem["formula"]?.ToString();
            string nullable = vItem["nullable"]?.ToString();
            string userType = vItem["type"]?.ToString();

            bool isModified = false;

            if (desc != null && targetAttr.Attribute.Description != desc) {
                targetAttr.Attribute.Description = desc; isModified = true;
            }

            if (formula != null) {
                string current = targetAttr.Attribute.Formula?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(formula)) {
                    if (targetAttr.Attribute.Formula != null) { targetAttr.Attribute.Formula = null; isModified = true; }
                } else if (formula != current) {
                    try {
                        targetAttr.Attribute.Formula = Formula.Parse(formula, targetAttr.Attribute, null);
                        targetAttr.IsKey = false; isModified = true;
                    } catch { }
                }
            }

            if (nullable != null) {
                int val = nullable.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? 1 : (nullable.Equals("Managed", StringComparison.OrdinalIgnoreCase) ? 2 : 0);
                if ((int)targetAttr.IsNullable != val) {
                    targetAttr.IsNullable = (dynamic)val;
                    try { targetAttr.Attribute.SetPropertyValue("Nullable", val); } catch { }
                    isModified = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(userType)) {
                var match = System.Text.RegularExpressions.Regex.Match(userType, @"^([a-zA-Z]+)(?:\((\d+)(?:[,.](\d+))?\))?");
                if (match.Success) {
                    string tName = match.Groups[1].Value.ToUpper();
                    if (Enum.TryParse<Artech.Genexus.Common.eDBType>(tName, true, out var eType)) {
                        if (targetAttr.Attribute.Type != eType) { targetAttr.Attribute.Type = eType; isModified = true; }
                        int len = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
                        if (len > 0 && targetAttr.Attribute.Length != len) { targetAttr.Attribute.Length = len; isModified = true; }
                        int dec = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
                        if (dec > 0 && targetAttr.Attribute.Decimals != dec) { targetAttr.Attribute.Decimals = dec; isModified = true; }
                    }
                }
            }

            if (isModified) modifiedTracker.Add(targetAttr.Attribute);
        }
    }
}
