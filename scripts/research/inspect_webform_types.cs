using System;
using System.Reflection;
using Artech.Genexus.Common.Parts;

public class InspectWebForm
{
    public static void Main()
    {
        Type t = typeof(WebFormPart);
        PropertyInfo propDoc = t.GetProperty("Document");
        if (propDoc != null)
        {
            Console.WriteLine("WebFormPart.Document Type: " + propDoc.PropertyType.FullName);
        }
        
        PropertyInfo propContent = t.GetProperty("Content");
        if (propContent != null)
        {
            Console.WriteLine("WebFormPart.Content Type: " + propContent.PropertyType.FullName);
        }
    }
}
