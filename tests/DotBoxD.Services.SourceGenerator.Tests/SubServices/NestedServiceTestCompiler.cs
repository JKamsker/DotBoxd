using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

internal static class NestedServiceTestCompiler
{
    public static (Assembly Assembly, GeneratorDriverRunResult RunResult) Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var final = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException("Emit failed: " + errors);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("Nested_" + Guid.NewGuid(), isCollectible: false);
        return (alc.LoadFromStream(ms), runResult);
    }
}

internal static class SubImplFactory
{
    public static object Create(Type subIface, int fixedCount = 0)
    {
        var openGeneric = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
        var closed = openGeneric.MakeGenericMethod(subIface, typeof(SubStub));
        var proxy = closed.Invoke(null, null)!;
        ((SubStub)proxy).Count = fixedCount;
        return proxy;
    }
}

public class SubStub : DispatchProxy
{
    public int Count;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "CountAsync")
        {
            return Task.FromResult(Count);
        }

        if (targetMethod?.Name == "SumAsync")
        {
            return Task.FromResult((int)args![0]! + (int)args[1]!);
        }

        throw new InvalidOperationException("unexpected " + targetMethod?.Name);
    }
}

internal static class RootImplFactory
{
    public static object Create(Type rootIface, Func<string, object> mintSub)
    {
        var openGeneric = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
        var closed = openGeneric.MakeGenericMethod(rootIface, typeof(RootStub));
        var proxy = closed.Invoke(null, null)!;
        ((RootStub)proxy).Mint = mintSub;
        return proxy;
    }
}

public class RootStub : DispatchProxy
{
    public Func<string, object>? Mint;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "GetSubAsync")
        {
            var sub = Mint!((string)args![0]!);
            var subInterface = targetMethod.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(subInterface)
                .Invoke(null, new[] { sub });
        }

        throw new InvalidOperationException("unexpected " + targetMethod?.Name);
    }
}
