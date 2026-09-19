using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

// usage:
//   decompile <assembly> <typeNameSubstring> [memberNameSubstring]
//   decompile <assembly> --list <typeNameSubstring>

var assemblyPath = args[0];
var decompiler = new CSharpDecompiler(
    Path.GetFullPath(assemblyPath),
    new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });

var typeFilter = args[1];
var memberFilter = args.Length > 2 ? args[2] : null;

var types = decompiler.TypeSystem.MainModule.TypeDefinitions
    .Where(t => t.FullName.Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
    .ToList();

foreach (var type in types)
{
    if (memberFilter is "--list")
    {
        Console.WriteLine(type.FullName + "  ||  " + type.ReflectionName);
        continue;
    }

    Console.WriteLine("// ===== " + type.FullName + " =====");
    try
    {
        var text = decompiler.DecompileTypeAsString(new FullTypeName(type.ReflectionName));
        if (memberFilter is null)
        {
            Console.WriteLine(text);
        }
        else
        {
            // print only lines around the member, plus surrounding 40 lines
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(memberFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var start = Math.Max(0, i - 6);
                var end = Math.Min(lines.Length - 1, i + 45);
                Console.WriteLine($"// ---- match at line {i + 1} ----");
                for (var j = start; j <= end; j++)
                {
                    Console.WriteLine(lines[j]);
                }

                break;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("// failed: " + ex.Message);
    }
}
