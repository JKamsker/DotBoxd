using System.Reflection;
using System.Reflection.Emit;
using DotBoxd.Kernels.Verifier;

namespace DotBoxd.Kernels.Tests;

public sealed class VerifierCustomAttributeTests
{
    [Theory]
    [MemberData(nameof(CustomAttributeAssemblies))]
    public async Task Verifier_rejects_custom_attributes(Func<byte[]> build)
    {
        var result = await VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-CUSTOM-ATTR");
    }

    public static TheoryData<Func<byte[]>> CustomAttributeAssemblies()
        => new() {
            TypeObsoleteAttributeAssembly,
            MethodObsoleteAttributeAssembly
        };

    private static async ValueTask<VerificationResult> VerifyAsync(byte[] bytes)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var manifest = new ArtifactManifest(
            1,
            "test",
            "module",
            "plan",
            "policy",
            "bindings",
            "runtime",
            "compiler",
            "type-system",
            "effect-analysis",
            "verifier",
            "1.0.0",
            "net10.0",
            [],
            hash,
            DateTimeOffset.UtcNow);

        return await new GeneratedAssemblyVerifier()
            .VerifyAsync(bytes, manifest, VerificationPolicy.BoxedValueDefaults(), CancellationToken.None);
    }

    private static byte[] TypeObsoleteAttributeAssembly()
        => BuildAssembly(type =>
        {
            var ctor = typeof(ObsoleteAttribute).GetConstructor([typeof(string)])!;
            type.SetCustomAttribute(new CustomAttributeBuilder(ctor, ["blocked"]));
            DefineVoidExecute(type);
        });

    private static byte[] MethodObsoleteAttributeAssembly()
        => BuildAssembly(type =>
        {
            var method = DefineVoidExecute(type);
            var ctor = typeof(ObsoleteAttribute).GetConstructor([typeof(string)])!;
            method.SetCustomAttribute(new CustomAttributeBuilder(ctor, ["blocked"]));
        });

    private static MethodBuilder DefineVoidExecute(TypeBuilder type)
    {
        var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return method;
    }

    private static byte[] BuildAssembly(Action<TypeBuilder> define)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Attributed" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("AttributedModule");
        var type = module.DefineType("Attributed.Module", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        define(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
