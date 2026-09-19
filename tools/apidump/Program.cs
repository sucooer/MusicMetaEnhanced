using System.Reflection;

// Usage:
//   apidump <assemblyPath> <typeNameSubstring> [memberFilterSubstring]
//
// Loads an assembly (plus its directory as a resolver) with MetadataLoadContext and
// prints type declarations: base type, interfaces, ctors, properties, methods.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: apidump <assemblyPath> <typeNameSubstring> [memberFilter]");
    return 1;
}

var assemblyPath = args[0];
var typeFilter = args[1];
var memberFilter = args.Length > 2 ? args[2] : null;

var dir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
var runtimeDir = Environment.GetEnvironmentVariable("DOTNET_ROOT_REF") ?? Path.GetDirectoryName(typeof(object).Assembly.Location) ?? ".";

var paths = new List<string>(Directory.GetFiles(runtimeDir, "*.dll"));
var seen = new HashSet<string>(paths.Select(p => Path.GetFileName(p)), StringComparer.OrdinalIgnoreCase);

foreach (var extra in (Environment.GetEnvironmentVariable("REF_DIRS") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
{
    foreach (var p in Directory.GetFiles(extra, "*.dll"))
    {
        if (seen.Add(Path.GetFileName(p)))
        {
            paths.Add(p);
        }
    }
}

if (dir is not null)
{
    foreach (var p in Directory.GetFiles(dir, "*.dll"))
    {
        if (seen.Add(Path.GetFileName(p)))
        {
            paths.Add(p);
        }
    }
}

var resolver = new PathAssemblyResolver(paths);
using var mlc = new MetadataLoadContext(resolver);

var asm = mlc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

foreach (var type in asm.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
{
    if (type.FullName is null || !type.FullName.Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var kind = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";
    var bases = new List<string>();
    if (type.BaseType is not null && type.BaseType.FullName != "System.Object" && type.BaseType.FullName != "System.ValueType")
    {
        bases.Add(Short(type.BaseType));
    }

    foreach (var i in type.GetInterfaces())
    {
        bases.Add(Short(i));
    }

    Console.WriteLine();
    Console.WriteLine($"{kind} {type.FullName}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty)}");

    foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
    {
        if (c.IsPrivate)
        {
            continue;
        }

        Console.WriteLine($"    .ctor({Params(c)})");
    }

    foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
    {
        if (memberFilter is not null && !p.Name.Contains(memberFilter, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        Console.WriteLine($"    prop {Short(p.PropertyType)} {p.Name} {{ {(p.CanRead ? "get; " : string.Empty)}{(p.CanWrite ? "set; " : string.Empty)}}}");
    }

    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
    {
        if (m.IsSpecialName || memberFilter is not null && !m.Name.Contains(memberFilter, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        Console.WriteLine($"    {Short(m.ReturnType)} {m.Name}({Params(m)})");
    }
}

return 0;

static string Params(MethodBase m)
{
    return string.Join(", ", m.GetParameters().Select(p => $"{Short(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = ?" : string.Empty)}"));
}

static string Short(Type t)
{
    if (t is null)
    {
        return "?";
    }

    if (t.IsGenericType)
    {
        var name = t.Name;
        var tick = name.IndexOf('`');
        if (tick > 0)
        {
            name = name[..tick];
        }

        return $"{name}<{string.Join(", ", t.GetGenericArguments().Select(Short))}>";
    }

    return t.Name;
}
