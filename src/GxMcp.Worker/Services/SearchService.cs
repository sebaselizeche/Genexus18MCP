using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class SearchService
    {
        private readonly IndexCacheService _indexCacheService;

        public SearchService(IndexCacheService indexCacheService)
        {
            _indexCacheService = indexCacheService;
        }

        private static readonly Dictionary<string, string[]> BusinessSynonyms = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "ptc", new[] { "protocolo" } },
            { "protocolo", new[] { "ptc" } },
            { "fin", new[] { "financeiro" } },
            { "financeiro", new[] { "fin" } },
            { "acad", new[] { "acadêmico", "academico", "estudante", "aluno" } },
            { "wrf", new[] { "workflow", "fluxo", "etapa" } },
            { "trn", new[] { "transação", "transacao", "transaction" } },
            { "prc", new[] { "procedure", "processo" } },
            { "att", new[] { "atributo", "attribute" } },
            { "tbl", new[] { "tabela", "table" } },
            { "dom", new[] { "domínio", "dominio" } }
        };

        public string Search(string query, string typeFilter = null, string domainFilter = null, int limit = 50)
        {
            try
            {
                var index = _indexCacheService.GetIndex();
                if (index == null || index.Objects.Count == 0)
                    return "{\"error\": \"Search index not found or empty. Please run 'genexus_bulk_index' first.\"}";

                var expandedTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(query))
                {
                    foreach (var term in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        expandedTerms.Add(term);
                        if (BusinessSynonyms.TryGetValue(term, out var synonyms))
                        {
                            foreach (var syn in synonyms) expandedTerms.Add(syn);
                        }
                    }
                }

                string[] terms = expandedTerms.ToArray();
                var results = new List<RankedResult>();

                foreach (var entry in index.Objects.Values)
                {
                    // Hard Filters
                    if (!string.IsNullOrEmpty(typeFilter) && !IsTypeMatch(entry.Type, typeFilter)) continue;
                    if (!string.IsNullOrEmpty(domainFilter) && !string.Equals(entry.BusinessDomain, domainFilter, StringComparison.OrdinalIgnoreCase)) continue;

                    int score = 0;
                    if (terms.Length > 0)
                    {
                        score = CalculateScore(entry, terms);
                        if (score <= 0) continue;
                    }
                    else
                    {
                        // Listing mode (no query)
                        score = 1; // All matching filters get same score
                    }

                    results.Add(new RankedResult { Entry = entry, Score = score });
                }

                var finalResults = results
                    .OrderByDescending(r => r.Score)
                    .ThenBy(r => r.Entry.Name)
                    .Take(limit)
                    .Select(r => {
                        var dict = new Dictionary<string, object>();
                        dict["name"] = r.Entry.Name;
                        dict["type"] = r.Entry.Type;
                        if (r.Score > 1) dict["score"] = r.Score;
                        if (!string.IsNullOrEmpty(r.Entry.Description)) dict["description"] = r.Entry.Description;
                        if (r.Entry.BusinessDomain != null && r.Entry.BusinessDomain != "Geral") dict["domain"] = r.Entry.BusinessDomain;
                        dict["connections"] = (r.Entry.Calls?.Count ?? 0) + (r.Entry.CalledBy?.Count ?? 0);
                        return dict;
                    })
                    .ToList();

                return Newtonsoft.Json.JsonConvert.SerializeObject(new { 
                    count = finalResults.Count, 
                    results = finalResults 
                });
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private int CalculateScore(SearchIndex.IndexEntry entry, string[] terms)
        {
            int score = 0;
            string content = $"{entry.Name} {entry.Description} {entry.BusinessDomain} {string.Join(" ", entry.Tags ?? new List<string>())}";
            
            foreach (var term in terms)
            {
                // Exact name match
                if (entry.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) score += 500;
                
                // Prefix match
                if (entry.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) score += 200;

                // Substring name
                if (entry.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 100;

                // Type match boost
                if (IsTypeMatch(entry.Type, term)) score += 150;

                // Domain boost
                if (entry.BusinessDomain != null && entry.BusinessDomain.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 50;

                // Content match
                if (content.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) score += 10;
            }

            // Connection weight
            score += (entry.CalledBy?.Count ?? 0) * 5;

            return score;
        }

        private bool IsTypeMatch(string type, string query)
        {
            string t = type.ToLower();
            string q = query.ToLower();

            if (q == "prc" || q == "proc" || q == "procedure") return t.Contains("procedure") || t == "prc";
            if (q == "trn" || q == "transaction") return t.Contains("transaction") || t == "trn";
            if (q == "wp" || q == "wbp" || q == "webpanel") return t.Contains("webpanel") || t == "wbp";
            if (q == "att" || q == "attribute") return t.Contains("attribute") || t == "att";
            if (q == "tbl" || q == "table") return t.Contains("table") || t == "tbl";
            
            return t.Contains(q) || q.Contains(t);
        }

        private class RankedResult
        {
            public SearchIndex.IndexEntry Entry { get; set; }
            public int Score { get; set; }
        }
    }
}
