using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class PatternAnalysisService
    {
        private readonly ObjectService _objectService;

        public PatternAnalysisService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string GetWWPStructure(string target)
        {
            try
            {
                var obj = _objectService.FindObject(target);
                if (obj == null) return "{\"error\": \"Object not found\"}";

                KBObject instanceObj = null;
                if (obj is Transaction)
                {
                    instanceObj = FindWWPInstance(obj as Transaction);
                }
                else if (obj.TypeDescriptor.Name.Equals("WorkWithPlus", StringComparison.OrdinalIgnoreCase))
                {
                    instanceObj = obj;
                }

                if (instanceObj == null) return "{\"error\": \"WorkWithPlus instance not found for '" + target + "'\"}";

                var part = instanceObj.Parts.Cast<KBObjectPart>().FirstOrDefault(p => 
                    p.Name.Equals("PatternInstance", StringComparison.OrdinalIgnoreCase) ||
                    p.GetType().Name.Contains("PatternInstance") ||
                    p.Type.Equals(new Guid("a51ced48-7bee-0001-ab12-04e9e32123d1")));

                if (part == null) return "{\"error\": \"PatternInstance part not found in " + instanceObj.Name + "\"}";

                string xml = "";
                if (part is ISource sourcePart)
                {
                    xml = sourcePart.Source;
                }
                
                if (string.IsNullOrEmpty(xml))
                {
                    // Try to get from properties if it's not ISource
                    dynamic dPart = part;
                    try { xml = dPart.Properties.Get<string>("InstanceXml"); } catch {}
                    if (string.IsNullOrEmpty(xml)) {
                         try { xml = dPart.Properties.Get<string>("Specification"); } catch {}
                    }
                    if (string.IsNullOrEmpty(xml)) {
                         try { xml = dPart.Properties.Get<string>("Settings"); } catch {}
                    }
                }

                // If still empty, try to serialize the object itself
                if (string.IsNullOrEmpty(xml))
                {
                    try 
                    {
                        using (var writer = new System.IO.StringWriter())
                        {
                            instanceObj.Serialize(writer);
                            xml = writer.ToString();
                        }
                    } 
                    catch { }
                }

                if (string.IsNullOrEmpty(xml)) return "{\"error\": \"Could not extract XML from PatternInstance. Object type: " + instanceObj.GetType().Name + "\"}";

                var result = ParseWWPXml(xml);
                result["rawSnippet"] = xml.Length > 1000 ? xml.Substring(0, 1000) : xml;
                return result.ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private KBObject FindWWPInstance(Transaction trn)
        {
            var model = trn.Model;
            
            // Search by name using SDK compliant way
            // In GX SDK, names are resolved via ResolveName or looking into the collection
            var instance = model.Objects.GetAll()
                                .FirstOrDefault(o => o.Name.Equals("WorkWithPlus" + trn.Name, StringComparison.OrdinalIgnoreCase));
            
            if (instance != null) return instance;

            // Search by children
            return model.Objects.GetChildren(trn)
                        .FirstOrDefault(o => o.TypeDescriptor.Name.Equals("WorkWithPlus", StringComparison.OrdinalIgnoreCase));
        }

        private JObject ParseWWPXml(string xml)
        {
            var result = new JObject();
            try
            {
                // GeneXus serialization often wraps the real Pattern XML in <Data><![CDATA[...]]>
                string realXml = xml;
                if (xml.Contains("<![CDATA["))
                {
                    int start = xml.IndexOf("<![CDATA[") + 9;
                    int end = xml.LastIndexOf("]]>");
                    if (end > start)
                    {
                        realXml = xml.Substring(start, end - start);
                    }
                }

                XDocument doc = XDocument.Parse(realXml);
                var root = doc.Root;
                
                // WWP (DVelop) uses specific tags like <instance>, <level>, <tab>, <grid>
                result["template"] = root?.Attribute("template")?.Value ?? root?.Element("Template")?.Value;
                
                var tabs = new JArray();
                foreach (var tab in doc.Descendants().Where(e => e.Name.LocalName.Equals("tab", StringComparison.OrdinalIgnoreCase)))
                {
                    var tObj = new JObject();
                    tObj["name"] = tab.Attribute("Name")?.Value ?? tab.Attribute("caption")?.Value;
                    tObj["caption"] = tab.Attribute("caption")?.Value;
                    tabs.Add(tObj);
                }
                result["tabs"] = tabs;

                var grids = new JArray();
                foreach (var grid in doc.Descendants().Where(e => e.Name.LocalName.Equals("grid", StringComparison.OrdinalIgnoreCase)))
                {
                    var gObj = new JObject();
                    gObj["name"] = grid.Attribute("name")?.Value ?? grid.Attribute("Name")?.Value;
                    
                    // In WWP, attributes are often children of the grid
                    var attributes = new JArray();
                    foreach (var att in grid.Descendants().Where(e => e.Name.LocalName.Equals("attribute", StringComparison.OrdinalIgnoreCase)))
                    {
                        attributes.Add(att.Attribute("Name")?.Value ?? att.Attribute("name")?.Value);
                    }
                    gObj["attributes"] = attributes;
                    grids.Add(gObj);
                }
                result["grids"] = grids;
            }
            catch (Exception ex)
            {
                result["parsingError"] = ex.Message;
            }
            return result;
        }
    }
}
