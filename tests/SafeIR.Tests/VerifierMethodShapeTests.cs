using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class VerifierMethodShapeTests
{
    [Theory]
    [InlineData("Helper")]
    [InlineData("Fn_alpha")]
    [InlineData("Fn_")]
    public async Task Verifier_rejects_unexpected_generated_method_names(string methodName)
    {
        var result = await VerifyAsync(AssemblyWithHelper(methodName));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-METHOD-NAME");
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
            "verifier",
            "1.0.0",
            "net10.0",
            [],
            hash,
            DateTimeOffset.UtcNow);

        return await new GeneratedAssemblyVerifier()
            .VerifyAsync(bytes, manifest, VerificationPolicy.BoxedValueDefaults(), CancellationToken.None);
    }

    private static byte[] AssemblyWithHelper(string methodName)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Methoded" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("MethodedModule");
        var type = module.DefineType(
            "SafeIR.Generated.Module_0123456789abcdef",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        DefineVoidMethod(type, "Execute", MethodAttributes.Public | MethodAttributes.Static);
        DefineVoidMethod(type, methodName, MethodAttributes.Private | MethodAttributes.Static);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static void DefineVoidMethod(TypeBuilder type, string name, MethodAttributes attributes)
    {
        var method = type.DefineMethod(name, attributes, typeof(void), []);
        method.GetILGenerator().Emit(OpCodes.Ret);
    }
}
