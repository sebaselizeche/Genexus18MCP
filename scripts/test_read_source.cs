using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Linq;

public class SdkTester {
    public static void Main() {
        string gxPath = @"C:\Program Files (x86)\GeneXus\GeneXus18";
        string kbPath = @"C:\KBs\academicoLocal";
        
        var sw = Stopwatch.StartNew();
        Console.WriteLine("Step 1: Loading Assemblies...");
        Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Common.dll"));
        Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Architecture.Common.dll"));
        Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Architecture.UI.Framework.dll"));
        Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Genexus.Common.dll"));
        var connAsm = Assembly.LoadFrom(Path.Combine(gxPath, "Connector.dll"));
        
        Console.WriteLine("Step 2: Initializing Connector...");
        var uiType = Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Architecture.UI.Framework.dll")).GetType("Artech.Architecture.UI.Framework.Services.UIServices");
        uiType.GetMethod("SetDisableUI", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { true });
        
        var connType = connAsm.GetType("Artech.Core.Connector");
        connType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        connType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        
        var kbType = typeof(Artech.Architecture.Common.Objects.KnowledgeBase);
        kbType.GetProperty("KBFactory", BindingFlags.Public | BindingFlags.Static).SetValue(null, Activator.CreateInstance(connAsm.GetType("Connector.KBFactory")));
        
        var initType = Assembly.LoadFrom(Path.Combine(gxPath, "Artech.Genexus.Common.dll")).GetType("Artech.Genexus.Common.KBModelObjectsInitializer");
        initType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        Console.WriteLine("Initialization took {0}ms", sw.ElapsedMilliseconds);

        sw.Restart();
        Console.WriteLine("Step 3: Opening KB...");
        var options = new Artech.Architecture.Common.Objects.KnowledgeBase.OpenOptions(kbPath);
        options.EnableMultiUser = true;
        options.AvoidIndexing = true;
        
        var kb = Artech.Architecture.Common.Objects.KnowledgeBase.Open(options);
        Console.WriteLine("KB Open took {0}ms. DesignModel: {1}", sw.ElapsedMilliseconds, kb.DesignModel.Name);

        sw.Restart();
        Console.WriteLine("Step 4: Finding Object...");
        // GetByName with 3 args
        var results = kb.DesignModel.Objects.GetByName(null, null, "ProcArqCandUniGra");
        var obj = results.Cast<Artech.Architecture.Common.Objects.KBObject>().FirstOrDefault();
        Console.WriteLine("FindObject took {0}ms", sw.ElapsedMilliseconds);

        if (obj != null) {
            sw.Restart();
            Console.WriteLine("Step 5: Reading Rules...");
            Guid rulesGuid = new Guid("9b0a32a3-de6d-4be1-a4dd-1b85d3741534");
            // Use manual loop for compatibility
            Artech.Architecture.Common.Objects.KBObjectPart part = null;
            foreach (Artech.Architecture.Common.Objects.KBObjectPart p in obj.Parts) {
                if (p.Type == rulesGuid) { part = p; break; }
            }

            if (part != null) {
                var source = (part as Artech.Architecture.Common.Objects.ISource).Source;
                Console.WriteLine("ReadSource SUCCESS in {0}ms", sw.ElapsedMilliseconds);
                Console.WriteLine("Content Preview: {0}", (source.Length > 50 ? source.Substring(0, 50) : source));
            } else {
                Console.WriteLine("Rules part not found via GUID.");
            }
        }
        
        kb.Close();
    }
}
