using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Collections.Generic;

namespace GxMcp.Worker.Services
{
    public class ObjectService
    {
        private BuildService _buildService;
        private string _kbPath;

        public ObjectService(BuildService buildService)
        {
            _buildService = buildService;
            // Hacky config reader for Phase 4
            _kbPath = _buildService.GetKBPath();
        }

        // Cache: Key=ObjectName, Value=XmlContent
        private static readonly System.Collections.Generic.Dictionary<string, string> _cache = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Generic.List<string> _lru = new System.Collections.Generic.List<string>();
        private const int MAX_CACHE_SIZE = 50;

        public string GetObjectXml(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;

            // Check Cache
            if (_cache.ContainsKey(target))
            {
                // Update LRU
                _lru.Remove(target);
                _lru.Add(target);
                Console.Error.WriteLine($"[ObjectService] Cache Hit: {target}");
                return _cache[target];
            }

            // Logic: Export to XPZ -> Unzip -> Read XML
            string tempXpz = Path.GetTempFileName() + ".xpz";
            string targetsFile = Path.GetTempFileName() + ".targets";

            try 
            {
                // Generate Export Target
                string xml = _buildService.GenerateExportTarget(tempXpz, target);
                File.WriteAllText(targetsFile, xml);

                // Run MSBuild
                string output = _buildService.RunMSBuild(targetsFile, "Export");
                
                if (File.Exists(tempXpz))
                {
                    // Unzip
                    string extractDir = Path.Combine(Path.GetTempPath(), "gx_io_" + Guid.NewGuid());
                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(tempXpz, extractDir);

                    // Find XML
                    string xmlFile = Directory.GetFiles(extractDir, "*.xml").FirstOrDefault();
                    if (xmlFile != null)
                    {
                        string content = File.ReadAllText(xmlFile);
                        // Cleanup
                        Directory.Delete(extractDir, true);
                        File.Delete(tempXpz);

                        // Update Cache
                        if (_cache.Count >= MAX_CACHE_SIZE)
                        {
                            string oldest = _lru[0];
                            _lru.RemoveAt(0);
                            _cache.Remove(oldest);
                        }
                        _cache[target] = content;
                        _lru.Add(target);
                        Console.Error.WriteLine($"[ObjectService] Cache Miss: {target} (Stored)");

                        return content; // Return raw XML
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ObjectService Error] {ex.Message}");
                return null;
            }
            finally
            {
                if (File.Exists(targetsFile)) File.Delete(targetsFile);
                if (File.Exists(tempXpz)) File.Delete(tempXpz);
            }
        }

        public void Invalidate(string target)
        {
            if (_cache.ContainsKey(target))
            {
                _cache.Remove(target);
                _lru.Remove(target);
                Console.Error.WriteLine($"[ObjectService] Invalidated: {target}");
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
            _lru.Clear();
            Console.Error.WriteLine($"[ObjectService] Cache Cleared");
        }

        public string ReadObject(string target)
        {
            var xmlContent = GetObjectXml(target);
            if (xmlContent == null) return "{\"error\": \"Object not found: " + CommandDispatcher.EscapeJsonString(target) + "\"}";
            
            try 
            {
                return ParseGenerusXmlToJson(xmlContent);
            }
            catch (Exception ex)
            {
                // Fallback to raw XML if parsing fails
                Console.Error.WriteLine($"[ObjectService] XML Parsing failed: {ex.Message}");
                 return "{\"xml\": \"" + CommandDispatcher.EscapeJsonString(xmlContent) + "\"}";
            }
        }

        public string ParseGenerusXmlToJson(string xmlContent)
        {
            XDocument doc = XDocument.Parse(xmlContent);
            XElement obj = doc.Descendants("Object").FirstOrDefault();
            
            if (obj == null) return "{\"error\": \"No Object found in XML\"}";

            string name = obj.Attribute("name")?.Value;
            string description = obj.Attribute("description")?.Value;
            string type = obj.Attribute("type")?.Value; // Guid

            // Extract Parts
            var parts = new StringBuilder();
            parts.Append("[");
            
            bool firstPart = true;
            foreach (var part in obj.Descendants("Part"))
            {
                // Try to identify part type (Source, Rules, etc) - simpler validation by content
                var source = part.Element("Source");
                if (source != null)
                {
                    if (!firstPart) parts.Append(",");
                    string content = source.Value;
                    string partType = "Source"; 

                    // Heuristic for Part Type based on content or known GUIDs if possible
                    // For now, let's just label it "Code" unless we map GUIDs
                    // 9b0a... = Rules, 528d... = Source, etc.
                    // But simpler: just inspect content or leave as generic Code
                    
                    string partId = part.Attribute("type")?.Value ?? "";
                    if (partId == "528d1c06-a9c2-420d-bd35-21dca83f12ff") partType = "Source";
                    else if (partId == "9b0a32a3-de6d-4be1-a4dd-1b85d3741534") partType = "Rules";
                    else if (partId == "c414ed00-8cc4-4f44-8820-4baf93547173") partType = "Layout";
                    else if (partId == "763f0d8b-d8ac-4db4-8dd4-de8979f2b5b9") partType = "Conditions";
                    else if (partId == "e4c4ade7-53f0-4a56-bdfd-843735b66f47") partType = "Variables";
                    else if (partId == "ad3ca970-19d0-44e1-a7b7-db05556e820c") partType = "Help";

                    parts.Append($"{{\"type\": \"{partType}\", \"content\": \"{CommandDispatcher.EscapeJsonString(content)}\"}}");
                    firstPart = false;
                }
                
                // Variables are distinct in some versions
                var variable = part.Element("Variable");
                // Actually Variables part contains invalid Source usually? 
                // Let's check variables specially
            }
            parts.Append("]");

            // Extract Attributes/Variables list from XML structure if needed
            // But simple Source extraction covers 90% of needs
            
            return $"{{\"name\": \"{name}\", \"description\": \"{description}\", \"parts\": {parts.ToString()}}}";
        }
    }
}
