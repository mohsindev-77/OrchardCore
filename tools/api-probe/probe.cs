#:package System.Reflection.MetadataLoadContext@10.0.0
// This tool carries its own package version rather than joining the solution's central
// package management, so that a tool-only dependency never reaches a product assembly.
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property NoWarn=IL2026;IL2055;IL2057;IL2070;IL2072;IL2075;IL2090;IL3000

// WorkMate API probe.
//
// CLAUDE.md requires every session to verify the Orchard Core APIs it intends to use
// against the version pinned in Directory.Packages.props, rather than relying on memory
// of an API surface that moves between releases. This tool does that verification by
// reading the pinned assemblies' metadata directly out of the local NuGet cache.
//
// It loads metadata only. It never executes Orchard code, and it is not referenced by any
// product assembly: it is a file-based app with no project file, so the solution does not
// build it.
//
// See README.md in this folder for usage.

using System.Reflection;

var repoRoot = FindRepositoryRoot();
var pinnedVersion = ReadPinnedOrchardCoreVersion(Path.Combine(repoRoot, "Directory.Packages.props"));

var nugetCache = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

// Assemblies the probe reports on: the pinned Orchard Core packages, and nothing else.
var orchardAssemblies = new List<string>();

// Everything the probe can resolve a type reference against, Orchard first so that it wins
// any name collision with an older copy sitting in the cache.
var resolvable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

void Offer(string path, bool isOrchard)
{
    var name = Path.GetFileNameWithoutExtension(path);
    if (!resolvable.ContainsKey(name))
    {
        resolvable[name] = path;
    }

    if (isOrchard && !orchardAssemblies.Contains(path))
    {
        orchardAssemblies.Add(path);
    }
}

foreach (var package in Directory.GetDirectories(nugetCache))
{
    if (!Path.GetFileName(package).StartsWith("orchardcore", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    var lib = Path.Combine(package, pinnedVersion, "lib");
    if (!Directory.Exists(lib))
    {
        continue;
    }

    foreach (var targetFramework in Directory.GetDirectories(lib))
    {
        foreach (var assembly in Directory.GetFiles(targetFramework, "*.dll"))
        {
            Offer(assembly, isOrchard: true);
        }
    }
}

if (orchardAssemblies.Count == 0)
{
    Console.Error.WriteLine($"No Orchard Core {pinnedVersion} assemblies found under {nugetCache}.");
    Console.Error.WriteLine("Run 'dotnet restore' first so the pinned packages are in the cache.");
    return 1;
}

foreach (var assembly in Directory.GetFiles(runtimeDirectory, "*.dll"))
{
    Offer(assembly, isOrchard: false);
}

// The shared frameworks, so that types deriving from ASP.NET Core types resolve.
var sharedFrameworks = Path.GetDirectoryName(Path.GetDirectoryName(runtimeDirectory))!;
foreach (var framework in new[] { "Microsoft.AspNetCore.App" })
{
    var directory = Path.Combine(sharedFrameworks, framework);
    if (!Directory.Exists(directory))
    {
        continue;
    }

    foreach (var version in Directory.GetDirectories(directory).OrderByDescending(x => x))
    {
        foreach (var assembly in Directory.GetFiles(version, "*.dll"))
        {
            Offer(assembly, isOrchard: false);
        }
    }
}

// The rest of the cache, newest version first, purely as reference closure.
foreach (var package in Directory.GetDirectories(nugetCache))
{
    foreach (var version in Directory.GetDirectories(package).OrderByDescending(x => x))
    {
        var lib = Path.Combine(version, "lib");
        if (!Directory.Exists(lib))
        {
            continue;
        }

        foreach (var targetFramework in Directory.GetDirectories(lib).OrderByDescending(x => x))
        {
            foreach (var assembly in Directory.GetFiles(targetFramework, "*.dll"))
            {
                Offer(assembly, isOrchard: false);
            }
        }
    }
}

using var context = new MetadataLoadContext(new PathAssemblyResolver(resolvable.Values), "System.Private.CoreLib");

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run probe.cs <query> [query ...]");
    Console.Error.WriteLine("       dotnet run probe.cs --features");
    Console.Error.WriteLine("See README.md for the query forms.");
    return 1;
}

Console.WriteLine($"# Orchard Core {pinnedVersion}, {orchardAssemblies.Count} assemblies");

if (args[0] == "--features")
{
    ListFeatures();
    return 0;
}

foreach (var query in args)
{
    Console.WriteLine($"=========== {query}");
    var found = false;

    foreach (var path in orchardAssemblies)
    {
        Assembly assembly;
        try
        {
            assembly = context.LoadFromAssemblyPath(path);
        }
        catch
        {
            continue;
        }

        foreach (var type in SafeGetTypes(assembly))
        {
            if (type.FullName is null)
            {
                continue;
            }

            if (query.StartsWith('='))
            {
                found |= DescribeMatchingMethods(type, query[1..], path);
                continue;
            }

            if (query.StartsWith('~'))
            {
                if (type.FullName.Contains(query[1..], StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    Console.WriteLine($"   {type.FullName}  [{Path.GetFileNameWithoutExtension(path)}]");
                }

                continue;
            }

            var matches = query.Contains('.')
                ? type.FullName.Equals(query, StringComparison.OrdinalIgnoreCase)
                : type.Name.Equals(query, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                found = true;
                DescribeType(type, path);
            }
        }
    }

    if (!found)
    {
        Console.WriteLine("   NOT FOUND");
    }
}

return 0;

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    // Run from the tools folder rather than from a build output.
    directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find Directory.Packages.props above the current directory.");
}

static string ReadPinnedOrchardCoreVersion(string packagesPropsPath)
{
    var text = File.ReadAllText(packagesPropsPath);
    var match = System.Text.RegularExpressions.Regex.Match(
        text,
        @"<OrchardCoreVersion>\s*([^<\s]+)\s*</OrchardCoreVersion>");

    return match.Success
        ? match.Groups[1].Value
        : throw new InvalidOperationException($"No OrchardCoreVersion property in {packagesPropsPath}.");
}

static IEnumerable<Type> SafeGetTypes(Assembly assembly)
{
    try
    {
        return assembly.GetTypes();
    }
    catch (ReflectionTypeLoadException exception)
    {
        return exception.Types.Where(type => type is not null)!;
    }
}

static void DescribeType(Type type, string path)
{
    Console.WriteLine($"-- {type.FullName}  [{Path.GetFileNameWithoutExtension(path)}]");

    try
    {
        var kind = type switch
        {
            { IsInterface: true } => "interface",
            { IsEnum: true } => "enum",
            { IsAbstract: true, IsSealed: true } => "static class",
            { IsAbstract: true } => "abstract class",
            { IsValueType: true } => "struct",
            _ => "class",
        };

        var baseType = type.BaseType is { Name: not "Object" } b ? $" : {b.Name}" : string.Empty;
        var interfaces = type.GetInterfaces();
        var implemented = interfaces.Length > 0
            ? " impl " + string.Join(", ", interfaces.Select(i => i.Name))
            : string.Empty;

        Console.WriteLine($"   kind: {kind}{baseType}{implemented}");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"   kind: unresolved ({exception.Message})");
    }

    const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    foreach (var constructor in type.GetConstructors())
    {
        Write(() => $"   .ctor({Parameters(constructor)})", ".ctor");
    }

    foreach (var method in type.GetMethods(Declared))
    {
        if (method.IsSpecialName || method.IsPrivate)
        {
            continue;
        }

        Write(
            () => "   "
                + (method.IsFamily ? "protected " : string.Empty)
                + (method.IsStatic ? "static " : string.Empty)
                + (method.IsAbstract ? "abstract " : string.Empty)
                + $"{method.ReturnType.Name} {method.Name}({Parameters(method)})",
            method.Name);
    }

    foreach (var property in type.GetProperties(Declared))
    {
        Write(
            () => $"   prop {property.PropertyType.Name} {property.Name} "
                + $"{{{(property.CanRead ? " get;" : string.Empty)}{(property.CanWrite ? " set;" : string.Empty)} }}",
            property.Name);
    }

    foreach (var field in type.GetFields(Declared))
    {
        if (field.IsPrivate)
        {
            continue;
        }

        Write(() => $"   field {field.FieldType.Name} {field.Name}", field.Name);
    }

    static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));

    static void Write(Func<string> line, string fallbackName)
    {
        try
        {
            Console.WriteLine(line());
        }
        catch
        {
            Console.WriteLine($"   {fallbackName}: unresolved");
        }
    }
}

