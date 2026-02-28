using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services.Structure
{
    public class VisualStructureService
    {
        private readonly ObjectService _objectService;
        private readonly HashSet<KBObject> _modifiedObjects = new HashSet<KBObject>();

        public VisualStructureService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public JArray SerializeVisualLevel(TransactionLevel level)
        {
            var children = new JArray();
            if (level == null) return children;

            try {
                Logger.Info($"[VisualStructureService] Serializing level: {level.Name}");
                
                var attributes = level.Attributes;
                if (attributes != null) { 
                    Logger.Info($"[VisualStructureService] Processing {attributes.Count} attributes in {level.Name}");
                    foreach (TransactionAttribute attr in attributes) {
                        try {
                            children.Add(VisualStructureMapper.MapAttribute(attr));
                        } catch (Exception ex) {
                            Logger.Error($"[VisualStructureService] Error mapping attribute in {level.Name}: {ex.Message}");
                        }
                    }
                }

                var subLevels = level.Levels;
                if (subLevels != null)
                {
                    Logger.Info($"[VisualStructureService] Processing {subLevels.Count} sub-levels in {level.Name}");
                    foreach (TransactionLevel subLevel in subLevels) {
                        try {
                            var levelItem = new JObject { 
                                ["name"] = subLevel.Name, 
                                ["isLevel"] = true, 
                                ["children"] = SerializeVisualLevel(subLevel) 
                            };
                            children.Add(levelItem);
                        } catch (Exception ex) {
                            Logger.Error($"[VisualStructureService] Error mapping sub-level {subLevel.Name} in {level.Name}: {ex.Message}");
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"[VisualStructureService] Fatal error serializing level: {ex.Message}");
            }
            return children;
        }

        public void SyncVisualStructure(Transaction trn, JArray visualItems)
        {
            _modifiedObjects.Clear();
            SyncVisualLevel(trn.Structure.Root, visualItems);
            
            // Batch Save: Salva cada objeto modificado apenas uma vez
            foreach (var obj in _modifiedObjects) {
                try { obj.Save(); } catch (Exception ex) { Logger.Error($"Failed to save modified object {obj.Name}: {ex.Message}"); }
            }
        }

        private void SyncVisualLevel(TransactionLevel sdkLevel, JArray visualItems)
        {
            var visualNames = new HashSet<string>(visualItems.Select(v => v["name"]?.ToString()), StringComparer.OrdinalIgnoreCase);
            
            if (sdkLevel.Attributes != null)
            {
                var toRemove = new List<dynamic>();
                foreach (dynamic attr in sdkLevel.Attributes) { if (!visualNames.Contains(attr.Name)) toRemove.Add(attr); }
                foreach (dynamic dead in toRemove) { try { sdkLevel.Attributes.Remove(dead); } catch { } }
            }
            if (sdkLevel.Levels != null)
            {
                var toRemove = new List<dynamic>();
                foreach (dynamic lvl in sdkLevel.Levels) { if (!visualNames.Contains(lvl.Name)) toRemove.Add(lvl); }
                foreach (dynamic dead in toRemove) { try { sdkLevel.Levels.Remove(dead); } catch { } }
            }

            foreach (var vItem in visualItems)
            {
                string name = vItem["name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                bool isLevel = (bool?)vItem["isLevel"] ?? false;

                if (isLevel) {
                    TransactionLevel targetLevel = null;
                    if (sdkLevel.Levels != null) { 
                        foreach (dynamic lvl in sdkLevel.Levels) { if (lvl.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { targetLevel = lvl; break; } } 
                    }
                    if (targetLevel == null) { 
                        targetLevel = new TransactionLevel(sdkLevel.Structure); 
                        targetLevel.Name = name; 
                        sdkLevel.Levels.Add(targetLevel); 
                    }
                    var children = vItem["children"] as JArray;
                    if (children != null) SyncVisualLevel(targetLevel, children);
                } else {
                    TransactionAttribute targetAttr = null;
                    if (sdkLevel.Attributes != null) { 
                        foreach (dynamic attr in sdkLevel.Attributes) { if (attr.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { targetAttr = attr; break; } } 
                    }
                    if (targetAttr == null) {
                        var attrObj = _objectService.FindObject(name) as Artech.Genexus.Common.Objects.Attribute;
                        if (attrObj != null) { 
                            targetAttr = new TransactionAttribute(sdkLevel.Structure, attrObj); 
                            targetAttr.Name = name; 
                            sdkLevel.Attributes.Add(targetAttr); 
                        }
                        else continue;
                    }
                    targetAttr.IsKey = (bool?)vItem["isKey"] ?? false;
                    
                    // Passamos o HashSet para coletar modificações globais no Atributo
                    VisualStructureMapper.SyncAttributeProperties(targetAttr, vItem, _modifiedObjects);
                }
            }
        }
    }
}
