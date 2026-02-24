using System;
using System.IO;
using System.Text;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using GxMcp.Worker.Services;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker
{
    class InspectEncoding {
        static void Main(string[] args) {
            try {
                string gxPath = @"C:\Program Files (x86)\GeneXus\GeneXus18";
                string kbPath = @"C:\KBs\academicoLocal";
                
                // Initialize
                var buildService = new BuildService();
                var indexService = new IndexCacheService(buildService);
                var kbService = new KbService(buildService, indexService);
                kbService.OpenKB(kbPath);
                
                var obj = kbService.GetKB().DesignModel.Objects.GetByName("DebugGravar").Cast<KBObject>().FirstOrDefault();
                if (obj == null) { Console.WriteLine("Object not found"); return; }
                
                var part = obj.Parts.Get<global::Artech.Architecture.Common.Objects.ISource>();
                string source = part.Source;
                
                Console.WriteLine("--- ENCODING INSPECTION ---");
                Console.WriteLine("Object: " + obj.Name);
                Console.WriteLine("Full Source String: " + source);
                
                Console.WriteLine("
Hex Dump of chars:");
                foreach (char c in source) {
                    Console.Write("{0:X4} ", (int)c);
                    if (c == '
') Console.WriteLine();
                }
                
                Console.WriteLine("

System Info:");
                Console.WriteLine("Default Encoding: " + Encoding.Default.WebName);
                Console.WriteLine("Current Culture: " + System.Threading.Thread.CurrentThread.CurrentCulture.Name);
            } catch (Exception ex) {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
