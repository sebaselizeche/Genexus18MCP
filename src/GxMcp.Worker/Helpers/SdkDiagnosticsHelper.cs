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
        private static PropertyInfo _messagesPropCache;

        public static JArray GetDiagnostics(KBObject obj)
        {
            var issues = new JArray();
            try
            {
                var output = new OutputMessages();
                obj.Validate(output);

                foreach (var msg in output.OnlyMessages)
                {
                    if (msg is OutputError error)
                    {
                        issues.Add(CreateIssueFromOutputError(obj, error));
                    }
                }

                // Also check the "Messages" property which some objects use for in-memory errors
                if (_messagesPropCache == null)
                    _messagesPropCache = obj.GetType().GetProperty("Messages", BindingFlags.Public | BindingFlags.Instance);

                if (_messagesPropCache != null)
                {
                    var messages = messagesProp.GetValue(obj) as System.Collections.IEnumerable;
                    if (messages != null)
                    {
                        foreach (var msg in messages)
                        {
                            // Avoid duplicates from Validate if possible, but Messages often has more runtime info
                            string msgStr = msg.ToString();
                            if (!issues.Any(i => i["description"]?.ToString() == msgStr))
                            {
                                issues.Add(CreateIssueFromMessage(obj, msgStr));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("SdkDiagnosticsHelper Error: " + ex.Message);
            }
            return issues;
        }

        private static JObject CreateIssueFromOutputError(KBObject obj, OutputError error)
        {
            int line = 1;
            int column = 1;
            string part = GetObjectDefaultPart(obj);

            if (error.Position is Artech.Common.Location.TextPosition textPos)
            {
                line = textPos.Line;
                column = textPos.Char;
            }

            string severity = "Information";
            if (error.Level == MessageLevel.Error) severity = "Error";
            else if (error.Level == MessageLevel.Warning) severity = "Warning";

            var issue = new JObject();
            issue["code"] = error.ErrorCode;
            issue["title"] = "SDK Validation";
            issue["severity"] = severity;
            issue["description"] = error.Text;
            issue["line"] = line;
            issue["column"] = column;
            issue["snippet"] = "";
            issue["part"] = part;
            return issue;
        }

        private static JObject CreateIssueFromMessage(KBObject obj, string message)
        {
            // Try to parse line number from string if it follows common pattern: "Error in line X: ..."
            int line = 1;
            int column = 1;
            string description = message;

            var match = System.Text.RegularExpressions.Regex.Match(message, @"line\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                int.TryParse(match.Groups[1].Value, out line);
            }

            var issue = new JObject();
            issue["code"] = "SDK";
            issue["title"] = "SDK Error";
            issue["severity"] = "Error";
            issue["description"] = description;
            issue["line"] = line;
            issue["column"] = column;
            issue["snippet"] = "";
            issue["part"] = GetObjectDefaultPart(obj);
            return issue;
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
