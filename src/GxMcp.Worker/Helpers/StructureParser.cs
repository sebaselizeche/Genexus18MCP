using System;
using System.Text;
using System.Collections.Generic;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using GxMcp.Worker.Parsers;

namespace GxMcp.Worker.Helpers
{
    public static class StructureParser
    {
        private static IDslParser GetParser(KBObject obj)
        {
            if (obj is Transaction) return new TransactionDslParser();
            if (obj is Table) return new TableDslParser();
            if (obj.TypeDescriptor.Name.Equals("SDT", StringComparison.OrdinalIgnoreCase)) return new SdtDslParser();
            return null;
        }

        public static string SerializeToText(KBObject obj)
        {
            var sb = new StringBuilder();
            try
            {
                var parser = GetParser(obj);
                if (parser != null) parser.Serialize(obj, sb);
            }
            catch (Exception ex)
            {
                Logger.Error("DSL Serialization Error: " + ex.ToString());
                sb.AppendLine("// Serialization Error: " + ex.Message);
            }
            return sb.ToString().Trim();
        }

        public static void ParseFromText(KBObject obj, string text)
        {
            try
            {
                var parser = GetParser(obj);
                if (parser != null) parser.Parse(obj, text);
            }
            catch (Exception ex)
            {
                Logger.Error("DSL Parse Error: " + ex.ToString());
                throw;
            }
        }
    }
}
