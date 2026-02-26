using System;
using Newtonsoft.Json.Linq;
using Artech.Genexus.Common.Objects;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Services.Structure;

namespace GxMcp.Worker.Services
{
    public class StructureService
    {
        private readonly ObjectService _objectService;
        private readonly VisualStructureService _visualStructureService;
        private readonly IndexService _indexService;

        public StructureService(ObjectService objectService)
        {
            _objectService = objectService;
            _visualStructureService = new VisualStructureService(objectService);
            _indexService = new IndexService(objectService);
        }

        public string UpdateVisualStructure(string targetName, string payload)
        {
            try {
                var obj = _objectService.FindObject(targetName);
                if (obj == null) return "{\"error\": \"Object not found\"}";
                var trn = obj as Transaction;
                if (trn == null) return "{\"error\": \"Object is not a Transaction\"}";

                using (var sdkTrans = trn.Model.KB.BeginTransaction()) {
                    try {
                        var json = JObject.Parse(payload);
                        var children = json["children"] as JArray;
                        if (children == null) return "{\"error\": \"Invalid payload\"}";
                        
                        // Chamada otimizada com Batch Save interno
                        _visualStructureService.SyncVisualStructure(trn, children);
                        
                        trn.EnsureSave();
                        sdkTrans.Commit();
                        
                        _objectService.GetKbService().GetIndexCache().UpdateEntry(trn);
                        return "{\"status\": \"Success\"}";
                    } catch (Exception ex) {
                        sdkTrans.Rollback();
                        return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
                    }
                }
            } catch (Exception ex) { return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}"; }
        }

        public string GetVisualStructure(string targetName)
        {
            try {
                var obj = _objectService.FindObject(targetName);
                if (obj == null) return "{\"error\": \"Object not found\"}";
                
                var result = new JObject { ["name"] = obj.Name, ["type"] = obj.TypeDescriptor.Name, ["description"] = obj.Description };
                if (obj is Transaction trn) result["children"] = _visualStructureService.SerializeVisualLevel(trn.Structure.Root);
                else if (obj is Table tbl) result["children"] = SerializeTableStructure(tbl);
                else return "{\"error\": \"Invalid object type\"}";
                
                return result.ToString();
            } catch (Exception ex) { return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}"; }
        }

        public string GetVisualIndexes(string targetName) => _indexService.GetVisualIndexes(targetName);

        private JArray SerializeTableStructure(Table tbl)
        {
            var children = new JArray();
            dynamic dStructure = ((dynamic)tbl).TableStructure;
            if (dStructure != null && dStructure.Attributes != null) {
                foreach (dynamic attr in dStructure.Attributes) children.Add(VisualStructureMapper.MapAttribute(attr));
            }
            return children;
        }
    }
}
