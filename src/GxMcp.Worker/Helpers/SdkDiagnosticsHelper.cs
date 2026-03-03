using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Common.Diagnostics;

namespace GxMcp.Worker.Helpers
{
    public static class SdkDiagnosticsHelper
    {
        public static JArray GetDiagnostics(KBObject obj)
        {
            var issues = new JArray();
            try
            {
                // 1. Structural Validation (Standard SDK)
                var output = new OutputMessages();
                obj.Validate(output);

                foreach (var msg in output.OnlyMessages)
                {
                    issues.Add(CreateIssueFromSdkMessage(obj, msg));
                }

                // 2. Scan Parts for "Messages" property (Where parser/compiler errors live)
                foreach (var part in obj.Parts)
                {
                    CollectDetailedMessages(part, issues, obj, part.TypeDescriptor?.Name);
                }

                // 3. Scan the Object itself
                CollectDetailedMessages(obj, issues, obj, "Object");
            }
            catch (Exception ex)
            {
                Logger.Error("SdkDiagnosticsHelper Error: " + ex.Message);
            }
            return issues;
        }

        private static void CollectDetailedMessages(object target, JArray issues, KBObject contextObj, string partName)
        {
            try
            {
                var prop = target.GetType().GetProperty("Messages", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (prop != null)
                {
                    var value = prop.GetValue(target);
                    if (value is System.Collections.IEnumerable list)
                    {
                        foreach (object msg in list)
                        {
                            if (msg == null) continue;
                            issues.Add(CreateIssueFromSdkMessage(contextObj, msg, partName));
                        }
                    }
                }
            }
            catch { }
        }

        private static JObject CreateIssueFromSdkMessage(KBObject obj, object msg, string partName = null)
        {
            string msgText = msg.ToString();
            string msgCode = "SDK";
            string severity = "Error";
            int line = 1;
            int column = 1;

            try {
                // Use reflection/dynamic to get properties from various SDK message types (OutputError, Message, etc.)
                dynamic dMsg = msg;
                try { msgText = dMsg.Text ?? dMsg.Description ?? msgText; } catch {}
                try { msgCode = dMsg.ErrorCode ?? dMsg.Id ?? dMsg.Code ?? msgCode; } catch {}
                
                try {
                    var position = dMsg.Position;
                    if (position != null && position.GetType().Name.Contains("TextPosition")) {
                        line = (int)position.Line;
                        column = (int)position.Char;
                    }
                } catch {}

                try {
                    string lvl = dMsg.Level.ToString() ?? dMsg.Type.ToString() ?? "";
                    if (lvl.Contains("Warn")) severity = "Warning";
                    else if (lvl.Contains("Info")) severity = "Information";
                } catch {}
            } catch { }

            // Heuristic for line numbers in plain text messages
            if (line == 1) {
                var match = System.Text.RegularExpressions.Regex.Match(msgText, @"line\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) int.TryParse(match.Groups[1].Value, out line);
            }

            var issue = new JObject();
            issue["code"] = msgCode;
            issue["title"] = "GeneXus Error/Message";
            issue["severity"] = severity;
            issue["description"] = msgText;
            issue["line"] = line;
            issue["column"] = column;
            issue["part"] = partName ?? GetObjectDefaultPart(obj);
            issue["suggestion"] = InferSuggestion(msgText);
            return issue;
        }

        private static string InferSuggestion(string message)
        {
            string m = message.ToLower();
            if (m.Contains("not defined") || m.Contains("não definida")) return "Check if the variable or attribute is properly declared in the Variables part or exists in the Transaction structure.";
            if (m.Contains("expected") || m.Contains("esperado")) return "Check for missing syntax elements like ENDIF, ENDFOR, or semicolons.";
            if (m.Contains("type mismatch") || m.Contains("incompatíveis")) return "The data types of the expressions don't match. Check if you're comparing a String with a Numeric.";
            if (m.Contains("invalid command")) return "The command is not valid in this part of the object (e.g., UI commands inside a Procedure without output).";
            return "Review the syntax and ensure all referenced objects exist in the KB.";
        }

        public static string GetObjectDefaultPart(KBObject obj)
        {
            if (obj is Artech.Genexus.Common.Objects.Procedure) return "Source";
            if (obj is Artech.Genexus.Common.Objects.WebPanel) return "Events";
            if (obj is Artech.Genexus.Common.Objects.Transaction) return "Structure";
            if (obj is Artech.Genexus.Common.Objects.DataProvider) return "Source";
            return "Logic";
        }
    }
}
