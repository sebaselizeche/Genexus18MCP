using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class KbService
    {
        private readonly BuildService _buildService;
        private readonly IndexCacheService _indexCacheService;

        // Progress Tracking
        private static int _processedCount = 0;
        private static int _totalCount = 0;
        private static bool _isIndexing = false;
        private static string _currentStatus = "";

        private static dynamic _kb;
        private static readonly object _kbLock = new object();

        public KbService(BuildService buildService, IndexCacheService indexCacheService)
        {
            _buildService = buildService;
            _indexCacheService = indexCacheService;
        }

        public IndexCacheService GetIndexCache()
        {
            return _indexCacheService;
        }

        public dynamic GetKB()
        {
            lock (_kbLock) 
            { 
                if (_kb == null)
                {
                    string kbPath = Environment.GetEnvironmentVariable("GX_KB_PATH");
                    if (!string.IsNullOrEmpty(kbPath))
                    {
                        try 
                        { 
                            Logger.Info($"Auto-opening KB from environment: {kbPath}");
                            OpenKB(kbPath); 
                        } 
                        catch (Exception ex) 
                        { 
                            Logger.Error($"Auto-open failed: {ex.Message}");
                        }
                    }
                    else
                    {
                        Logger.Warn("GX_KB_PATH environment variable is empty.");
                    }
                }
                return _kb; 
            }
        }

        public void OpenKB(string path)
        {
            lock (_kbLock)
            {
                if (_kb != null)
                {
                    try { if (string.Equals(_kb.Location, path, StringComparison.OrdinalIgnoreCase)) return; } catch { }
                    try { _kb.Close(); } catch { }
                }

                try {
                    Logger.Info($"Opening KB: {path}");
                    string oldDir = Directory.GetCurrentDirectory();
                    try {
                        string kbDir = Path.GetDirectoryName(path);
                        Directory.SetCurrentDirectory(kbDir);
                        
                        // Use direct call with OpenOptions for stability
                        var options = new global::Artech.Architecture.Common.Objects.KnowledgeBase.OpenOptions(path);
                        
                        _kb = global::Artech.Architecture.Common.Objects.KnowledgeBase.Open(options);
                        
                        Logger.Info($"KB opened successfully. DesignModel: {_kb.DesignModel.Name}");
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
            Logger.Info("BulkIndex() requested.");
            if (_isIndexing) return "{\"status\":\"Already in progress\"}";

            System.Threading.Tasks.Task.Run(() => {
                try
                {
                    dynamic kb = GetKB();
                    if (kb == null) { _isIndexing = false; return; }
                    
                    Logger.Info("Bulk Indexing starting...");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    
                    _isIndexing = true;
                    _processedCount = 0;
                    _currentStatus = "Gathering objects...";
                    string shadowPath = Environment.GetEnvironmentVariable("GX_SHADOW_PATH");
                    if (!string.IsNullOrEmpty(shadowPath)) {
                        if (!Directory.Exists(shadowPath)) Directory.CreateDirectory(shadowPath);
                        File.WriteAllText(Path.Combine(shadowPath, "sync_test.txt"), "Shadow Sync Active at " + DateTime.Now.ToString());
                    }

                    var objects = new List<global::Artech.Architecture.Common.Objects.KBObject>();
                    try {
                        foreach (global::Artech.Architecture.Common.Objects.KBObject obj in kb.DesignModel.Objects.GetAll()) {
                            objects.Add(obj);
                        }
                        Logger.Info($"Gathering complete: {objects.Count} objects found.");
                    } catch (Exception ex) { Logger.Error("Gather error: " + ex.Message); }

                    _totalCount = objects.Count;
                    int processed = 0;

                    var index = new SearchIndex { LastUpdated = DateTime.Now };

                    _currentStatus = "Processing objects...";
                    foreach (dynamic obj in objects) {
                        try {
                            string objType = obj.TypeDescriptor.Name;
                            string objName = obj.Name;

                            var entry = new SearchIndex.IndexEntry {
                                Name = objName,
                                Type = objType,
                                Description = obj.Description
                            };

                            // Fast Path: Parent & Module
                            try {
                                if (obj.Parent != null && obj.Parent.Guid != obj.Guid)
                                    entry.Parent = (obj.Parent.TypeDescriptor.Name == "DesignModel") ? "Root Module" : obj.Parent.Name;
                                if (obj.Module != null && obj.Module.Guid != obj.Guid)
                                    entry.Module = obj.Module.Name;
                            } catch { }

                            // Deep Indexing ONLY for Core Types (Speedup: skip for Folders, Categories, etc.)
                            bool isCoreType = objType == "Procedure" || objType == "WebPanel" || objType == "Transaction" || objType == "DataProvider" || objType == "SDT" || objType == "Attribute" || objType == "Table";

                            if (isCoreType) {
                                try {
                                    if (objType == "Attribute") {
                                        entry.DataType = obj.Type.ToString();
                                        entry.Length = obj.Length;
                                        entry.Decimals = obj.Decimals;
                                    } else if (objType == "Table") {
                                        entry.RootTable = objName;
                                    }

                                    // Extract Rules & Snippets (only for logic)
                                    if (objType == "Procedure" || objType == "WebPanel" || objType == "DataProvider")
                                    {
                                        var rulesPart = obj.Parts.Get(Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534"));
                                        if (rulesPart != null) {
                                            string rSrc = rulesPart.Source;
                                            var parmMatch = System.Text.RegularExpressions.Regex.Match(rSrc, @"(?i)\bparm\s*\(.*?\)\s*;", System.Text.RegularExpressions.RegexOptions.Singleline);
                                            if (parmMatch.Success) entry.ParmRule = parmMatch.Value.Trim();
                                        }

                                        var sourcePart = obj.Parts.Get(Guid.Parse("c5f0ef88-9ef8-4218-bf76-915024b3c48f"));
                                        if (sourcePart != null) {
                                            string sSrc = sourcePart.Source;
                                            if (!string.IsNullOrEmpty(sSrc)) {
                                                entry.SourceSnippet = string.Join("\n", sSrc.Split('\n').Take(5)).Trim();
                                            }
                                        }
                                    }
                                } catch { }

                                // SHADOW SYNC (Physical Dump) - Only for Core logic
                                try {
                                    if (!string.IsNullOrEmpty(shadowPath) && (objType == "Procedure" || objType == "WebPanel" || objType == "Transaction" || objType == "DataProvider"))
                                    {
                                        string typeDir = Path.Combine(shadowPath, objType);
                                        if (!Directory.Exists(typeDir)) Directory.CreateDirectory(typeDir);

                                        foreach (global::Artech.Architecture.Common.Objects.KBObjectPart part in obj.Parts) {
                                            if (part is global::Artech.Architecture.Common.Objects.ISource sourcePart) {
                                                string source = sourcePart.Source;
                                                if (!string.IsNullOrEmpty(source)) {
                                                    string partName = part.TypeDescriptor.Name;
                                                    string fileName = (partName == "Source" || partName == "Events" || partName == "Structure") 
                                                        ? string.Format("{0}.gx", objName) 
                                                        : string.Format("{0}.{1}.gx", objName, partName);
                                                    
                                                    File.WriteAllText(Path.Combine(typeDir, fileName), source);
                                                }
                                            }
                                        }
                                    }
                                } catch (Exception shadowEx) { Logger.Error("Shadow Sync Error: " + shadowEx.Message); }
                            }

                            index.Objects[string.Format("{0}:{1}", entry.Type, entry.Name)] = entry;
                            
                            processed++;
                            _processedCount = processed;
                            if (processed % 1000 == 0) {
                                Logger.Info(string.Format("Progress: {0}/{1} objects indexed...", processed, _totalCount));
                            }
                        } catch { }
                    }

                    _indexCacheService.UpdateIndex(index);
                    sw.Stop();
                    _isIndexing = false;
                    _processedCount = _totalCount;
                    _currentStatus = "Complete";
                    
                    Logger.Info($"Bulk Indexing complete. {_totalCount} objects in {sw.ElapsedMilliseconds}ms.");

                    // TRIGGER BACKGROUND REFERENCE CRAWLING (Incremental)
                    System.Threading.Tasks.Task.Run(() => CrawlReferences(index, objects));
                } catch (Exception ex) { 
                    _isIndexing = false;
                    Logger.Error($"BulkIndex Fatal: {ex.Message}");
                }
            });

            return "{\"status\":\"Started\"}";
        }

        private void CrawlReferences(SearchIndex index, List<global::Artech.Architecture.Common.Objects.KBObject> objects)
        {
            try {
                Logger.Info("Background Reference Crawling started...");
                int crawled = 0;
                foreach (var obj in objects) {
                    try {
                        string objType = obj.TypeDescriptor.Name;
                        if (objType == "Procedure" || objType == "WebPanel" || objType == "Transaction" || objType == "DataProvider" || objType == "SDT") {
                            string key = string.Format("{0}:{1}", objType, obj.Name);
                            if (index.Objects.TryGetValue(key, out var entry)) {
                                entry.Calls.Clear();
                                entry.Tables.Clear();

                                foreach (dynamic reference in obj.GetReferences()) {
                                    try {
                                        dynamic targetKey = reference.To;
                                        string targetName = targetKey.Name;
                                        if (string.IsNullOrEmpty(targetName)) targetName = targetKey.ToString();
                                        if (string.IsNullOrEmpty(targetName)) continue;

                                        string targetType = targetKey.TypeDescriptor.Name;
                                        if (targetType == "Attribute" || targetType == "Table") {
                                            if (!entry.Tables.Contains(targetName)) entry.Tables.Add(targetName);
                                        } else {
                                            if (!entry.Calls.Contains(targetName)) entry.Calls.Add(targetName);
                                        }
                                    } catch { }
                                }
                                crawled++;
                                
                                // Periodic Save & Log
                                if (crawled % 500 == 0) {
                                    Logger.Debug($"Crawled references for {crawled} objects...");
                                    _indexCacheService.UpdateIndex(index);
                                }
                            }
                        }
                    } catch { }
                }
                _indexCacheService.UpdateIndex(index);
                Logger.Info($"Background Reference Crawling complete. {crawled} objects detailed.");
            } catch (Exception ex) {
                Logger.Error("CrawlReferences Error: " + ex.Message);
            }
        }

        public string GetIndexStatus()
        {
            var json = new Newtonsoft.Json.Linq.JObject();
            json["isIndexing"] = _isIndexing;
            json["total"] = _totalCount;
            json["processed"] = _processedCount;
            json["status"] = _currentStatus;
            
            if (_totalCount > 0)
                json["progress"] = (int)((_processedCount / (float)_totalCount) * 100);
            else
                json["progress"] = 0;

            return json.ToString();
        }
    }
}
