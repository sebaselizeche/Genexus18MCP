using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;
using System.Diagnostics;
using System.Reflection;

namespace GxMcp.Worker.Services
{
    public class ObjectService
    {
        private readonly KbService _kbService;
        private readonly IndexCacheService _indexCache;

        // Guaranteed GUIDs for GX18
        private static readonly Guid PART_PROCEDURE = new Guid("528d1c06-a9c2-420d-bd35-21dca83f12ff");
        private static readonly Guid PART_RULES     = new Guid("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
        private static readonly Guid PART_EVENTS    = new Guid("c44bd5ff-f918-415b-98e6-aca44fed84fa");
        private static readonly Guid PART_VARIABLES = new Guid("e4c4ade7-53f0-4a56-bdfd-843735b66f47");

        public ObjectService(KbService kbService)
        {
            _kbService = kbService;
            _indexCache = new IndexCacheService();
        }

        public global::Artech.Architecture.Common.Objects.KnowledgeBase GetKB() { return _kbService.GetKB(); }

        public global::Artech.Architecture.Common.Objects.KBObject FindObject(string target)
        {
            var sw = Stopwatch.StartNew();
            try {
                var kb = GetKB();
                if (kb == null) return null;

                string namePart = target.Contains(":") ? target.Split(':')[1] : target;
                
                // PERFORMANCE BOOST: Use our Index Cache to find the Type first!
                var index = _indexCache.GetIndex();
                Guid? typeGuid = null;
                if (index != null)
                {
                    var entry = index.Objects.Values.FirstOrDefault(o => string.Equals(o.Name, namePart, StringComparison.OrdinalIgnoreCase));
                    if (entry != null) {
                        // Attempt to map type name back to SDK Guid if needed
                        Logger.Debug($"Index found '{namePart}' as {entry.Type}");
                    }
                }

                // GX18 Signature: GetByName(string namespace, Guid? type, string name)
                var results = kb.DesignModel.Objects.GetByName(null, null, namePart);
                foreach (global::Artech.Architecture.Common.Objects.KBObject obj in results)
                {
                    if (string.Equals(obj.Name, namePart, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug($"FindObject '{namePart}' SUCCESS in {sw.ElapsedMilliseconds}ms");
                        return obj;
                    }
                }
            } catch (Exception ex) { Logger.Error("FindObject Fatal: " + ex.Message); }
            return null;
        }

        public string ReadObjectSource(string target, string partName)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var obj = FindObject(target);
                if (obj == null) return "{\"error\": \"Object not found\"}";

                Guid partGuid = MapLogicalPartToGuid(obj.TypeDescriptor.Name, partName);
                
                global::Artech.Architecture.Common.Objects.KBObjectPart part = null;
                // SPEED: Manual loop on Parts collection (already loaded by FindObject)
                foreach (global::Artech.Architecture.Common.Objects.KBObjectPart p in obj.Parts)
                {
                    if (p.Type == partGuid) { part = p; break; }
                }

                if (part == null) return "{\"error\": \"Part '" + partName + "' not found.\"}";

                var sourcePart = part as global::Artech.Architecture.Common.Objects.ISource;
                if (sourcePart == null) return "{\"error\": \"Part has no source content\"}";

                string content = sourcePart.Source;
                Logger.Info($"ReadSource SUCCESS in {sw.ElapsedMilliseconds}ms");
                
                return new JObject { ["source"] = content }.ToString();
            }
            catch (Exception ex) { 
                return "{\"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}"; 
            }
        }

        private Guid MapLogicalPartToGuid(string objType, string logicalPart)
        {
            string lp = logicalPart.ToLower();
            if (lp == "source" || lp == "code") return (objType == "Procedure") ? PART_PROCEDURE : PART_EVENTS;
            if (lp == "rules") return PART_RULES;
            if (lp == "events") return PART_EVENTS;
            if (lp == "variables") return PART_VARIABLES;
            return PART_PROCEDURE;
        }

        public string ReadObject(string target)
        {
            var obj = FindObject(target);
            if (obj == null) return "{\"error\": \"Object not found\"}";
            return new JObject { ["name"] = obj.Name, ["type"] = obj.TypeDescriptor.Name }.ToString();
        }

        public string GetObjectXml(string target)
        {
            var obj = FindObject(target);
            if (obj == null) return "{\"error\": \"Object not found\"}";
            return new JObject { ["xml"] = obj.SerializeToXml() }.ToString();
        }
    }
}
