using System;
using System.IO;
using System.Text;

class TestEncoding {
    static void Main() {
        Console.OutputEncoding = Encoding.UTF8;
        string text = "comentário início";
        Console.WriteLine("Direct UTF-16 string to Console.Out (which is UTF-8):");
        Console.WriteLine(text);
        
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(text);
        Console.WriteLine("UTF-8 Bytes converted back to string:");
        Console.WriteLine(Encoding.UTF8.GetString(utf8Bytes));
    }
}
