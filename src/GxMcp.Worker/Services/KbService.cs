using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using Artech.Architecture.Common.Objects;

namespace GxMcp.Worker.Services
{
    public class KbService
    {
        private BuildService _buildService;
        private readonly IndexCacheService _indexCacheService;
        private readonly VectorService _vectorService = new VectorService();

        // Progress Tracking
        private static int _processedCount = 0;
        private static int _totalCount = 0;
        private static bool _isIndexing = false;
        private static string _currentStatus = "";

        private static dynamic _kb;
        private static bool _isOpenInProgress = false;
        private static readonly object _kbLock = new object();

        public KbService(IndexCacheService indexCacheService)
        {
            _indexCacheService = indexCacheService;
        }

        public void SetBuildService(BuildService bs) { _buildService = bs; }
        public IndexCacheService GetIndexCache() { return _indexCacheService; }
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

        public string OpenKB(string path)
        {
            lock (_kbLock)
            {
                if (_isOpenInProgress) return "{\"status\":\"In Progress\"}";
                _isOpenInProgress = true;
                
                if (_kb != null)
                {
                    try { if (string.Equals(_kb.Location, path, StringComparison.OrdinalIgnoreCase)) { _isOpenInProgress = false; return "{\"status\":\"Success\"}"; } } catch { }
                    try { _kb.Close(); } catch { }
                }

                try {
                    Logger.Info($"Opening KB: {path}");
                    string oldDir = Directory.GetCurrentDirectory();
                    try {
                        string kbDir = Path.GetDirectoryName(path);
                        Directory.SetCurrentDirectory(kbDir);
                        
                        var options = new KnowledgeBase.OpenOptions(path);
                        _kb = KnowledgeBase.Open(options);
                        
                        Logger.Info($"KB opened successfully.");
                        return "{\"status\":\"Success\"}";
                    } finally { Directory.SetCurrentDirectory(oldDir); _isOpenInProgress = false; }
                } catch (Exception ex) { 
                    Logger.Error($"ERROR opening KB: {ex.Message}");
                    _kb = null;
                    _isOpenInProgress = false;
                    return "{\"error\":\"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
                }
            }
        }

        public string BulkIndex()
        {
            Logger.Info("BulkIndex() requested.");
            if (_isIndexing) return "{\"status\":\"Already in progress\"}";

            _isIndexing = true;
            _processedCount = 0;
            _totalCount = 0;
            _currentStatus = "Scanning objects...";

            Program.BackgroundQueue.Enqueue(() => {
                try {
                    dynamic kb = GetKB();
                    if (kb == null) { _isIndexing = false; return; }
                    
                    var allObjects = kb.DesignModel.Objects.GetAll();
                    // Ensure we cast to IEnumerable if it's dynamic to use Linq
                    var objectList = ((System.Collections.IEnumerable)allObjects).Cast<global::Artech.Architecture.Common.Objects.KBObject>();
                    
                    _totalCount = objectList.Count();
                    _currentStatus = $"Indexing {_totalCount} objects...";
                    Logger.Info(_currentStatus);

                    foreach (var obj in objectList)
                    {
                        try {
                            _indexCacheService.UpdateEntry(obj);
                            _processedCount++;
                            if (_processedCount % 100 == 0) {
                                _currentStatus = $"Processed {_processedCount}/{_totalCount}";
                                Logger.Info(_currentStatus);
                            }
                        } catch (Exception ex) {
                            Logger.Error($"Error indexing object {obj.Name}: {ex.Message}");
                        }
                    }

                    _currentStatus = "Complete";
                    _isIndexing = false;
                    Logger.Info("BulkIndex completed successfully.");
                } catch (Exception ex) {
                    Logger.Error("BulkIndex FATAL: " + ex.Message);
                    _isIndexing = false;
                    _currentStatus = "Error: " + ex.Message;
                }
            });

            return "{\"status\":\"Started\"}";
        }

        public string GetIndexStatus()
        {
            var json = new Newtonsoft.Json.Linq.JObject();
            json["isIndexing"] = _isIndexing;
            json["total"] = _totalCount;
            json["processed"] = _processedCount;
            json["status"] = _currentStatus;
            return json.ToString();
        }
    }
}
