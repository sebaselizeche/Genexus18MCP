using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.Common;
using Artech.Architecture.UI.Framework.Services;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class ListService
    {
        private readonly BuildService _buildService;
        private readonly KbService _kbService;
        private readonly IndexCacheService _indexCacheService;

        public ListService(BuildService buildService, KbService kbService, IndexCacheService indexCacheService)
        {
            _buildService = buildService;
            _kbService = kbService;
            _indexCacheService = indexCacheService;
        }

        private KnowledgeBase EnsureKbOpen()
        {
            return _kbService.GetKB();
        }

        public string ListObjects(string filter, int limit = 100, int offset = 0)
        {
            try
            {
                var objects = new List<string>();
                string[] filters = string.IsNullOrEmpty(filter) ? null : filter.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                var index = _indexCacheService.GetIndex();
                if (index != null && index.Objects.Count > 0)
                {
                    // Use memory cache for fast listing
                    foreach (var entry in index.Objects.Values)
                    {
                        bool matchesFilter = (filters == null);
                        if (!matchesFilter)
                        {
                            foreach (string f in filters)
                            {
                                if (entry.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    entry.Type.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    (entry.Description != null && entry.Description.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    matchesFilter = true;
                                    break;
                                }
                            }
                        }

                        if (matchesFilter)
                        {
                            objects.Add($"{GetShorthand(entry.Type)}:{(entry.Name.Contains(":") ? entry.Name.Split(':')[1] : entry.Name)}");
                        }
                    }
                }

                // Fallback to SDK iteration if index is missing or didn't yield results
                if (objects.Count == 0 && filters != null)
                {
                    var kb = EnsureKbOpen();
                    var designModel = kb.DesignModel;
                    if (designModel != null)
                    {
                        foreach (object o in designModel.Objects)
                        {
                            KBObject kbo = o as KBObject;
                            if (kbo != null)
                            {
                                string kbName = kbo.Name;
                                string typeName = kbo.TypeDescriptor.Name;
                                string description = kbo.TypeDescriptor.Description;

                                bool matchesFilter = false;
                                foreach (string f in filters)
                                {
                                    if (kbName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        typeName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        description.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        matchesFilter = true;
                                        break;
                                    }
                                }

                                if (matchesFilter)
                                {
                                    objects.Add($"{GetShorthand(kbo.TypeDescriptor.Name)}:{kbo.Name}");
                                }
                            }
                        }
                    }
                }

                int totalCount = objects.Count;
                var pagedObjects = objects.Distinct().Skip(offset).Take(limit).ToArray();
                var jsonItems = pagedObjects.Select(o => "\"" + CommandDispatcher.EscapeJsonString(o) + "\"");
                
                return "{\"total\": " + totalCount + "," +
                       "\"count\": " + pagedObjects.Length + "," +
                       "\"limit\": " + limit + "," +
                       "\"offset\": " + offset + "," + 
                       "\"objects\": [" + string.Join(",", jsonItems) + "]}";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ListService Error] {ex.Message}");
                return "{\"error\": \"SDK Error: " + CommandDispatcher.EscapeJsonString(ex.Message) + ". Check Worker logs for details.\"}";
            }
        }

        private string GetShorthand(string typeName)
        {
            switch (typeName.ToLower())
            {
                case "procedure": return "Prc";
                case "transaction": return "Trn";
                case "webpanel": return "Wbp";
                case "dataview": return "Dvw";
                case "dataprovider": return "Dpr";
                case "sdpanel": return "Sdp";
                case "menu": return "Mnu";
                case "attribute": return "Att";
                case "table": return "Tbl";
                case "domain": return "Dom";
                case "image": return "Img";
                case "file": return "File";
                case "module": return "Mod";
                default: return typeName.Substring(0, Math.Min(3, typeName.Length));
            }
        }
    }
}
