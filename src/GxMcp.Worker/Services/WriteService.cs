using System;
using System.IO.Compression;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace GxMcp.Worker.Services
{
    public class WriteService
    {
        private readonly ObjectService _objectService;
        private readonly BuildService _buildService;

        // GeneXus Part Type GUIDs
        private const string SOURCE_GUID = "528d1c06-a9c2-420d-bd35-21dca83f12ff";
        private const string RULES_GUID = "9b0a32a3-de6d-4be1-a4dd-1b85d3741534";
        private const string EVENTS_GUID = "c414ed33-d3ca-4837-ad9d-c6c84e02222b";

        public WriteService(ObjectService objectService, BuildService buildService)
        {
            _objectService = objectService;
            _buildService = buildService;
        }

        public string WriteObject(string target, string part, string newCode)
        {
            try
            {
                string xmlContent = _objectService.GetObjectXml(target);
                if (xmlContent == null) return "{\"error\": \"Object not found\"}";

                var doc = new XmlDocument();
                doc.LoadXml(xmlContent);

                string partGuid = GetPartGuid(part);
                if (partGuid == null) return "{\"error\": \"Invalid part: " + part + ". Use Source, Rules, or Events.\"}";

                // Find the part node and update CDATA
                var partNodes = doc.GetElementsByTagName("Part");
                foreach (XmlNode pn in partNodes)
                {
                    if (pn.Attributes?["type"]?.Value == partGuid)
                    {
                        var sourceNode = pn.SelectSingleNode("Source");
                        if (sourceNode != null)
                        {
                            // Replace CDATA content
                            sourceNode.InnerXml = "<![CDATA[" + newCode + "]]>";
                        }
                        break;
                    }
                }

                // Import the modified XML back into the KB
                string tempXml = Path.GetTempFileName() + ".xml";
                doc.Save(tempXml);
                ImportXpz(tempXml);
                File.Delete(tempXml);

                // Invalidate Cache
                _objectService.Invalidate(target);

                return _objectService.ParseGenerusXmlToJson(doc.OuterXml);
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private void ImportXpz(string xmlPath)
        {
            string xpzPath = xmlPath.Replace(".xml", ".xpz");
            string zipPath = xmlPath.Replace(".xml", ".zip");

            // Create XPZ (zip with .xpz extension)
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (File.Exists(xpzPath)) File.Delete(xpzPath);
            
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(xmlPath, Path.GetFileName(xmlPath));
            }
            File.Move(zipPath, xpzPath);

            // Generate import targets
            string targetsFile = Path.GetTempFileName() + ".targets";
            string kbPath = _buildService.GetKBPath();
            string gxDir = @"C:\Program Files (x86)\GeneXus\GeneXus18";

            string content = $@"<Project DefaultTargets='Import' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                <Import Project='{gxDir}\Genexus.Tasks.targets' />
                <Target Name='Import'>
                    <OpenKnowledgeBase Directory='{kbPath}' />
                    <Import File='{xpzPath}' />
                </Target>
            </Project>";
            File.WriteAllText(targetsFile, content, Encoding.UTF8);
            _buildService.RunMSBuild(targetsFile, "Import");

            // Cleanup
            File.Delete(targetsFile);
            File.Delete(xpzPath);
        }

        private string GetPartGuid(string part)
        {
            switch (part?.ToLower())
            {
                case "source": return SOURCE_GUID;
                case "rules": return RULES_GUID;
                case "events": return EVENTS_GUID;
                default: return null;
            }
        }
    }
}