static bool DescribeMatchingMethods(Type type, string methodNameFragment, string path)
{
    MethodInfo[] methods;

    try
    {
        methods = type.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
    }
    catch
    {
        return false;
    }

    var found = false;

    foreach (var method in methods)
    {
        if (!method.Name.Contains(methodNameFragment, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        found = true;

        try
        {
            var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine(
                $"   {type.FullName}.{method.Name}({parameters}) -> {method.ReturnType.Name}"
                + $"  [{Path.GetFileNameWithoutExtension(path)}]");
        }
        catch
        {
            Console.WriteLine($"   {type.FullName}.{method.Name}(unresolved)");
        }
    }

    return found;
}

void ListFeatures()
{
    foreach (var path in orchardAssemblies)
    {
        Assembly assembly;

        try
        {
            assembly = context.LoadFromAssemblyPath(path);
        }
        catch
        {
            continue;
        }

        foreach (var attribute in assembly.GetCustomAttributesData())
        {
            string attributeName;

            try
            {
                attributeName = attribute.AttributeType.Name;
            }
            catch
            {
                continue;
            }

            if (attributeName is not ("ModuleAttribute" or "FeatureAttribute"))
            {
                continue;
            }

            var id = Named(attribute, "Id") as string ?? Path.GetFileNameWithoutExtension(path);
            var name = Named(attribute, "Name") as string;
            var dependencies = Named(attribute, "Dependencies") is IEnumerable<CustomAttributeTypedArgument> values
                ? string.Join(",", values.Select(v => v.Value as string))
                : string.Empty;

            Console.WriteLine($"{attributeName[..^9],-7} id={id}  name={name}  deps=[{dependencies}]");
        }
    }

    static object? Named(CustomAttributeData attribute, string memberName) =>
        attribute.NamedArguments.FirstOrDefault(a => a.MemberName == memberName).TypedValue.Value;
}
