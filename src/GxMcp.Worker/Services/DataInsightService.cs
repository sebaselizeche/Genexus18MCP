using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class DataInsightService
    {
        private readonly KbService _kbService;
        private readonly ObjectService _objectService;
        private readonly NavigationService _navigationService;
        private readonly PatternAnalysisService _patternAnalysisService;

        public DataInsightService(KbService kbService, ObjectService objectService, NavigationService navigationService, PatternAnalysisService patternAnalysisService)
        {
            _kbService = kbService;
            _objectService = objectService;
            _navigationService = navigationService;
            _patternAnalysisService = patternAnalysisService;
        }

        public string GetDataContext(string target)
        {
            try
            {
                var obj = _objectService.FindObject(target);
                if (obj == null) return "{\"error\": \"Object not found\"}";

                var result = new JObject();
                result["objectName"] = obj.Name;
                result["objectType"] = obj.TypeDescriptor.Name;

                // 1. Get Tables used via Navigation Report
                var tableSchemas = new JObject();
                string navJson = _navigationService.GetNavigation(target);
                if (!navJson.Contains("\"error\""))
                {
                    var nav = JObject.Parse(navJson);
                    var levels = nav["levels"] as JArray;
                    if (levels != null)
                    {
                        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var lvl in levels)
                        {
                            string tblName = lvl["baseTable"]?.ToString();
                            if (!string.IsNullOrEmpty(tblName)) tableNames.Add(tblName);
                        }

                        foreach (var tblName in tableNames)
                        {
                            var tbl = _objectService.FindObject(tblName) as Table;
                            if (tbl != null) tableSchemas[tblName] = GetTableStructure(tbl);
                        }
                    }
                }
                result["dataSchema"] = tableSchemas;

                // 2. Variables & Local Context
                result["variables"] = GetVariables(obj);

                // 3. Pattern Context (WWP)
                if (obj is WebPanel || obj is Transaction)
                {
                    string patternMeta = _patternAnalysisService.GetWWPStructure(target);
                    if (!patternMeta.Contains("\"error\""))
                    {
                        result["patternMetadata"] = JObject.Parse(patternMeta);
                    }
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private JObject GetTableStructure(Table tbl)
        {
            var res = new JObject();
            res["description"] = tbl.Description;
            
            var columns = new JArray();
            foreach (var attr in tbl.TableStructure.Attributes)
            {
                var col = new JObject();
                col["name"] = attr.Name;
                col["isKey"] = attr.IsKey;
                col["type"] = attr.Attribute.Type.ToString();
                col["length"] = attr.Attribute.Length;
                col["decimals"] = attr.Attribute.Decimals;
                col["isNullable"] = attr.IsNullable.ToString();
                columns.Add(col);
            }
            res["columns"] = columns;
            return res;
        }

        private JArray GetVariables(KBObject obj)
        {
            var vars = new JArray();
            var part = obj.Parts.Get<VariablesPart>();
            if (part != null)
            {
                foreach (var v in part.Variables)
                {
                    var varObj = new JObject();
                    varObj["name"] = v.Name;
                    varObj["type"] = v.Type.ToString();
                    varObj["length"] = v.Length;
                    varObj["decimals"] = v.Decimals;
                    varObj["isCollection"] = v.IsCollection;
                    vars.Add(varObj);
                }
            }
            return vars;
        }
    }
}
