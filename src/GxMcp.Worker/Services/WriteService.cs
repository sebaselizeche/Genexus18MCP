using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class WriteService
    {
        private readonly ObjectService _objectService;

        // GUIDs
        private static readonly Guid PART_PROCEDURE = new Guid("528d1c06-a9c2-420d-bd35-21dca83f12ff");
        private static readonly Guid PART_RULES     = new Guid("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
        private static readonly Guid PART_EVENTS    = new Guid("c44bd5ff-f918-415b-98e6-aca44fed84fa");

        public WriteService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string WriteObject(string target, string partName, string code)
        {
            try
            {
                var obj = _objectService.FindObject(target);
                if (obj == null) return "{\"error\": \"Object not found\"}";

                Guid partGuid = MapLogicalPartToGuid(obj.TypeDescriptor.Name, partName);
                
                global::Artech.Architecture.Common.Objects.KBObjectPart part = null;
                foreach (global::Artech.Architecture.Common.Objects.KBObjectPart p in obj.Parts)
                {
                    if (p.Type == partGuid) { part = p; break; }
                }

                if (part == null) return "{\"error\": \"Part '" + partName + "' not found in this object.\"}";

                var sourcePart = part as global::Artech.Architecture.Common.Objects.ISource;
                if (sourcePart == null) return "{\"error\": \"Part is not a source part\"}";

                sourcePart.Source = code;
                obj.Save();
                
                Logger.Info($"Object {obj.Name} saved successfully.");
                return "{\"status\": \"Success\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private Guid MapLogicalPartToGuid(string objType, string logicalPart)
        {
            string lp = logicalPart.ToLower();
            if (lp == "source" || lp == "code")
            {
                if (objType == "Procedure") return PART_PROCEDURE;
                return PART_EVENTS;
            }
            if (lp == "rules") return PART_RULES;
            if (lp == "events") return PART_EVENTS;
            return PART_PROCEDURE;
        }

        public string WriteSection(string target, string partName, string sectionName, string code)
        {
            return "{\"status\": \"Not implemented yet\"}";
        }
    }
}
