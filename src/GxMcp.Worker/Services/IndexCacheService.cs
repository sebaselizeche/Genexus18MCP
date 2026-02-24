using System;
using System.IO;
using GxMcp.Worker.Models;
using GxMcp.Worker.Helpers;
using System.Threading.Tasks;
using System.Linq;
using Artech.Architecture.Common.Objects;

namespace GxMcp.Worker.Services
{
    public class IndexCacheService
    {
        private SearchIndex _index;
        private string _indexPath;
        private readonly BuildService _buildService;
        private bool _initialized = false;
        private readonly object _lock = new object();
        private bool _savingInProgress = false;
        private string _lastSavedJsonHash = null;

        public IndexCacheService(BuildService buildService)
        {
            _buildService = buildService;
            _indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "search_index.json");
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            try
            {
                string kbPath = _buildService.GetKBPath();
                if (!string.IsNullOrEmpty(kbPath)) Initialize(kbPath);
            }
            catch { }
        }

        public void Initialize(string kbPath)
        {
            if (string.IsNullOrEmpty(kbPath)) return;
            if (kbPath.EndsWith(".gxw", StringComparison.OrdinalIgnoreCase)) kbPath = Path.GetDirectoryName(kbPath);

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cacheDir = Path.Combine(localAppData, "GxMcp", "Cache");
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                string hash = GetHash(kbPath);
                _indexPath = Path.Combine(cacheDir, string.Format("index_{0}.json", hash));
                _initialized = true;
                Logger.Info(string.Format("IndexCache initialized: {0}", _indexPath));

                // PERFORMANCE: Pro-active loading in background
                Task.Run(() => GetIndex());
            }
            catch (Exception ex) { Logger.Error("IndexCache Init Error: " + ex.Message); }
        }

        private string GetHash(string input)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input.ToLower().Trim()));
                return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 16);
            }
        }

        private void BuildParentIndex(SearchIndex index)
        {
            var byParent = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<SearchIndex.IndexEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in index.Objects.Values)
            {
                AddOrUpdateEntryInParentIndex(byParent, entry);
            }
            index.ChildrenByParent = byParent;
        }

        private void AddOrUpdateEntryInParentIndex(System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<SearchIndex.IndexEntry>> byParent, SearchIndex.IndexEntry entry)
        {
            string parent = entry.Parent ?? "";
            if (!byParent.TryGetValue(parent, out var list))
            {
                list = new System.Collections.Generic.List<SearchIndex.IndexEntry>();
                byParent[parent] = list;
            }
            // Evitar duplicatas em atualizações incrementais
            if (!list.Any(e => e.Name == entry.Name && e.Type == entry.Type))
            {
                list.Add(entry);
            }
        }

        public SearchIndex GetIndex()
        {
            if (_index != null) return _index;
            EnsureInitialized();

            lock (_lock)
            {
                if (_index != null) return _index;
                try
                {
                    if (File.Exists(_indexPath))
                    {
                        Logger.Debug(string.Format("Loading index from disk: {0}", _indexPath));
                        string json = File.ReadAllText(_indexPath);
                        _index = SearchIndex.FromJson(json);
                        BuildParentIndex(_index);
                        Logger.Info(string.Format("Index loaded. Objects: {0}", _index.Objects.Count));
                    }
                }
                catch (Exception ex) { Logger.Error("Load Index Error: " + ex.Message); }

                if (_index == null) _index = new SearchIndex();
                return _index;
            }
        }

        public void UpdateIndex(SearchIndex index)
        {
            lock (_lock)
            {
                BuildParentIndex(index);
                _index = index;
            }
            // Fire and forget save to disk
            Task.Run(() => FlushToDisk());
        }

        private void FlushToDisk()
        {
            if (_savingInProgress) return;
            lock (_lock)
            {
                _savingInProgress = true;
                try
                {
                    string dir = Path.GetDirectoryName(_indexPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    
                    // PERFORMANCE: Use optimized JSON settings for faster serialization
                    var settings = new Newtonsoft.Json.JsonSerializerSettings { 
                        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                        DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore
                    };
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(_index, settings);
                    
                    File.WriteAllText(_indexPath, json);
                    _lastSavedJsonHash = GetJsonHash(json);
                    Logger.Debug("Index flushed to disk (Background)");
                }
                catch (Exception ex) { Logger.Error("Flush Error: " + ex.Message); }
                finally { _savingInProgress = false; }
            }
        }

        private string GetJsonHash(string json)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        public void UpdateEntry(global::Artech.Architecture.Common.Objects.KBObject obj)
        {
            var index = GetIndex();
            string parentName = null;
            string moduleName = null;
            try {
                if (obj.Parent != null && obj.Parent.Guid != obj.Guid) {
                    if (obj.Parent.TypeDescriptor.Name == "DesignModel") parentName = "Root Module";
                    else if (obj.Parent is global::Artech.Architecture.Common.Objects.Module || obj.Parent is global::Artech.Architecture.Common.Objects.Folder)
                        parentName = obj.Parent.Name;
                }
            } catch { }
            try { if (obj.Module != null && obj.Module.Guid != obj.Guid) moduleName = obj.Module.Name; } catch { }

            var entry = new SearchIndex.IndexEntry
            {
                Name = obj.Name,
                Type = obj.TypeDescriptor.Name,
                Description = obj.Description,
                Parent = parentName,
                Module = moduleName
            };

            if (obj is global::Artech.Genexus.Common.Objects.Attribute attr)
            {
                entry.DataType = attr.Type.ToString();
                entry.Length = attr.Length;
                entry.Decimals = attr.Decimals;
            }

            // ENRICHMENT: Dependencies (Calls and Tables)
            try
            {
                foreach (dynamic reference in obj.GetReferences())
                {
                    try
                    {
                        dynamic targetKey = reference.To;
                        string targetName = targetKey.Name;

                        // Robust Name Resolution
                        if (string.IsNullOrEmpty(targetName))
                        {
                            string keyStr = targetKey.ToString();
                            if (keyStr.Contains(":")) targetName = keyStr.Split(':')[1];
                            else targetName = keyStr;
                        }

                        if (string.IsNullOrEmpty(targetName)) continue;

                        string targetType = targetKey.TypeDescriptor.Name;

                        if (targetType == "Attribute" || targetType == "Table")
                        {
                            if (!entry.Tables.Contains(targetName)) entry.Tables.Add(targetName);
                        }
                        else
                        {
                            if (!entry.Calls.Contains(targetName)) entry.Calls.Add(targetName);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            string key = string.Format("{0}:{1}", entry.Type, entry.Name);
            lock (_lock)
            {
                // Incremental update of Parent Index to avoid O(N) rebuild
                if (index.ChildrenByParent != null)
                {
                    // Remove old entry from parent index if it exists and changed parents
                    if (index.Objects.TryGetValue(key, out var oldEntry) && oldEntry.Parent != entry.Parent)
                    {
                        string oldParent = oldEntry.Parent ?? "";
                        if (index.ChildrenByParent.TryGetValue(oldParent, out var oldList))
                            oldList.RemoveAll(e => e.Name == entry.Name && e.Type == entry.Type);
                    }
                    AddOrUpdateEntryInParentIndex(index.ChildrenByParent, entry);
                }

                if (index.Objects.ContainsKey(key)) index.Objects[key] = entry;
                else index.Objects.Add(key, entry);
            }
            
            // Fire and forget save to disk
            Task.Run(() => FlushToDisk());
        }

        public void RemoveEntry(string type, string name)
        {
            var index = GetIndex();
            string key = string.Format("{0}:{1}", type, name);
            lock (_lock)
            {
                if (index.Objects.Remove(key)) UpdateIndex(index);
            }
        }

        public void Clear() { lock (_lock) { _index = null; } }
    }
}
