using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class ListService
    {
        private readonly KbService _kbService;

        public ListService(KbService kbService)
        {
            _kbService = kbService;
        }

        public string ListObjects(string filter, int limit, int offset)
        {
            try
            {
                var kb = _kbService.GetKB();
                if (kb == null) return "{\"error\":\"KB not open\"}";

                var array = new JArray();
                int count = 0;
                int current = 0;

                if (kb.DesignModel == null) return "{\"error\":\"KB DesignModel is null\"}";
                var objects = kb.DesignModel.Objects;
                if (objects == null) return "{\"error\":\"KB DesignModel.Objects is null\"}";

                // Parse filter: can be a comma-separated list of types or a partial name
                var filterTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string nameFilter = null;

                if (!string.IsNullOrEmpty(filter))
                {
                    if (filter.Contains(","))
                    {
                        foreach (var t in filter.Split(',')) filterTypes.Add(t.Trim());
                    }
                    else if (IsLikelyType(filter))
                    {
                        filterTypes.Add(filter.Trim());
                    }
                    else
                    {
                        nameFilter = filter.Trim();
                    }
                }

                foreach (var obj in objects.GetAll())
                {
                    // Filter logic
                    if (filterTypes.Count > 0 && !filterTypes.Contains(obj.TypeDescriptor.Name)) continue;
                    if (!string.IsNullOrEmpty(nameFilter) && !obj.Name.Contains(nameFilter)) continue;
                    
                    if (current >= offset)
                    {
                        var item = new JObject();
                        item["name"] = obj.Name;
                        item["type"] = obj.TypeDescriptor?.Name ?? "Unknown";
                        item["description"] = obj.Description;
                        array.Add(item);
                        count++;
                        if (count >= limit) break;
                    }
                    current++;
                }

                return array.ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private bool IsLikelyType(string s)
        {
            var types = new[] { "Procedure", "Transaction", "WebPanel", "Attribute", "Table", "DataView", "Domain", "WorkPanel", "ExternalObject", "Menu", "SDPanel" };
            return types.Any(t => string.Equals(t, s, StringComparison.OrdinalIgnoreCase));
        }
    }
}
