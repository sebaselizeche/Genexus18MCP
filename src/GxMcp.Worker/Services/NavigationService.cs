using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class NavigationService
    {
        private readonly KbService _kbService;

        public NavigationService(KbService kbService)
        {
            _kbService = kbService;
        }

        public string GetNavigation(string targetName)
        {
            try
            {
                string nvgPath = FindNavigationFile(targetName);
                if (nvgPath == null) return "{\"error\": \"Navigation report not found for '" + targetName + "'. Make sure the object is specified.\"}";

                // Use Windows-1252 encoding for GeneXus XML reports
                string xmlContent = File.ReadAllText(nvgPath, System.Text.Encoding.GetEncoding(1252));
                XDocument doc = XDocument.Parse(xmlContent);
                var result = new JObject();
                result["name"] = targetName;
                
                var levels = new JArray();
                foreach (var level in doc.Descendants("Level"))
                {
                    var levelObj = new JObject();
                    levelObj["number"] = (int?)level.Element("LevelNumber");
                    levelObj["type"] = level.Element("LevelType")?.Value;
                    levelObj["line"] = (int?)level.Element("LevelBeginRow");
                    
                    var baseTable = level.Element("BaseTable")?.Element("Table");
                    if (baseTable != null)
                    {
                        levelObj["baseTable"] = baseTable.Element("TableName")?.Value;
                        levelObj["baseTableDescription"] = baseTable.Element("Description")?.Value;
                    }

                    levelObj["index"] = level.Element("IndexName")?.Value;
                    
                    var order = new JArray();
                    var orderEl = level.Element("Order");
                    if (orderEl != null)
                    {
                        foreach (var att in orderEl.Elements("Attribute"))
                            order.Add(att.Element("AttriName")?.Value);
                    }
                    levelObj["order"] = order;

                    var optWhere = level.Element("OptimizedWhere");
                    bool hasOptimization = optWhere != null && optWhere.Elements().Any();
                    levelObj["isOptimized"] = hasOptimization;

                    levels.Add(levelObj);
                }

                result["levels"] = levels;

                var warnings = new JArray();
                foreach (var w in doc.Descendants("Warning"))
                    warnings.Add(w.Element("Message")?.Value);
                result["warnings"] = warnings;

                return result.ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private string FindNavigationFile(string targetName)
        {
            var kb = _kbService.GetKB();
            if (kb == null) return null;

            string kbPath = kb.Location;
            var specFolders = Directory.GetDirectories(kbPath, "GXSPC*", SearchOption.TopDirectoryOnly)
                                       .OrderByDescending(d => d);

            foreach (var specFolder in specFolders)
            {
                string genPath = Path.Combine(specFolder, "GEN15", "NVG");
                if (Directory.Exists(genPath))
                {
                    string fullPath = Path.Combine(genPath, targetName + ".xml");
                    if (File.Exists(fullPath)) return fullPath;
                }
            }

            try {
                var nvgDirs = Directory.GetDirectories(kbPath, "NVG", SearchOption.AllDirectories);
                foreach (var dir in nvgDirs)
                {
                    string fullPath = Path.Combine(dir, targetName + ".xml");
                    if (File.Exists(fullPath)) return fullPath;
                }
            } catch {}

            return null;
        }
    }
}
