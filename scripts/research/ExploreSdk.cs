using System;
using System.Reflection;
using System.Linq;

public class ExploreSdk
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: ExploreSdk.exe <dllPath> <pattern>");
            return;
        }

        string dllPath = args[0];
        string pattern = args.Length > 1 ? args[1] : "";

        try
        {
            Assembly assembly = Assembly.LoadFrom(dllPath);
            Console.WriteLine(string.Format("Exploring assembly: {0}", assembly.FullName));

            var types = assembly.GetTypes()
                .Where(t => string.IsNullOrEmpty(pattern) || (t.FullName != null && t.FullName.Contains(pattern)))
                .OrderBy(t => t.FullName);

            foreach (var type in types)
            {
                Console.WriteLine(string.Format("Type: {0}", type.FullName));
                // Handle Enums
                if (type.IsEnum)
                {
                    foreach (var name in Enum.GetNames(type))
                    {
                        Console.WriteLine(string.Format("  Enum Value: {0}", name));
                    }
                }
                // Also list some members if it's a direct match or contains pattern
                if (pattern != "" && type.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                    {
                        Console.WriteLine(string.Format("  Property: {0} ({1})", prop.Name, prop.PropertyType.Name));
                    }
                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                    {
                        var value = field.IsLiteral ? field.GetRawConstantValue() : "(Non-literal)";
                        Console.WriteLine(string.Format("  Field: {0} ({1}) = {2}", field.Name, field.FieldType.Name, value));
                    }
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                    {
                        var parameters = method.GetParameters().Select(p => string.Format("{0} {1}", p.ParameterType.Name, p.Name));
                        Console.WriteLine(string.Format("  Method: {0}({1})", method.Name, string.Join(", ", parameters)));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(string.Format("Error: {0}", ex.Message));
            if (ex is ReflectionTypeLoadException)
            {
                var rex = (ReflectionTypeLoadException)ex;
                foreach (var le in rex.LoaderExceptions)
                {
                    Console.WriteLine(string.Format("  LoaderError: {0}", le.Message));
                }
            }
        }
    }
}
