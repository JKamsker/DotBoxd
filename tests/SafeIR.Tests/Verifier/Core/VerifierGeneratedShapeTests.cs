using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class VerifierGeneratedShapeTests
{
    [Theory]
    [InlineData("Malicious.Module_0123456789abcdef")]
    [InlineData("SafeIR.Generated.NotModule_0123456789abcdef")]
    [InlineData("SafeIR.Generated.Module_nothex")]
    public async Task Verifier_rejects_unexpected_generated_type_names(string typeName)
    {
        var result = await VerifyAsync(AssemblyWithType(typeName));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-PUBLIC-SURFACE" &&
            d.Message.Contains("SafeIR.Generated.Module_<16-hex-hash>", StringComparison.Ordinal));
    }

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

    private static byte[] AssemblyWithType(string typeName)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Named" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("NamedModule");
        var type = module.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var execute = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
        execute.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
