using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GxMcp.Worker.Services
{
    public class WriteService
    {
        private readonly ObjectService _objectService;
        private ValidationService _validationService;
        private static readonly object _flushLock = new object();
        private static System.Timers.Timer _flushTimer;
        private static bool _pendingCommit = false;

        public WriteService(ObjectService objectService)
        {
            _objectService = objectService;
            InitializeFlushTimer();
        }

        public void SetValidationService(ValidationService vs) { _validationService = vs; }

        private void InitializeFlushTimer()
        {
            if (_flushTimer != null) return;
            lock (_flushLock)
            {
                if (_flushTimer != null) return;
                _flushTimer = new System.Timers.Timer(2000); // 2 seconds debounce
                _flushTimer.AutoReset = false;
                _flushTimer.Elapsed += (s, e) => FlushBackground();
            }
        }

        private void FlushBackground()
        {
            if (!_pendingCommit) return;
            
            lock (_flushLock)
            {
                if (!_pendingCommit) return;
                try
                {
                    Logger.Info("[BACKGROUND-FLUSH] Starting commits...");
                    var kb = _objectService.GetKbService().GetKB();
                    if (kb == null) return;

                    // Commits
                    var model = kb.DesignModel;
                    if (model != null) {
                        try {
                            var modelCommit = model.GetType().GetMethod("Commit", BindingFlags.Public | BindingFlags.Instance);
                            modelCommit?.Invoke(model, null);
                            Logger.Info("[BACKGROUND-FLUSH] Model.Commit() successful.");
                        } catch (Exception ex) { Logger.Debug("[BACKGROUND-FLUSH] Model.Commit skipped: " + ex.Message); }
                    }
                    
                    try {
                        var kbCommit = kb.GetType().GetMethod("Commit", BindingFlags.Public | BindingFlags.Instance);
                        kbCommit?.Invoke(kb, null);
                        Logger.Info("[BACKGROUND-FLUSH] KB.Commit() successful.");
                    } catch (Exception ex) { Logger.Debug("[BACKGROUND-FLUSH] KB.Commit skipped: " + ex.Message); }

                    _pendingCommit = false;
                    Logger.Info("[BACKGROUND-FLUSH] Full commit cycle complete.");
                }
                catch (Exception ex)
                {
                    Logger.Error("[BACKGROUND-FLUSH] ERROR: " + ex.Message);
                }
            }
        }

        private void ScheduleFlush()
        {
            _pendingCommit = true;
            _flushTimer.Stop();
            _flushTimer.Start();
        }

        public string WriteObject(string target, string partName, string code, bool autoValidate = true)
        {
            try
            {
                // DEBUG ENCODING: Detect and decode Base64 if needed
                string decodedCode = code;
                if (!string.IsNullOrEmpty(code) && (code.EndsWith("=") || code.Length > 100)) {
                    try {
                        byte[] data = Convert.FromBase64String(code);
                        decodedCode = System.Text.Encoding.UTF8.GetString(data);
                        Logger.Info("[DEBUG-SAVE] Payload decoded from Base64.");
                    } catch { /* Not base64, use as is */ }
                }

                // Nirvana v19.4: Auto-Healing (Pre-save validation)
                if (autoValidate && _validationService != null && !partName.Equals("Variables", StringComparison.OrdinalIgnoreCase) && !partName.Equals("Structure", StringComparison.OrdinalIgnoreCase))
                {
                    string validationRes = _validationService.ValidateCode(target, partName, decodedCode);
                    var valJson = JObject.Parse(validationRes);
                    if (valJson["status"]?.ToString() == "Error")
                    {
                        Logger.Warn($"[AUTO-HEALING] Blocked invalid code for {target} ({partName}): {valJson["errors"]?[0]?["message"]}");
                        return validationRes; // Return the error immediately to the LLM
                    }
                }

                Logger.Info(string.Format("[DEBUG-SAVE] Request received for {0} (Part: {1}, Code Length: {2})", target, partName, decodedCode?.Length ?? 0));
                
                var obj = _objectService.FindObject(target);
                if (obj == null) {
                    Logger.Error("[DEBUG-SAVE] Object NOT FOUND: " + target);
                    return "{\"error\": \"Object not found\"}";
                }

                Logger.Debug(string.Format("[DEBUG-SAVE] Object Found: {0} ({1})", obj.Name, obj.TypeDescriptor.Name));

                // ... (rest of the log)
                Guid partGuid = MapLogicalPartToGuid(obj.TypeDescriptor.Name, partName);
                global::Artech.Architecture.Common.Objects.KBObjectPart part = null;
                
                // Strategy 1: Map by logical GUID
                if (partGuid != Guid.Empty) {
                    part = obj.Parts.Cast<global::Artech.Architecture.Common.Objects.KBObjectPart>().FirstOrDefault(p => p.Type == partGuid);
                }

                // Strategy 2: Fallback to name-based or interface-based matching
                if (part == null)
                {
                    string pName = partName.ToLower();
                    foreach (global::Artech.Architecture.Common.Objects.KBObjectPart p in obj.Parts)
                    {
                        if (pName == "variables" && p is global::Artech.Genexus.Common.Parts.VariablesPart) { part = p; break; }
                        if ((pName == "source" || pName == "code" || pName == "events") && p is global::Artech.Architecture.Common.Objects.ISource) { part = p; break; }
                        if (pName == "structure" && (p.GetType().Name.Contains("Structure") || p.TypeDescriptor.Name.Equals("Structure", StringComparison.OrdinalIgnoreCase))) { part = p; break; }
                    }
                }

                if (part == null) {
                    Logger.Error("[DEBUG-SAVE] Part NOT FOUND in object: " + partName);
                    return "{\"error\": \"Part not found in " + obj.TypeDescriptor.Name + "\"}";
                }

                // 1. SET CONTENT
                bool contentSet = false;
                if (part is global::Artech.Genexus.Common.Parts.VariablesPart varPart)
                {
                    VariableInjector.SetVariablesFromText(varPart, decodedCode);
                    contentSet = true;
                }
                else if (part is global::Artech.Architecture.Common.Objects.ISource sourcePart)
                {
                    // NO-CHANGE SKIP: If code is identical, don't trigger Save/Commit
                    if (sourcePart.Source == decodedCode)
                    {
                        Logger.Info("[DEBUG-SAVE] Content is identical. Skipping Save.");
                        return "{\"status\": \"Success\", \"details\": \"No change\"}";
                    }

                    sourcePart.Source = decodedCode;
                    
                    // Auto-inject variables based on the new code (Optimized with Index)
                    try {
                        var index = _objectService.GetKbService().GetIndexCache().GetIndex();
                        VariableInjector.InjectVariables(obj, decodedCode, index);
                    } catch (Exception ex) {
                        Logger.Warn("[DEBUG-SAVE] Auto-inject variables failed: " + ex.Message);
                    }
                    contentSet = true;
                }
                else if (partName.Equals("Structure", StringComparison.OrdinalIgnoreCase) && 
                        (obj is global::Artech.Genexus.Common.Objects.Transaction || obj.TypeDescriptor.Name.Equals("SDT", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        StructureParser.ParseFromText(obj, decodedCode);
                        contentSet = true;
                        Logger.Info("[DEBUG-SAVE] Structure DSL successfully parsed and applied.");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("[DEBUG-SAVE] Error parsing Structure DSL: " + ex.Message);
                        return "{\"error\": \"Invalid Structure Syntax: " + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
                    }
                }
                else
                {
                    try {
                        if (decodedCode.Trim().StartsWith("<") && !partName.Equals("Structure", StringComparison.OrdinalIgnoreCase)) {
                            part.DeserializeFromXml(decodedCode);
                            contentSet = true;
                        } else {
                            var contentProp = part.GetType().GetProperty("Source", BindingFlags.Public | BindingFlags.Instance)
                                           ?? part.GetType().GetProperty("Content", BindingFlags.Public | BindingFlags.Instance);
                            if (contentProp != null && contentProp.CanWrite) {
                                contentProp.SetValue(part, decodedCode);
                                contentSet = true;
                            }
                        }
                    } catch { }
                }

                if (!contentSet) {
                    Logger.Warn("[DEBUG-SAVE] No suitable method found to update part content.");
                }

                // 2. FORCE DIRTY (Crucial)
                try {
                    // Mark Part as Dirty
                    var pType = part.GetType();
                    var pDirtyProp = pType.GetProperty("Dirty", BindingFlags.Public | BindingFlags.Instance) 
                                  ?? pType.GetProperty("IsDirty", BindingFlags.Public | BindingFlags.Instance);
                    if (pDirtyProp != null) {
                        pDirtyProp.SetValue(part, true);
                        Logger.Debug("[DEBUG-SAVE] Part property '" + pDirtyProp.Name + "' set to TRUE");
                    }

                    // Mark Header Object as Dirty (Essential for Save)
                    var oType = obj.GetType();
                    var oDirtyProp = oType.GetProperty("Dirty", BindingFlags.Public | BindingFlags.Instance)
                                  ?? oType.GetProperty("IsDirty", BindingFlags.Public | BindingFlags.Instance);
                    if (oDirtyProp != null) {
                        oDirtyProp.SetValue(obj, true);
                        Logger.Debug("[DEBUG-SAVE] Object property '" + oDirtyProp.Name + "' set to TRUE");
                    }
                } catch (Exception ex) { Logger.Debug("[DEBUG-SAVE] Force Dirty failed: " + ex.Message); }

                // 3. PERSISTENCE SEQUENCE
                try
                {
                    var kb = _objectService.GetKbService().GetKB();
                    if (kb == null) throw new Exception("KB not opened");

                    // 1. Start Transaction
                    Logger.Info("[DEBUG-SAVE] Starting SDK Transaction...");
                    var transaction = kb.BeginTransaction();

                    try {
                        // 2. Checkout
                        try {
                            var checkoutMethod = obj.GetType().GetMethod("Checkout", BindingFlags.Public | BindingFlags.Instance);
                            checkoutMethod?.Invoke(obj, null);
                            Logger.Debug("[DEBUG-SAVE] SDK Checkout invoked.");
                        } catch { }

                        // 3. Save Part (CRITICAL: Save the part explicitly first)
                        Logger.Info(string.Format("[DEBUG-SAVE] Invoking part.Save() for {0}...", part.TypeDescriptor?.Name));
                        part.Save();
                        Logger.Info("[DEBUG-SAVE] part.Save() completed.");

                        // 4. Save Object (Unified approach)
                        Logger.Info("[DEBUG-SAVE] Invoking obj.EnsureSave()...");
                        obj.EnsureSave();
                        Logger.Info("[DEBUG-SAVE] obj.EnsureSave() completed.");
                        
                        // 5. Transaction Commit
                        Logger.Info("[DEBUG-SAVE] Committing SDK Transaction...");
                        transaction.Commit();
                        Logger.Info("[DEBUG-SAVE] SDK Transaction Committed.");
                    }
                    catch (Exception ex)
                    {
                        var issues = SdkDiagnosticsHelper.GetDiagnostics(obj);
                        transaction.Rollback();

                        var errorRes = new JObject();
                        errorRes["status"] = "Error";
                        errorRes["error"] = ex.Message;
                        errorRes["issues"] = issues;
                        return errorRes.ToString();
                    }

                    // FAST SAVE: Run heavy indexing in background
                    Task.Run(() => {
                        try {
                            _objectService.GetKbService().GetIndexCache().UpdateEntry(obj);
                        } catch (Exception ex) { Logger.Error("[DEBUG-SAVE] Background Index update failed: " + ex.Message); }
                    });
                    
                    // Final persistence in background for "Fast Save"
                    ScheduleFlush();

                    Logger.Info("[DEBUG-SAVE] SAVE & COMMIT COMPLETE.");
                    return "{\"status\": \"Success\"}";
                }
                catch (Exception saveEx)
                {
                    Logger.Error("[DEBUG-SAVE] CRITICAL SDK EXCEPTION: " + saveEx.ToString());
                    return "{\"error\": \"SDK Save failed: " + saveEx.Message + "\"}";
                }
            }
            catch (Exception ex)
            {
                Logger.Error("[DEBUG-SAVE] OUTER EXCEPTION: " + ex.ToString());
                return "{\"error\": \"" + ex.Message + "\"}";
            }
        }

        public string AddVariable(string target, string varName, string typeName = null)
        {
            try
            {
                var obj = _objectService.FindObject(target);
                if (obj == null) return "{\"error\": \"Object not found\"}";

                var varPart = obj.Parts.Get<global::Artech.Genexus.Common.Parts.VariablesPart>();
                if (varPart == null) return "{\"error\": \"Variables part not found\"}";

                if (varPart.Variables.Any(v => string.Equals(v.Name, varName, StringComparison.OrdinalIgnoreCase)))
                    return "{\"status\": \"Variable already exists\"}";

                if (!string.IsNullOrEmpty(typeName))
                {
                    global::Artech.Genexus.Common.Variable newVar = new global::Artech.Genexus.Common.Variable(varPart);
                    newVar.Name = varName;

                    if (VariableInjector.TryParseDbType(typeName, out var dbType))
                    {
                        newVar.Type = dbType;
                    }
                    else
                    {
                        var targetObj = VariableInjector.ResolveTypeObject(varPart.Model, typeName);
                        if (targetObj != null)
                        {
                            if (targetObj is global::Artech.Genexus.Common.Objects.Domain dom)
                                newVar.DomainBasedOn = dom;
                            else if (targetObj.TypeDescriptor.Name.Equals("SDT", StringComparison.OrdinalIgnoreCase))
                            {
                                newVar.Type = global::Artech.Genexus.Common.eDBType.GX_SDT;
                                newVar.SetPropertyValue("DataType", targetObj.Key);
                            }
                            else if (targetObj is global::Artech.Genexus.Common.Objects.Transaction trn && trn.IsBusinessComponent)
                            {
                                newVar.Type = global::Artech.Genexus.Common.eDBType.GX_BUSCOMP;
                                newVar.SetPropertyValue("DataType", targetObj.Key);
                            }
                        }
                    }
                    varPart.Variables.Add(newVar);
                }
                else
                {
                    // Use intelligent creation (attribute inheritance + heuristics)
                    var newVar = VariableInjector.CreateVariable(varPart, varName);
                    varPart.Variables.Add(newVar);
                }

                obj.EnsureSave();
                ScheduleFlush();
                
                return "{\"status\": \"Success\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private Guid MapLogicalPartToGuid(string objType, string logicalPart)
        {
            string p = logicalPart.ToLower();
            
            // GUIDs Oficiais GeneXus 18
            if (objType.Equals("Procedure", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "source" || p == "code") return Guid.Parse("c5f0ef88-9ef8-4218-bf76-915024b3c48f");
                if (p == "rules") return Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "help") return Guid.Parse("017ea008-6202-4468-a400-3f412c938473");
            }
            
            if (objType.Equals("WebPanel", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "events" || p == "source" || p == "code") return Guid.Parse("c44bd5ff-f918-415b-98e6-aca44fed84fa");
                if (p == "rules") return Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "layout") return Guid.Parse("ad3ca970-19d0-44e1-a7b7-db05556e820c");
                if (p == "webform") return Guid.Parse("d24a58ad-57ba-41b7-9e6e-eaca3543c778");
            }

            if (objType.Equals("Transaction", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "structure") return Guid.Parse("1608677c-a7a2-4a00-8809-6d2466085a5a");
                if (p == "events" || p == "source" || p == "code") return Guid.Parse("c44bd5ff-f918-415b-98e6-aca44fed84fa");
                if (p == "rules") return Guid.Parse("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "webform") return Guid.Parse("d24a58ad-57ba-41b7-9e6e-eaca3543c778");
            }

            if (objType.Equals("DataProvider", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "source" || p == "code") return Guid.Parse("91705646-6086-4f32-8871-08149817e754");
                if (p == "variables") return Guid.Parse("e4c4ade7-53f0-4a56-bdfd-843735b66f47");
                if (p == "help") return Guid.Parse("017ea008-6202-4468-a400-3f412c938473");
            }

            if (objType.Equals("SDT", StringComparison.OrdinalIgnoreCase) || objType.Equals("StructuredDataType", StringComparison.OrdinalIgnoreCase))
            {
                if (p == "structure" || p == "source") return Guid.Parse("8597371d-1941-4c12-9c17-48df9911e2f3");
            }

            return ObjectService.GetPartGuid(p);
        }

    }
}
