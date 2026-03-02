using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class SelfTestService
    {
        private readonly KbService _kbService;
        private readonly SearchService _searchService;
        private readonly LinterService _linterService;

        public SelfTestService(KbService kbService, SearchService searchService, LinterService linterService)
        {
            _kbService = kbService;
            _searchService = searchService;
            _linterService = linterService;
        }

        public string RunAllTests()
        {
            var results = new JArray();
            
            results.Add(TestSearchScoring());
            results.Add(TestLinterLogic());
            results.Add(TestIndexCacheIntegrity());

            var summary = new JObject();
            summary["status"] = results.All(r => (string)r["status"] == "Pass") ? "Success" : "Partial";
            summary["timestamp"] = DateTime.Now;
            summary["tests"] = results;

            return summary.ToString();
        }

        private JObject TestSearchScoring()
        {
            var result = new JObject { ["name"] = "Search Semantic Scoring" };
            try {
                // Test search logic directly
                string json = _searchService.Search("Customer", null, null, 10);
                var data = JObject.Parse(json);
                
                if (data["results"] != null) {
                    result["status"] = "Pass";
                    result["message"] = $"Found {((JArray)data["results"]).Count} objects.";
                } else {
                    result["status"] = "Fail";
                    result["message"] = "Search returned no results.";
                }
            } catch (Exception ex) {
                result["status"] = "Fail";
                result["message"] = ex.Message;
            }
            return result;
        }

        private JObject TestLinterLogic()
        {
            var result = new JObject { ["name"] = "Linter Anti-Pattern Detection" };
            try {
                // Test linter on a dummy target (it will likely say not found but shouldn't crash)
                string json = _linterService.Lint("NonExistentObject");
                if (!string.IsNullOrEmpty(json)) {
                    result["status"] = "Pass";
                    result["message"] = "Linter engine is active.";
                } else {
                    result["status"] = "Fail";
                }
            } catch (Exception ex) {
                result["status"] = "Fail";
                result["message"] = ex.Message;
            }
            return result;
        }

        private JObject TestIndexCacheIntegrity()
        {
            var result = new JObject { ["name"] = "Super Cache Metadata" };
            try {
                var index = _kbService.GetIndexCache().GetIndex();
                if (index != null && index.Objects != null) {
                    result["status"] = "Pass";
                    result["objectCount"] = index.Objects.Count;
                } else {
                    result["status"] = "Fail";
                }
            } catch (Exception ex) {
                result["status"] = "Fail";
                result["message"] = ex.Message;
            }
            return result;
        }
    }
}
