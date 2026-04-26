using System;
using System.Reflection;
using nkast.Aether.Physics2D.Common;

class Program
{
    static void Main()
    {
        var c = new ChainShape();
        Console.WriteLine("Type: " + c.GetType().FullName);
        
        Console.WriteLine("\n--- All methods ---");
        foreach (var m in c.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Console.WriteLine(m.Name);
        }

        Console.WriteLine("\n--- All properties ---");
        foreach (var p in c.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Console.WriteLine(p.Name + " : " + p.PropertyType.Name);
        }

        // Check the extension method
        Console.WriteLine("\n--- BodyExtensions methods ---");
        var beType = Type.GetType("nkast.Aether.Physics2D.Dynamics.BodyExtensions, Aether.Physics2D");
        if (beType != null)
        {
            foreach (var m in beType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                Console.WriteLine(m.Name);
            }
        }
        else
        {
            Console.WriteLine("BodyExtensions type not found");
        }
    }
}
