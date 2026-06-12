using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SafeIR.Tests;

public sealed class VerifierDocumentedAttackMatrixTests
{
    public static TheoryData<string, Func<byte[]>, string[]> DocumentedAttackCases()
        => new() {
            { "exception handlers", ExceptionHandlerAssembly, ["V-EXCEPTION"] },
            { "embedded resources", EmbeddedResourceAssembly, ["V-RESOURCE"] },
            { "Thread.Start", ThreadStartAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "raw Stream", StreamAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "IServiceProvider.GetService", ServiceProviderAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "unmanaged function pointer signature", FunctionPointerSignatureAssembly, ["V-FUNCTION-SIGNATURE"] }
        };

    [Theory]
    [MemberData(nameof(DocumentedAttackCases))]
    public async Task Verifier_rejects_documented_boundary_attacks(
        string name,
        Func<byte[]> build,
        string[] expectedCodes)
    {
        var result = await VerifierTestHelpers.VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => expectedCodes.Contains(d.Code));
        Assert.NotEmpty(name);
    }

    private static byte[] ExceptionHandlerAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var method = DefineExecute(type);
            var local = method.GetILGenerator().DeclareLocal(typeof(SandboxValue));
            var il = method.GetILGenerator();
            var end = il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Leave_S, end);
            il.BeginCatchBlock(typeof(Exception));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Leave_S, end);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] EmbeddedResourceAssembly()
    {
        const string source = """
            namespace SafeIR.Generated;

            public static class Module_0123456789abcdef
            {
                public static SafeIR.SandboxValue Execute(
                    SafeIR.SandboxContext context,
                    SafeIR.SandboxValue input) => input;
            }
            """;
        using var output = new MemoryStream();
        var compilation = CSharpCompilation.Create(
            "ResourceAttack",
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences().Append(MetadataReference.CreateFromFile(typeof(SandboxValue).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var resource = new ResourceDescription(
            "payload.bin",
            () => new MemoryStream([1, 2, 3], writable: false),
            isPublic: true);
        var result = compilation.Emit(output, manifestResources: [resource]);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return output.ToArray();
    }

    private static byte[] ThreadStartAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, typeof(Thread).GetMethod(nameof(Thread.Start), Type.EmptyTypes)!);
            ReturnInput(il);
        });

    private static byte[] StreamAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, typeof(Stream).GetMethod(nameof(Stream.Synchronized), [typeof(Stream)])!);
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static byte[] ServiceProviderAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService))!);
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static byte[] FunctionPointerSignatureAssembly()
    {
        const string source = """
            using System.Runtime.CompilerServices;

            namespace SafeIR.Generated;

            public static unsafe class Module_0123456789abcdef
            {
                public static SafeIR.SandboxValue Execute(
                    SafeIR.SandboxContext context,
                    SafeIR.SandboxValue input) => input;

                private static SafeIR.SandboxValue Fn_0(
                    SafeIR.SandboxContext context,
                    delegate* unmanaged[Cdecl]<void> callback) => null!;
            }
            """;
        using var output = new MemoryStream();
        var compilation = CSharpCompilation.Create(
            "FunctionPointerAttack",
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            TrustedPlatformReferences().Append(MetadataReference.CreateFromFile(typeof(SandboxValue).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var result = compilation.Emit(output);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return output.ToArray();
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return MetadataReference.CreateFromFile(path);
        }
    }

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static void ReturnInput(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }
}
