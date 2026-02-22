using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class ObjectService
    {
        private readonly KbService _kbService;
        private readonly BuildService _buildService;
        private DataInsightService _dataInsightService;
        private UIService _uiService;

        public ObjectService(KbService kbService, BuildService buildService)
        {
            _kbService = kbService;
            _buildService = buildService;
        }

        public void SetDataInsightService(DataInsightService ds) { _dataInsightService = ds; }
        public void SetUIService(UIService ui) { _uiService = ui; }
        public KbService GetKbService() { return _kbService; }

        public SearchIndex GetIndex() { return _kbService.GetIndexCache().GetIndex(); }

        public KBObject FindObject(string target)
        {
            var sw = Stopwatch.StartNew();
            var kb = _kbService.GetKB();
            if (kb == null) return null;

            string typePart = null;
            string namePart = target;

            if (target.Contains(":"))
            {
                var parts = target.Split(':');
                typePart = parts[0];
                namePart = parts[1];
            }

            foreach (KBObject obj in kb.DesignModel.Objects.GetByName(null, null, namePart))
            {
                if (typePart == null || string.Equals(obj.TypeDescriptor.Name, typePart, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Debug(string.Format("FindObject '{0}' SUCCESS in {1}ms", target, sw.ElapsedMilliseconds));
                    return obj;
                }
            }
            return null;
        }

        public string ReadObject(string target)
        {
            var obj = FindObject(target);
            if (obj == null) return "{\"error\": \"Object not found\"}";

            var parts = new JArray();
            foreach (KBObjectPart p in obj.Parts)
            {
                parts.Add(new JObject { 
                    ["name"] = p.Type.ToString(), 
                    ["guid"] = p.Type.ToString() 
                });
            }

            string parentName = null;
            string moduleName = null;

            try { parentName = obj.Parent?.Name; } catch { }
            try { moduleName = obj.Module?.Name; } catch { }

            return new JObject { 
                ["name"] = obj.Name, 
                ["type"] = obj.TypeDescriptor.Name,
                ["parent"] = parentName,
                ["module"] = moduleName,
                ["parts"] = parts
            }.ToString();
        }

        public string ReadObjectSource(string target, string partName, int? offset = null, int? limit = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var obj = FindObject(target);
                if (obj == null) return "{\"error\": \"Object not found\"}";

                // Special handling for Layout
                if (partName.Equals("layout", StringComparison.OrdinalIgnoreCase))
                {
                    var uiContextJson = _uiService.GetUIContext(target);
                    var uiObj = JObject.Parse(uiContextJson);
                    if (uiObj["html"] != null)
                    {
                        var layoutResult = new JObject();
                        layoutResult["source"] = uiObj["html"].ToString();
                        return layoutResult.ToString();
                    }
                }

                Guid partGuid = MapLogicalPartToGuid(obj.TypeDescriptor.Name, partName);
                
                KBObjectPart part = null;
                foreach (KBObjectPart p in obj.Parts)
                {
                    if (p.Type == partGuid) { part = p; break; }
                }

                // Dynamic Discovery Fallback: if part not found by GUID, try to find by type
                if (part == null)
                {
                    if (partName.Equals("Source", StringComparison.OrdinalIgnoreCase) || partName.Equals("Code", StringComparison.OrdinalIgnoreCase))
                        part = obj.Parts.Cast<KBObjectPart>().FirstOrDefault(p => p is ISource);
                    else if (partName.Equals("Variables", StringComparison.OrdinalIgnoreCase))
                        part = obj.Parts.Cast<KBObjectPart>().FirstOrDefault(p => p is global::Artech.Genexus.Common.Parts.VariablesPart);
                }

                if (part == null) return "{\"error\": \"Part '" + partName + "' not found in " + obj.TypeDescriptor.Name + ". Available parts: " + string.Join(", ", obj.Parts.Cast<KBObjectPart>().Select(p => p.Type.ToString())) + "\"}";

                JObject result = new JObject();
                
                // Handle Variables Part specially
                if (part is global::Artech.Genexus.Common.Parts.VariablesPart varPart)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (global::Artech.Genexus.Common.Variable v in varPart.Variables)
                    {
                        sb.AppendLine(string.Format("&{0} : {1}({2}{3})", v.Name, v.Type, v.Length, v.Decimals > 0 ? "," + v.Decimals : ""));
                    }
                    result["source"] = sb.ToString();
                    Logger.Info("ReadSource (Variables as Text) SUCCESS");
                }
                else if (part is ISource sourcePart)
                {
                    string content = sourcePart.Source;
                    
                    if (offset.HasValue || limit.HasValue)
                    {
                        string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        int start = offset ?? 0;
                        int count = limit ?? (lines.Length - start);
                        
                        if (start < 0) start = 0;
                        if (start >= lines.Length) count = 0;
                        else if (start + count > lines.Length) count = lines.Length - start;

                        string paginatedContent = string.Join(Environment.NewLine, lines.Skip(start).Take(count));
                        result["source"] = paginatedContent;
                        result["offset"] = start;
                        result["limit"] = count;
                        result["totalLines"] = lines.Length;

                        AddVariableMetadata(obj, paginatedContent, result);
                        Logger.Info(string.Format("ReadSource (Paginated {0}-{1}) SUCCESS", start, start+count));
                    }
                    else
                    {
                        result["source"] = content;
                        AddVariableMetadata(obj, content, result);
                        Logger.Info("ReadSource (Full Text) SUCCESS");
                    }
                }
                else
                {
                    result["source"] = part.SerializeToXml();
                    Logger.Info("ReadSource (XML serialized) SUCCESS");
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private void AddVariableMetadata(KBObject obj, string source, JObject result)
        {
            try
            {
                var variables = new JArray();
                global::Artech.Genexus.Common.Parts.VariablesPart varPart = obj.Parts.Get<global::Artech.Genexus.Common.Parts.VariablesPart>();
                if (varPart == null) return;

                foreach (global::Artech.Genexus.Common.Variable v in varPart.Variables)
                {
                    if (source.Contains("&" + v.Name))
                    {
                        variables.Add(new JObject {
                            ["name"] = v.Name,
                            ["type"] = v.Type.ToString(),
                            ["length"] = v.Length,
                            ["decimals"] = v.Decimals
                        });
                    }
                }
                if (variables.Count > 0) result["variables"] = variables;
            }
            catch { }
        }

        public static Guid GetPartGuid(string name)
        {
            if (string.IsNullOrEmpty(name)) return Guid.Empty;
            switch (name.ToLower())
            {
                case "source": return Guid.Parse("c5f0ef88-9ef8-4218-bf76-915024b3c48f");
                case "rules": return Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
                case "events": return Guid.Parse("c44bd5ff-f918-415b-98e6-aca44fed84fa");
                case "variables": return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                default: return Guid.Empty;
            }
        }

        private Guid MapLogicalPartToGuid(string objType, string partName)
        {
            string p = partName.ToLower();
            
            // GUIDs Oficiais GeneXus 18
            if (objType.Equals("Procedure", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "source" || p == "code") return Guid.Parse("c5f0ef88-9ef8-4218-bf76-915024b3c48f");
                if (p == "rules") return Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "help") return Guid.Parse("017ea008-6202-4468-a400-3f412c938473");
            }
            
            if (objType.Equals("WebPanel", StringComparison.OrdinalIgnoreCase) || objType.Equals("Transaction", StringComparison.OrdinalIgnoreCase))
            {
                // Em WebPanels e Transactions, 'Source' lógico mapeia para 'Events' do SDK
                if (p == "events" || p == "source" || p == "code") return Guid.Parse("c44bd5ff-f918-415b-98e6-aca44fed84fa");
                if (p == "rules") return Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "layout") return Guid.Parse("ad3ca970-19d0-44e1-a7b7-db05556e820c");
                if (p == "webform") return Guid.Parse("d24a58ad-57ba-41b7-9e6e-eaca3543c778");
            }

            if (objType.Equals("Transaction", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "structure") return Guid.Parse("1608677c-a7a2-4a00-8809-6d2466085a5a");
            }

            return GetPartGuid(p);
        }
    }
}
