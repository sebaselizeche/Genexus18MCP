using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class CommandDispatcher
    {
        public static string LastRequest { get; private set; }
        private static CommandDispatcher _instance;
        private static readonly object _lock = new object();

        private readonly KbService _kbService;
        private readonly ObjectService _objectService;
        private readonly WriteService _writeService;
        private readonly ListService _listService;
        private readonly AnalyzeService _analyzeService;
        private readonly DataInsightService _dataInsightService;
        private readonly UIService _uiService;
        private readonly BuildService _buildService;
        private readonly RefactorService _refactorService;
        private readonly BatchService _batchService;
        private readonly ForgeService _forgeService;
        private readonly ValidationService _validationService;
        private readonly TestService _testService;
        private readonly IndexCacheService _indexCacheService;
        private readonly SearchService _searchService;
        private readonly WikiService _wikiService;
        private readonly HistoryService _historyService;
        private readonly VisualizerService _visualizerService;
        private readonly HealthService _healthService;
        private readonly LinterService _linterService;
        private readonly PatternService _patternService;
        private readonly PatchService _patchService;
        private readonly SDTService _sdtService;
        private readonly StructureService _structureService;
        private readonly FormatService _formatService;
        private readonly PropertyService _propertyService;
        private readonly VersionControlService _versionControlService;
        private readonly ConversionService _conversionService;
        private readonly SelfTestService _selfTestService;

        private CommandDispatcher()
        {
            _buildService = new BuildService();
            _indexCacheService = new IndexCacheService(_buildService);
            _kbService = new KbService(_buildService, _indexCacheService);
            _buildService.SetKbService(_kbService);
            _objectService = new ObjectService(_kbService, _buildService);
            _writeService = new WriteService(_objectService);
            _listService = new ListService(_kbService);
            _analyzeService = new AnalyzeService(_kbService, _objectService, _indexCacheService);
            _dataInsightService = new DataInsightService(_kbService, _objectService);
            _uiService = new UIService(_kbService, _objectService);
            _objectService.SetDataInsightService(_dataInsightService);
            _objectService.SetUIService(_uiService);
            _refactorService = new RefactorService(_kbService, _objectService, _indexCacheService);
            _batchService = new BatchService(_kbService, _writeService);
            _forgeService = new ForgeService(_kbService);
            _validationService = new ValidationService(_kbService);
            _testService = new TestService(_kbService, _buildService);
            _searchService = new SearchService(_indexCacheService);
            _wikiService = new WikiService(_objectService);
            _historyService = new HistoryService(_objectService, _writeService);
            _visualizerService = new VisualizerService();
            _healthService = new HealthService();
            _linterService = new LinterService(_objectService);
            _patternService = new PatternService(_indexCacheService, _objectService);
            _patchService = new PatchService(_objectService, _writeService);
            _sdtService = new SDTService(_objectService);
            _structureService = new StructureService(_objectService);
            _formatService = new FormatService();
            _propertyService = new PropertyService(_objectService);
            _versionControlService = new VersionControlService(_kbService);
            _conversionService = new ConversionService(_objectService);
            _selfTestService = new SelfTestService(_kbService, _searchService, _linterService);
        }

        public static CommandDispatcher Instance
        {
            get { lock (_lock) { return _instance ?? (_instance = new CommandDispatcher()); } }
        }

        public KbService GetKbService() { return _kbService; }

        public bool IsThreadSafe(string line)
        {
            try
            {
                var request = JObject.Parse(line);
                string method = request["method"] != null ? request["method"].ToString() : null;
                if (method == "ping") return true;
                var @params = request["params"] as JObject ?? new JObject();
                if (method == "execute_command")
                {
                    string module = @params["module"] != null ? @params["module"].ToString() : null;
                    if (module == "Health" || module == "Search" || module == "ListObjects" || module == "Formatting") return true;
                    return false;
                }
                return false;
            }
            catch { return false; }
        }

        public string Dispatch(string line)
        {
            LastRequest = line;
            try
            {
                Logger.Debug(string.Format("Dispatching command: {0}", line.Length > 500 ? line.Substring(0, 500) + "..." : line));
                var request = JObject.Parse(line);
                string method = request["method"] != null ? request["method"].ToString() : null;
                var @params = request["params"] as JObject ?? new JObject();

                if (@params["shadowPath"] != null) Environment.SetEnvironmentVariable("GX_SHADOW_PATH", @params["shadowPath"].ToString());

                if (method == "execute_command")
                {
                    string module = @params["module"] != null ? @params["module"].ToString() : null;
                    string action = @params["action"] != null ? @params["action"].ToString() : null;
                    string target = @params["target"] != null ? @params["target"].ToString() : null;
                    string payload = @params["payload"] != null ? @params["payload"].ToString() : null;
                    string client = @params["client"] != null ? @params["client"].ToString() : "ide";

                    switch (module)
                    {
                        case "KB":
                            if (action == "BulkIndex") return _kbService.BulkIndex();
                            if (action == "SelfTest") return _selfTestService.RunAllTests();
                            if (action == "GetIndexStatus") return _kbService.GetIndexStatus();
                            if (action == "Initialize") {
                                if (_kbService.GetKB() != null) return "{\"status\":\"Ready\"}";
                                if (_kbService.IsInitializing) return "{\"status\":\"Initializing\"}";
                                return "{\"error\":\"Failed to open KB\"}";
                            }
                            if (action == "CreateObject") return _objectService.CreateObject(@params["type"]?.ToString(), @params["name"]?.ToString());
                            return "{\"error\": \"Action not found in KB\"}";
                        case "ListObjects": return _searchService.Search(null, target, null, @params["limit"] != null ? (int)@params["limit"] : 100);
                        case "Search": return _searchService.Search(target ?? @params["query"]?.ToString(), null, null, @params["limit"] != null ? (int)@params["limit"] : 200);
                        case "Read":
                            if (action == "ExtractSource") return _objectService.ReadObjectSource(target, @params["part"]?.ToString() ?? "Source", @params["offset"]?.ToObject<int?>(), @params["limit"]?.ToObject<int?>(), client);
                            if (action == "ExtractAllParts") return _objectService.ExtractAllParts(target, client);
                            if (action == "GetAttribute") return _analyzeService.GetAttributeMetadata(target);
                            if (action == "GetVariables") return _analyzeService.GetVariables(target);
                            return _objectService.ReadObject(target);
                        case "Write":
                            if (action == "AddVariable") return _writeService.AddVariable(target, @params["varName"]?.ToString(), @params["typeName"]?.ToString());
                            return _writeService.WriteObject(target, action, payload);
                        case "Analyze":
                            if (action == "GetHierarchy") return _analyzeService.GetHierarchy(target);
                            if (action == "GetDataContext") return _dataInsightService.GetDataContext(target);
                            if (action == "GetParameters") return _analyzeService.GetParameters(target);
                            if (action == "ExplainCode") return _analyzeService.ExplainCode(target, payload);
                            if (action == "TranslateTo") return _conversionService.TranslateTo(target, @params["language"]?.ToString());
                            return _analyzeService.Analyze(target);
                        case "UI":
                            if (action == "GetUIContext") return _uiService.GetUIContext(target);
                            return "{\"error\":\"Unknown UI action\"}";
                        case "Batch": return _batchService.ProcessBatch(action, target, payload);
                        case "Forge": return _forgeService.Scaffold(target, payload, @params);
                        case "Refactor": return _refactorService.Refactor(target, action, payload);
                        case "Wiki": return _wikiService.Generate(target);
                        case "History": return _historyService.Execute(target, action);
                        case "Visualizer": return _visualizerService.GenerateGraph(target);
                        case "Linter": return _linterService.Lint(target, @params["part"]?.ToString());
                        case "Health": 
                            if (action == "Ping") return _healthService.Ping();
                            return _healthService.GetHealthReport();
                        case "Pattern": return _patternService.GetSample(target);
                        case "Property":
                            if (action == "SetProperty") return _propertyService.SetProperty(target, @params["name"]?.ToString(), payload, @params["control"]?.ToString());
                            return _propertyService.GetProperties(target, @params["control"]?.ToString());
                        case "VersionControl":
                            if (action == "Update") return _versionControlService.Update();
                            if (action == "Commit") return _versionControlService.Commit(payload);
                            return _versionControlService.GetPendingChanges();
                        case "Patch": return _patchService.ApplyPatch(target, @params["part"]?.ToString(), @params["operation"]?.ToString(), @params["content"]?.ToString(), @params["context"]?.ToString(), @params["expectedCount"] != null ? (int)@params["expectedCount"] : 1);
                        case "Test": return _testService.RunTest(target);
                        case "Validation": return _validationService.ValidateCode(target, @params["part"]?.ToString(), payload);
                        case "Build": return _buildService.Build(action, target);
                        case "Structure":
                            if (action == "GetTable") return _structureService.GetVisualStructure(target);
                            if (action == "GetVisualStructure") return _structureService.GetVisualStructure(target);
                            if (action == "GetVisualIndexes") return _structureService.GetVisualIndexes(target);
                            if (action == "UpdateVisualStructure") return _structureService.UpdateVisualStructure(target, payload);
                            return _sdtService.GetSDTStructure(target);
                        case "Formatting": return _formatService.Format(payload);
                    }
                }
                return "{\"error\": \"Method not found: " + EscapeJsonString(method) + "\"}";
            }
            catch (Exception ex) { return "{\"error\": \"" + EscapeJsonString(ex.Message) + "\"}"; }
        }

        public static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
