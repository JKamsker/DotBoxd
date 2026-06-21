namespace DotBoxD.Architecture.Tests;

/// <summary>
/// Naming and content conventions across the shipping surface that are easy to regress and that no
/// analyzer enforces here: no console I/O in library code, exception-type naming, and the interface
/// 'I' prefix.
/// </summary>
public sealed class ConventionTests
{
    private static readonly string[] ForbiddenConsoleCalls =
        ["Console.Write", "Console.Error", "Console.Out", "Console.Read", "Console.Beep"];

    [Fact]
    public void Shipping_libraries_do_not_write_to_the_console()
    {
        var root = ArchTestSupport.RepositoryRoot();
        var srcRoot = Path.Combine(root, "src");
        var sep = Path.DirectorySeparatorChar;
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{sep}obj{sep}", StringComparison.Ordinal) ||
                file.Contains($"{sep}bin{sep}", StringComparison.Ordinal) ||
                file.EndsWith(".g.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (ForbiddenConsoleCalls.Any(call => text.Contains(call, StringComparison.Ordinal)))
            {
                offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Library code must not perform console I/O. Offending files:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Public_exception_types_use_the_Exception_suffix()
    {
        var offenders = ExportedTypes()
            .Where(t => typeof(Exception).IsAssignableFrom(t) && !t.Name.EndsWith("Exception", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Exception types must end in 'Exception':\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Public_interfaces_use_the_I_prefix()
    {
        var offenders = ExportedTypes()
            .Where(t => t.IsInterface)
            .Where(t => t.Name.Length < 2 || t.Name[0] != 'I' || !char.IsUpper(t.Name[1]))
            .Select(t => t.FullName!)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Interfaces must use the 'I' prefix:\n" + string.Join("\n", offenders));
    }

    private static IEnumerable<Type> ExportedTypes()
        => ArchTestSupport.ShippingAssemblies().SelectMany(a => a.GetExportedTypes());
}
