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
        private static bool _isOpenInProgress = false;
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

        public bool IsInitializing => _isOpenInProgress;

        public dynamic GetKB()
        {
            lock (_kbLock) 
            { 
                if (_kb == null && !_isOpenInProgress)
                {
                    string kbPath = Environment.GetEnvironmentVariable("GX_KB_PATH");
                    if (!string.IsNullOrEmpty(kbPath))
                    {
                        Logger.Info($"Auto-opening KB in background: {kbPath}");
                        System.Threading.Tasks.Task.Run(() => OpenKB(kbPath));
                    }
                }
                return _kb; 
            }
        }

        public void OpenKB(string path)
        {
            lock (_kbLock)
            {
                if (_isOpenInProgress) return;
                _isOpenInProgress = true;
                
                if (_kb != null)
                {
                    try { if (string.Equals(_kb.Location, path, StringComparison.OrdinalIgnoreCase)) { _isOpenInProgress = false; return; } } catch { }
                    try { _kb.Close(); } catch { }
                }

                try {
                    Logger.Info($"Opening KB: {path}");
                    string oldDir = Directory.GetCurrentDirectory();
                    try {
                        string kbDir = Path.GetDirectoryName(path);
                        Directory.SetCurrentDirectory(kbDir);
                        
                        var options = new global::Artech.Architecture.Common.Objects.KnowledgeBase.OpenOptions(path);
                        _kb = global::Artech.Architecture.Common.Objects.KnowledgeBase.Open(options);
                        
                        Logger.Info($"KB opened successfully.");
                    } finally { Directory.SetCurrentDirectory(oldDir); _isOpenInProgress = false; }
                } catch (Exception ex) { 
                    Logger.Error($"ERROR opening KB: {ex.Message}");
                    _kb = null;
                    _isOpenInProgress = false;
                    throw;
                }
            }
        }

        public string BulkIndex()
        {
            Logger.Info("BulkIndex() requested.");
            if (_isIndexing) return "{\"status\":\"Already in progress\"}";

            // Use the BackgroundQueue to ensure STA safety and allow interactivity
            Program.BackgroundQueue.Enqueue(() => {
                try {
                    dynamic kb = GetKB();
                    if (kb == null) { _isIndexing = false; return; }
                    
                    Logger.Info("Bulk Indexing starting (STA-Safe)...");
                    _isIndexing = true;
                    _processedCount = 0;
                    _currentStatus = "Gathering objects...";

                    var objects = new List<global::Artech.Architecture.Common.Objects.KBObject>();
                    foreach (global::Artech.Architecture.Common.Objects.KBObject obj in kb.DesignModel.Objects.GetAll()) {
                        objects.Add(obj);
                    }
                    _totalCount = objects.Count;
                    
                    var index = new SearchIndex { LastUpdated = DateTime.Now };
                    _currentStatus = "Processing...";

                    // Start the chunked indexing process
                    ProcessIndexChunk(index, objects, 0);
                } catch (Exception ex) {
                    _isIndexing = false;
                    Logger.Error("BulkIndex Initialization Fatal: " + ex.Message);
                }
            });

            return "{\"status\":\"Started\"}";
        }

        private void ProcessIndexChunk(SearchIndex index, List<global::Artech.Architecture.Common.Objects.KBObject> objects, int startIndex)
        {
            const int chunkSize = 50;
            int endIndex = Math.Min(startIndex + chunkSize, objects.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var obj = objects[i];
                try {
                    // (Indexing logic from original BulkIndex...)
                    string objType = obj.TypeDescriptor.Name;
                    string objName = obj.Name;

                    var entry = new SearchIndex.IndexEntry {
                        Name = objName,
                        Type = objType,
                        Description = obj.Description
                    };

                    try {
                        if (obj.Parent != null && obj.Parent.Guid != obj.Guid)
                            entry.Parent = (obj.Parent.TypeDescriptor.Name == "DesignModel") ? "Root Module" : obj.Parent.Name;
                        if (obj.Module != null && obj.Module.Guid != obj.Guid)
                            entry.Module = obj.Module.Name;
                    } catch { }

                    bool isCoreType = objType == "Procedure" || objType == "WebPanel" || objType == "Transaction" || objType == "DataProvider" || objType == "SDT" || objType == "Attribute" || objType == "Table";
                    if (isCoreType) {
                        // Deep Indexing (DataType, Parm, etc.)
                        try {
                            if (objType == "Attribute" && obj is global::Artech.Genexus.Common.Objects.Attribute attr) {
                                entry.DataType = attr.Type.ToString();
                                entry.Length = attr.Length;
                                entry.Decimals = attr.Decimals;
                            } else if (objType == "Table") {
                                entry.RootTable = objName;
                            }

                            if (objType == "Procedure" || objType == "WebPanel" || objType == "DataProvider") {
                                var rulesPart = obj.Parts.Get(Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534"));
                                if (rulesPart != null) {
                                    string rSrc = rulesPart.Source;
                                    var parmMatch = System.Text.RegularExpressions.Regex.Match(rSrc, @"(?i)\bparm\s*\(.*?\)\s*;", System.Text.RegularExpressions.RegexOptions.Singleline);
                                    if (parmMatch.Success) entry.ParmRule = parmMatch.Value.Trim();
                                }
                                var sourcePart = obj.Parts.Get(Guid.Parse("c5f0ef88-9ef8-4218-bf76-915024b3c48f"));
                                if (sourcePart != null) {
                                    string sSrc = sourcePart.Source;
                                    if (!string.IsNullOrEmpty(sSrc)) 
                                        entry.SourceSnippet = string.Join("\n", sSrc.Split('\n').Take(5)).Trim();
                                }
                            }
                        } catch { }
                    }

                    index.Objects[string.Format("{0}:{1}", entry.Type, entry.Name)] = entry;
                } catch { }
            }

            _processedCount = endIndex;
            if (endIndex % 1000 == 0 || endIndex == objects.Count) {
                Logger.Info(string.Format("Indexing Progress: {0}/{1}", endIndex, objects.Count));
            }

            if (endIndex < objects.Count) {
                // Yield to main loop by enqueuing the next chunk
                Program.BackgroundQueue.Enqueue(() => ProcessIndexChunk(index, objects, endIndex));
            } else {
                // Finalize
                _indexCacheService.UpdateIndex(index);
                _isIndexing = false;
                _currentStatus = "Complete";
                Logger.Info("Bulk Indexing complete (Responsive).");
                
                // Trigger Crawling (also chunked)
                Program.BackgroundQueue.Enqueue(() => ProcessCrawlChunk(index, objects, 0));
            }
        }

        private void ProcessCrawlChunk(SearchIndex index, List<global::Artech.Architecture.Common.Objects.KBObject> objects, int startIndex)
        {
            const int chunkSize = 20;
            int endIndex = Math.Min(startIndex + chunkSize, objects.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var obj = objects[i];
                try {
                    string objType = obj.TypeDescriptor.Name;
                    if (objType == "Procedure" || objType == "WebPanel" || objType == "Transaction" || objType == "DataProvider" || objType == "SDT") {
                        string key = string.Format("{0}:{1}", objType, obj.Name);
                        if (index.Objects.TryGetValue(key, out var entry)) {
                            entry.Calls.Clear(); entry.Tables.Clear();
                            foreach (dynamic reference in obj.GetReferences()) {
                                try {
                                    dynamic targetKey = reference.To;
                                    string targetName = targetKey.Name;
                                    if (string.IsNullOrEmpty(targetName)) targetName = targetKey.ToString();
                                    string targetType = targetKey.TypeDescriptor.Name;
                                    if (targetType == "Attribute" || targetType == "Table") {
                                        if (!entry.Tables.Contains(targetName)) entry.Tables.Add(targetName);
                                    } else {
                                        if (!entry.Calls.Contains(targetName)) entry.Calls.Add(targetName);
                                    }
                                } catch { }
                            }
                        }
                    }
                } catch { }
            }

            if (endIndex < objects.Count) {
                Program.BackgroundQueue.Enqueue(() => ProcessCrawlChunk(index, objects, endIndex));
            } else {
                _indexCacheService.UpdateIndex(index);
                Logger.Info("Reference Crawling complete (Responsive).");
            }
        }

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
