using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Descriptors;
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

        public string CreateObject(string type, string name)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var kb = _kbService.GetKB();
                if (kb == null) return "{\"status\":\"Error\", \"error\":\"No KB open\"}";

                Logger.Info(string.Format("Creating Object: {0} ({1})", name, type));

                // Map string type to Guid
                Guid typeGuid = Guid.Empty;
                if (type.Equals("Procedure", StringComparison.OrdinalIgnoreCase)) typeGuid = KBObjectDescriptor.Get<global::Artech.Genexus.Common.Objects.Procedure>().Id;
                else if (type.Equals("Transaction", StringComparison.OrdinalIgnoreCase)) typeGuid = KBObjectDescriptor.Get<global::Artech.Genexus.Common.Objects.Transaction>().Id;
                else if (type.Equals("WebPanel", StringComparison.OrdinalIgnoreCase)) typeGuid = KBObjectDescriptor.Get<global::Artech.Genexus.Common.Objects.WebPanel>().Id;
                else if (type.Equals("SDT", StringComparison.OrdinalIgnoreCase) || type.Equals("StructuredDataType", StringComparison.OrdinalIgnoreCase)) typeGuid = KBObjectDescriptor.Get<global::Artech.Genexus.Common.Objects.SDT>().Id;
                else if (type.Equals("DataProvider", StringComparison.OrdinalIgnoreCase)) typeGuid = KBObjectDescriptor.Get<global::Artech.Genexus.Common.Objects.DataProvider>().Id;
                else if (type.Equals("Attribute", StringComparison.OrdinalIgnoreCase)) typeGuid = KBObjectDescriptor.Get<global::Artech.Genexus.Common.Objects.Attribute>().Id;
                else if (type.Equals("Table", StringComparison.OrdinalIgnoreCase)) typeGuid = KBObjectDescriptor.Get<global::Artech.Genexus.Common.Objects.Table>().Id;
                else if (type.Equals("SDPanel", StringComparison.OrdinalIgnoreCase)) typeGuid = Guid.Parse("702119eb-90e9-4e78-958b-96d5e182283a"); // SDPanel Guid

                if (typeGuid == Guid.Empty) return "{\"status\":\"Error\", \"error\":\"Unsupported object type: " + type + "\"}";

                KBObject newObj = KBObject.Create(kb.DesignModel, typeGuid);
                newObj.Name = name;
                
                // Initialize with some default content if possible
                if (newObj is global::Artech.Genexus.Common.Objects.Procedure proc)
                {
                    proc.ProcedurePart.Source = "// Procedure: " + name + "\n\n";
                }
                else if (newObj is global::Artech.Genexus.Common.Objects.DataProvider dp)
                {
                    var part = dp.Parts.Get<SourcePart>();
                    if (part != null) part.Source = "// Data Provider: " + name + "\n\n";
                }

                newObj.Save();
                
                Logger.Info(string.Format("Object created successfully in {0}ms", sw.ElapsedMilliseconds));
                return "{\"status\":\"Success\", \"type\":\"" + type + "\", \"name\":\"" + name + "\"}";
            }
            catch (Exception ex)
            {
                Logger.Error("CreateObject failed: " + ex.Message);
                return "{\"status\":\"Error\", \"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        public KBObject FindObject(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            var sw = Stopwatch.StartNew();
            var kb = _kbService.GetKB();
            if (kb == null) return null;

            string typePart = null;
            string namePart = target.Trim();

            if (target.Contains(":"))
            {
                var parts = target.Split(':');
                typePart = parts[0].Trim();
                namePart = parts[1].Trim();
            }

            // 1. Precise search with type if provided
            if (typePart != null)
            {
                foreach (KBObject obj in kb.DesignModel.Objects.GetByName(null, null, namePart))
                {
                    if (string.Equals(obj.TypeDescriptor.Name, typePart, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug(string.Format("FindObject '{0}' SUCCESS (Typed) in {1}ms", target, sw.ElapsedMilliseconds));
                        return obj;
                    }
                }
            }

            // 2. Global search, prioritizing non-container objects
            KBObject firstMatch = null;
            foreach (KBObject obj in kb.DesignModel.Objects.GetByName(null, null, namePart))
            {
                if (firstMatch == null) firstMatch = obj;
                
                string type = obj.TypeDescriptor.Name;
                if (type != "Folder" && type != "Module")
                {
                    Logger.Debug(string.Format("FindObject '{0}' SUCCESS (Global) in {1}ms", target, sw.ElapsedMilliseconds));
                    return obj;
                }
            }

            if (firstMatch != null)
            {
                Logger.Debug(string.Format("FindObject '{0}' SUCCESS (Container) in {1}ms", target, sw.ElapsedMilliseconds));
            }
            return firstMatch;
        }

        public string ReadObject(string target)
        {
            var obj = FindObject(target);
            if (obj == null) return "{\"error\": \"Object not found: " + target + "\"}";

            var parts = new JArray();
            foreach (KBObjectPart p in obj.Parts)
            {
                parts.Add(new JObject { 
                    ["name"] = p.TypeDescriptor?.Name ?? p.Type.ToString(), 
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
                if (obj == null) return "{\"error\": \"Object not found: " + target + "\"}";

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
                if (partGuid != Guid.Empty)
                {
                    foreach (KBObjectPart p in obj.Parts)
                    {
                        if (p.Type == partGuid) { part = p; break; }
                    }
                }

                // Dynamic Discovery Fallback: if part not found by GUID, try to find by type or interface
                if (part == null)
                {
                    foreach (KBObjectPart p in obj.Parts)
                    {
                        if (p is ISource && (partName.Equals("Source", StringComparison.OrdinalIgnoreCase) || partName.Equals("Code", StringComparison.OrdinalIgnoreCase) || partName.Equals("Events", StringComparison.OrdinalIgnoreCase)))
                        {
                            part = p;
                            break;
                        }
                        if (p is global::Artech.Genexus.Common.Parts.VariablesPart && partName.Equals("Variables", StringComparison.OrdinalIgnoreCase))
                        {
                            part = p;
                            break;
                        }
                    }
                }

                if (part == null) return "{\"error\": \"Part '" + partName + "' not found in " + obj.TypeDescriptor.Name + ". Available parts: " + string.Join(", ", obj.Parts.Cast<KBObjectPart>().Select(p => p.TypeDescriptor?.Name ?? p.Type.ToString())) + "\"}";

                JObject result = new JObject();
                
                // Handle Variables Part specially
                if (part is global::Artech.Genexus.Common.Parts.VariablesPart varPart)
                {
                    string varText = VariableInjector.GetVariablesAsText(varPart);
                    result["source"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(varText));
                    result["isBase64"] = true;
                    Logger.Info("ReadSource (Variables as Base64) SUCCESS");
                }
                else if (part is ISource sourcePart)
                {
                    string content = sourcePart.Source ?? "";
                    
                    if (offset.HasValue || limit.HasValue)
                    {
                        string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        int start = offset ?? 0;
                        int count = limit ?? (lines.Length - start);
                        
                        if (start < 0) start = 0;
                        if (start >= lines.Length) count = 0;
                        else if (start + count > lines.Length) count = lines.Length - start;

                        string paginatedContent = string.Join(Environment.NewLine, lines.Skip(start).Take(count));
                        result["source"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(paginatedContent));
                        result["isBase64"] = true;
                        result["offset"] = start;
                        result["limit"] = count;
                        result["totalLines"] = lines.Length;

                        AddVariableMetadata(obj, paginatedContent, result);
                        Logger.Info(string.Format("ReadSource (Paginated Base64 {0}-{1}) SUCCESS", start, start+count));
                    }
                    else
                    {
                        result["source"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));
                        result["isBase64"] = true;
                        AddVariableMetadata(obj, content, result);
                        Logger.Info("ReadSource (Full Base64) SUCCESS");
                    }
                }
                else
                {
                    string xml = part.SerializeToXml();
                    result["source"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(xml));
                    result["isBase64"] = true;
                    Logger.Info("ReadSource (XML Base64) SUCCESS");
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
                case "structure": return Guid.Parse("1608677c-a7a2-4a00-8809-6d2466085a5a");
                case "webform": return Guid.Parse("d24a58ad-57ba-41b7-9e6e-eaca3543c778");
                case "help": return Guid.Parse("017ea008-6202-4468-a400-3f412c938473");
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
            
            if (objType.Equals("WebPanel", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "events" || p == "source" || p == "code") return Guid.Parse("c44bd5ff-f918-415b-98e6-aca44fed84fa");
                if (p == "rules") return Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "layout") return Guid.Parse("ad3ca970-19d0-44e1-a7b7-db05556e820c");
                if (p == "webform") return Guid.Parse("d24a58ad-57ba-41b7-9e6e-eaca3543c778");
            }

            if (objType.Equals("Transaction", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "structure") return Guid.Parse("1608677c-a7a2-4a00-8809-6d2466085a5a");
                if (p == "events" || p == "source" || p == "code") return Guid.Parse("c44bd5ff-f918-415b-98e6-aca44fed84fa");
                if (p == "rules") return Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "webform") return Guid.Parse("d24a58ad-57ba-41b7-9e6e-eaca3543c778");
            }

            if (objType.Equals("DataProvider", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "source" || p == "code") return Guid.Parse("91705646-6086-4f32-8871-08149817e754");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "help") return Guid.Parse("017ea008-6202-4468-a400-3f412c938473");
            }

            if (objType.Equals("SDT", StringComparison.OrdinalIgnoreCase) || objType.Equals("StructuredDataType", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "structure" || p == "source") return Guid.Parse("8597371d-1941-4c12-9c17-48df9911e2f3");
            }

            return GetPartGuid(p);
        }
    }
}
