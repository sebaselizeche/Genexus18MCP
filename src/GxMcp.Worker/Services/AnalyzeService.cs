using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class AnalyzeService
    {
        private readonly KbService _kbService;
        private readonly ObjectService _objectService;

        public AnalyzeService(KbService kbService, ObjectService objectService)
        {
            _kbService = kbService;
            _objectService = objectService;
        }

        public string Analyze(string target)
        {
            try
            {
                var kb = _kbService.GetKB();
                if (target == null) return "{\"status\":\"KB analysis not implemented for all objects yet\"}";
                
                var obj = _objectService.FindObject(target);
                if (obj == null) return "{\"error\":\"Object not found\"}";

                var result = new JObject();
                result["name"] = obj.Name;
                result["type"] = obj.TypeDescriptor.Name;
                
                // Add dependencies (calls)
                var calls = new JArray();
                foreach (var reference in obj.GetReferences())
                {
                    var targetObj = kb.DesignModel.Objects.Get(reference.To);
                    if (targetObj != null) calls.Add(targetObj.Name);
                }
                result["calls"] = calls;

                return result.ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        public string GetAttributeMetadata(string name)
        {
            try
            {
                var kb = _kbService.GetKB();
                foreach (var obj in kb.DesignModel.Objects.GetByName(null, null, name))
                {
                    if (string.Equals(obj.TypeDescriptor.Name, "Attribute", StringComparison.OrdinalIgnoreCase))
                    {
                        var json = new JObject();
                        json["name"] = obj.Name;
                        json["guid"] = obj.Guid.ToString();
                        return json.ToString();
                    }
                }
                return "{\"error\":\"Attribute not found\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        public string GetHierarchy(string name) { return "{\"status\":\"Not implemented\"}"; }
        public string GetVariables(string name) { return "{\"status\":\"Not implemented\"}"; }
        public string ListSections(string target, string partName)
        {
            return "{\"status\": \"ListSections not implemented yet\"}";
        }
    }
}
