using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using GxMcp.Worker.Models;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class SearchService
    {
        private readonly IndexCacheService _indexCacheService;
        private static readonly ConcurrentDictionary<string, string> _queryCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastIndexTime = DateTime.MinValue;

        public SearchService(IndexCacheService indexCacheService)
        {
            _indexCacheService = indexCacheService;
        }

        public string Search(string query, string typeFilter = null, string domainFilter = null, int limit = 50)
        {
            try
            {
                var index = _indexCacheService.GetIndex();
                if (index == null || index.Objects.Count == 0) return "{\"error\": \"Index empty.\"}";

                if (index.LastUpdated > _lastIndexTime) { _queryCache.Clear(); _lastIndexTime = index.LastUpdated; }

                string cacheKey = string.Format("{0}|{1}|{2}|{3}", query ?? "", typeFilter ?? "", domainFilter ?? "", limit);
                if (_queryCache.TryGetValue(cacheKey, out var cached)) return cached;

                var criteria = ParseQuery(query);
                if (!string.IsNullOrEmpty(typeFilter)) criteria.TypeFilter = typeFilter;
                if (!string.IsNullOrEmpty(domainFilter)) criteria.DomainFilter = domainFilter;

                IEnumerable<SearchIndex.IndexEntry> sourceSet = null;

                // PERFORMANCE CRITICAL: If searching by parent, use the specialized index
                if (!string.IsNullOrEmpty(criteria.ParentFilter) && index.ChildrenByParent != null)
                {
                    if (index.ChildrenByParent.TryGetValue(criteria.ParentFilter, out var children))
                    {
                        sourceSet = children;
                    }
                    else
                    {
                        sourceSet = Enumerable.Empty<SearchIndex.IndexEntry>();
                    }
                }
                else
                {
                    sourceSet = index.Objects.Values;
                }

                // PLINQ Search: Ultra fast scan
                var queryResults = sourceSet.AsParallel();

                // 1. Apply Hard Filters
                if (!string.IsNullOrEmpty(criteria.TypeFilter))
                    queryResults = queryResults.Where(e => IsTypeMatch(e.Type, criteria.TypeFilter));
                
                if (!string.IsNullOrEmpty(criteria.DomainFilter))
                    queryResults = queryResults.Where(e => string.Equals(e.BusinessDomain, criteria.DomainFilter, StringComparison.OrdinalIgnoreCase));

                // Parent filter is already applied if we used the specialized index, but double check for non-indexed cases
                if (!string.IsNullOrEmpty(criteria.ParentFilter) && (sourceSet == index.Objects.Values))
                    queryResults = queryResults.Where(e => string.Equals(e.Parent, criteria.ParentFilter, StringComparison.OrdinalIgnoreCase));

                // 2. Apply Semantic Filters (usedby:)
                if (!string.IsNullOrEmpty(criteria.UsedByFilter))
                {
                    queryResults = queryResults.Where(e => 
                        (e.RootTable != null && string.Equals(e.RootTable, criteria.UsedByFilter, StringComparison.OrdinalIgnoreCase)) ||
                        (e.Calls != null && e.Calls.Exists(c => string.Equals(c, criteria.UsedByFilter, StringComparison.OrdinalIgnoreCase))) ||
                        (e.Tables != null && e.Tables.Exists(t => string.Equals(t, criteria.UsedByFilter, StringComparison.OrdinalIgnoreCase)))
                    );
                }

                // 3. Scoring and Finalizing - OPTIMIZED FOR SEMANTIC RELEVANCE
                var scoredResults = queryResults
                    .Select(entry => {
                        int score = 0;
                        if (criteria.Terms.Count > 0) {
                            score = CalculateSemanticScore(entry, criteria.Terms);
                            if (score <= 0) return new RankedResult { Score = -1 };
                        } else {
                            score = (entry.Type == "Folder" || entry.Type == "Module") ? 1000 : 1;
                        }
                        return new RankedResult { Entry = entry, Score = score };
                    })
                    .Where(r => r.Score > 0)
                    .OrderByDescending(r => r.Score)
                    .ThenBy(r => r.Entry.Name)
                    .Take(limit)
                    .ToList();

                // OPTIMIZATION: If quick mode is requested (e.g. CTRL+P), return only essential fields
                bool isQuick = false;
                if (!string.IsNullOrEmpty(query) && query.Contains("@quick")) isQuick = true;
                
                string json;
                if (isQuick)
                {
                    json = Newtonsoft.Json.JsonConvert.SerializeObject(new { 
                        count = scoredResults.Count, 
                        results = scoredResults.Select(r => new {
                            guid = r.Entry.Guid,
                            name = r.Entry.Name,
                            type = r.Entry.Type,
                            parent = r.Entry.Parent
                        })
                    });
                }
                else
                {
                    json = Newtonsoft.Json.JsonConvert.SerializeObject(new { 
                        count = scoredResults.Count, 
                        results = scoredResults.Select(r => new {
                            guid = r.Entry.Guid,
                            name = r.Entry.Name,
                            type = r.Entry.Type,
                            description = r.Entry.Description,
                            parm = r.Entry.ParmRule,
                            snippet = r.Entry.SourceSnippet,
                            parent = r.Entry.Parent,
                            dataType = r.Entry.DataType,
                            length = r.Entry.Length,
                            decimals = r.Entry.Decimals,
                            table = r.Entry.RootTable
                        })
                    });
                }

                _queryCache.TryAdd(cacheKey, json);

                // PERFORMANCE: Object Warm-up (Background)
                // Load the top 5 results into the SDK's internal cache to speed up subsequent Read/Analyze calls.
                if (scoredResults.Count > 0)
                {
                    var topGuids = scoredResults.Take(5)
                        .Where(r => !string.IsNullOrEmpty(r.Entry.Guid))
                        .Select(r => new Guid(r.Entry.Guid))
                        .ToList();

                    Program.BackgroundQueue.Enqueue(() => {
                        try {
                            var kb = _indexCacheService.KbService?.GetKB();
                            if (kb == null) return;
                            foreach (var guid in topGuids) {
                                // Just calling .Get() triggers the SDK's object loader/caching mechanism
                                var obj = kb.DesignModel.Objects.Get(guid);
                                if (obj != null) Logger.Debug($"[Warm-up] Loaded {obj.Name} into SDK cache.");
                            }
                        } catch { }
                    });
                }

                return json;
            }
            catch (Exception ex) { return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}"; }
        }

        private int CalculateSemanticScore(SearchIndex.IndexEntry entry, HashSet<string> terms)
        {
            int score = 0;
            string name = entry.Name ?? "";
            string desc = entry.Description ?? "";
            
            foreach (var term in terms) {
                // Exact name match
                if (name.Equals(term, StringComparison.OrdinalIgnoreCase)) score += 2000;
                // Name starts with term
                else if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) score += 1000;
                // Name contains term
                else if (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 500;
                
                // Description match
                if (desc.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 300;

                // Keyword/Tag match
                if (entry.Keywords != null && entry.Keywords.Contains(term, StringComparer.OrdinalIgnoreCase)) score += 800;
                if (entry.Tags != null && entry.Tags.Contains(term, StringComparer.OrdinalIgnoreCase)) score += 800;

                // Structural context matches
                if (entry.Tables != null && entry.Tables.Contains(term, StringComparer.OrdinalIgnoreCase)) score += 400;
                if (entry.Calls != null && entry.Calls.Contains(term, StringComparer.OrdinalIgnoreCase)) score += 400;
            }
            return score;
        }

        private bool IsTypeMatch(string type, string query)
        {
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(query)) return false;
            string t = type.ToLower(); string q = query.ToLower();
            if (q == "prc" || q == "procedure") return t.Contains("procedure");
            if (q == "trn" || q == "transaction") return t.Contains("transaction");
            return t.Contains(q);
        }

        private SearchCriteria ParseQuery(string query)
        {
            var c = new SearchCriteria();
            if (string.IsNullOrEmpty(query)) return c;
            
            // Extract usedby:
            if (query.Contains("usedby:")) {
                var m = System.Text.RegularExpressions.Regex.Match(query, @"usedby:([\w\.]+)");
                if (m.Success) { c.UsedByFilter = m.Groups[1].Value; query = query.Replace(m.Value, ""); }
            }
            // Extract parent:
            if (query.Contains("parent:")) {
                var m = System.Text.RegularExpressions.Regex.Match(query, "parent:\"?([^\"]+)\"?");
                if (m.Success) { c.ParentFilter = m.Groups[1].Value; query = query.Replace(m.Value, ""); }
            }

            foreach (var part in query.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries)) {
                c.Terms.Add(part.ToLowerInvariant()); // Store terms lowercased once
            }
            return c;
        }

        private class RankedResult { public SearchIndex.IndexEntry Entry { get; set; } public int Score { get; set; } }
        private class SearchCriteria { 
            public string TypeFilter { get; set; } public string ParentFilter { get; set; } 
            public string UsedByFilter { get; set; } public string DomainFilter { get; set; } 
            public HashSet<string> Terms { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase); 
        }
    }
}
