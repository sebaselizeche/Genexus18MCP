using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class SDTService
    {
        private readonly ObjectService _objectService;

        public SDTService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string GetSDTStructure(string sdtName)
        {
            try
            {
                var obj = _objectService.FindObject(sdtName);
                if (obj == null) return "{\"error\": \"SDT not found\"}";

                if (obj.TypeDescriptor.Name.Equals("SDT", StringComparison.OrdinalIgnoreCase))
                {
                    dynamic sdt = obj;
                    var result = new JObject();
                    result["name"] = sdt.Name;
                    result["type"] = "SDT";
                    result["isCollection"] = sdt.IsCollection;
                    
                    var children = new JArray();
                    // Part GUID for SDT Structure is 8597371d-1941-4c12-9c17-48df9911e2f3
                    dynamic structure = sdt.Parts.Get(Guid.Parse("8597371d-1941-4c12-9c17-48df9911e2f3")); 
                    if (structure != null && structure.Root != null)
                    {
                        foreach (dynamic child in structure.Root.Children)
                        {
                            children.Add(MapLevelToResult(child));
                        }
                    }
                    result["children"] = children;
                    return result.ToString();
                }

                return "{\"error\": \"Object is not an SDT\"}";
            }
            catch (Exception ex)
            {
                Logger.Error("SDTService Error: " + ex.Message);
                return "{\"error\": \"" + ex.Message + "\"}";
            }
        }

        private JObject MapLevelToResult(dynamic level)
        {
            var res = new JObject();
            res["name"] = level.Name;
            res["isLevel"] = level.IsCompound;
            res["isCollection"] = level.IsCollection;
            
            if (level.IsCompound)
            {
                var children = new JArray();
                foreach (var child in level.Children)
                {
                    children.Add(MapLevelToResult(child));
                }
                res["children"] = children;
                res["type"] = "Compound";
            }
            else
            {
                res["type"] = level.Type.ToString();
            }
            return res;
        }
    }
}
