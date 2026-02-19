using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GxMcp.Worker.Helpers
{
    public static class CodeParser
    {
        public static List<string> GetSections(string code)
        {
            var sections = new List<string>();
            // Matches: Sub 'Name', Sub "Name", Sub Name, Event 'Name', Event Name
            var subMatches = Regex.Matches(code, @"(?i)^\s*(?:Sub|Event)\s+['""]?([\w\.\-]+)['""]?", RegexOptions.Multiline);
            foreach (Match m in subMatches)
            {
                if (m.Success) sections.Add(m.Groups[1].Value);
            }
            return sections;
        }

        public static (int start, int end) GetSectionRange(string code, string sectionName)
        {
            // Regex to find the start of the section (Sub or Event)
            var pattern = @"(?i)^\s*(?:Sub|Event)\s+['""]?" + Regex.Escape(sectionName) + @"['""]?";
            var match = Regex.Match(code, pattern, RegexOptions.Multiline);
            
            if (!match.Success) return (-1, -1);

            int start = match.Index;
            string endPattern = "";
            
            if (match.Value.Trim().StartsWith("Sub", StringComparison.OrdinalIgnoreCase))
                endPattern = @"(?i)^\s*EndSub\b";
            else
                endPattern = @"(?i)^\s*EndEvent\b";

            var endMatch = Regex.Match(code.Substring(start), endPattern, RegexOptions.Multiline);
            if (!endMatch.Success) return (start, code.Length);

            return (start, start + endMatch.Index + endMatch.Length);
        }
    }
}
