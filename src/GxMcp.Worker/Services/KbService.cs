using System;
using System.IO;
using System.Collections.Generic;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using System.Reflection;
using System.Linq;

namespace GxMcp.Worker.Services
{
    public class KbService
    {
        private static global::Artech.Architecture.Common.Objects.KnowledgeBase _kb;
        private static readonly object _kbLock = new object();
        private readonly BuildService _buildService;
        private readonly IndexCacheService _indexCacheService;

        public KbService(BuildService buildService, IndexCacheService indexCacheService)
        {
            _buildService = buildService;
            _indexCacheService = indexCacheService;
        }

        public global::Artech.Architecture.Common.Objects.KnowledgeBase GetKB() 
        { 
            if (_kb != null) return _kb;
            EnsureKbOpen(); 
            return _kb; 
        }

        private void EnsureKbOpen()
        {
            if (_kb != null) return;
            lock (_kbLock)
            {
                if (_kb != null) return;
                
                string gxPath = Environment.GetEnvironmentVariable("GX_PROGRAM_DIR") ?? @"C:\Program Files (x86)\GeneXus\GeneXus18";
                try {
                    string kbPath = _buildService.GetKBPath();
                    if (kbPath.EndsWith(".gxw", StringComparison.OrdinalIgnoreCase)) kbPath = Path.GetDirectoryName(kbPath);

                    string oldDir = Directory.GetCurrentDirectory();
                    try {
                        Directory.SetCurrentDirectory(gxPath);
                        Logger.Info($"Opening KB in OFFLINE mode: {kbPath}");

                        var options = new global::Artech.Architecture.Common.Objects.KnowledgeBase.OpenOptions(kbPath);
                        options.EnableMultiUser = true;
                        options.AvoidIndexing = true;
                        
                        // GX18 Hardening: Try to disable all background checks
                        try {
                            var props = options.GetType().GetProperties();
                            foreach(var p in props) {
                                if (p.Name == "NoAutomaticUpdate" || p.Name == "OfflineMode") {
                                    p.SetValue(options, true);
                                    Logger.Debug($"Option {p.Name} set to true.");
                                }
                            }
                        } catch { }

                        _kb = global::Artech.Architecture.Common.Objects.KnowledgeBase.Open(options);
                        if (_kb == null) throw new Exception("KnowledgeBase.Open returned null");
                        
                        Logger.Info($"KB opened successfully. DesignModel: {_kb.DesignModel?.Name}");
                    } finally { Directory.SetCurrentDirectory(oldDir); }
                } catch (Exception ex) { 
                    Logger.Error($"ERROR opening KB: {ex.Message}");
                    _kb = null;
                    throw;
                }
            }
        }

        public string BulkIndex()
        {
            try
            {
                var kb = GetKB();
                if (kb == null) return "{\"error\":\"KB not open\"}";
                Logger.Info("Bulk Indexing...");
                var index = new SearchIndex { LastUpdated = DateTime.Now };
                var objects = kb.DesignModel.Objects.GetAll();
                int total = 0;
                foreach (var obj in objects)
                {
                    index.Objects[$"{obj.TypeDescriptor.Name}:{obj.Name}"] = new SearchIndex.IndexEntry {
                        Name = obj.Name, Type = obj.TypeDescriptor.Name, Description = obj.Description
                    };
                    total++;
                }
                _indexCacheService.UpdateIndex(index);
                return "{\"status\":\"Success\", \"totalIndexed\":" + total + "}";
            } catch (Exception ex) { return "{\"error\":\"" + ex.Message + "\"}"; }
        }
    }
}
