using System;
using Artech.Genexus.Common.Parts.WebForm;

public class ListWebTagType
{
    public static void Main()
    {
        foreach (var value in Enum.GetValues(typeof(WebTagType)))
        {
            Console.WriteLine(string.Format("{0}: {1}", (int)value, value));
        }
    }
}
