using System.Reflection;
using System.Reflection.Emit;
using DotBoxd.Kernels.Verifier;

namespace DotBoxd.Kernels.Tests;

public sealed class VerifierFieldTests
{
    [Theory]
    [MemberData(nameof(FieldAssemblies))]
    public async Task Verifier_rejects_generated_fields(Func<byte[]> build)
    {
        var result = await VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-FIELD");
    }

    public static TheoryData<Func<byte[]>> FieldAssemblies()
        => new() {
            StaticReadonlyFieldAssembly,
            InstanceFieldAssembly
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

    private static byte[] StaticReadonlyFieldAssembly()
        => BuildAssembly(type =>
        {
            type.DefineField("Cached", typeof(int), FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
            DefineVoidExecute(type);
        });

    private static byte[] InstanceFieldAssembly()
        => BuildAssembly(type =>
        {
            type.DefineField("State", typeof(int), FieldAttributes.Private);
            DefineVoidExecute(type);
        });

    private static void DefineVoidExecute(TypeBuilder type)
    {
        var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
        method.GetILGenerator().Emit(OpCodes.Ret);
    }

    private static byte[] BuildAssembly(Action<TypeBuilder> define)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Fielded" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("FieldedModule");
        var type = module.DefineType("Fielded.Module", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        define(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
