using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Kernels.Tests.Verifier.Generated;

public sealed class VerifierLiteralFactoryTests
{
    [Theory]
    [InlineData(".StringLiteralValue(")]
    [InlineData(".OpaqueIdLiteralValue(")]
    [InlineData(".GuidLiteralValue(")]
    [InlineData(".PathLiteralValue(")]
    [InlineData(".UriLiteralValue(")]
    [InlineData(".CreateLiteralValueArray(")]
    [InlineData(".ListLiteralValue(")]
    [InlineData(".MapLiteralValue(")]
    public void Default_policy_excludes_context_free_literal_factories(string memberName)
    {
        var policy = VerificationPolicy.BoxedValueDefaults();

        Assert.DoesNotContain(policy.AllowedMembers, member => member.Contains(memberName, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(UnbudgetedLiteralFactories))]
    public async Task Verifier_rejects_context_free_literal_factories(Func<byte[]> build)
    {
        var result = await VerifierTestHelpers.VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-MEMBER");
    }

    public static TheoryData<Func<byte[]>> UnbudgetedLiteralFactories()
        => new()
        {
            StringLiteralFactoryAssembly,
            LiteralArrayFactoryAssembly
        };

    private static byte[] StringLiteralFactoryAssembly()
        => BuildAssembly(type =>
        {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "unchecked");
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.StringLiteralValue))!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] LiteralArrayFactoryAssembly()
        => BuildAssembly(type =>
        {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue[]),
                []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, 1024);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.CreateLiteralValueArray))!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] BuildAssembly(Action<TypeBuilder> define)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Generated" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("GeneratedModule");
        var type = module.DefineType(
            "DotBoxD.Kernels.Generated.Module_0123456789abcdef",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        define(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
