using System.Reflection;
using System.Reflection.Emit;

namespace SafeIR.Tests;

public sealed class VerifierMetadataTableTests
{
    [Theory]
    [MemberData(nameof(ForbiddenMetadataAssemblies))]
    public async Task Verifier_rejects_forbidden_metadata_tables(Func<byte[]> build, string expectedCode)
    {
        var result = await VerifierTestHelpers.VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == expectedCode);
    }

    public static TheoryData<Func<byte[]>, string> ForbiddenMetadataAssemblies()
        => new() {
            { PropertyMetadataAssembly, "V-METADATA-SHAPE" },
            { EventMetadataAssembly, "V-METADATA-SHAPE" },
            { NestedTypeAssembly, "V-METADATA-SHAPE" },
            { GenericMethodSpecificationAssembly, "V-GENERIC" }
        };

    private static byte[] PropertyMetadataAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type => {
            VerifierTestHelpers.DefineValidExecute(type);
            type.DefineProperty("State", PropertyAttributes.None, typeof(int), []);
        });

    private static byte[] EventMetadataAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type => {
            VerifierTestHelpers.DefineValidExecute(type);
            type.DefineEvent("Changed", EventAttributes.None, typeof(Action));
        });

    private static byte[] NestedTypeAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type => {
            VerifierTestHelpers.DefineValidExecute(type);
            var nested = type.DefineNestedType(
                "Nested",
                TypeAttributes.NestedPrivate | TypeAttributes.Abstract | TypeAttributes.Sealed);
            nested.CreateType();
        });

    private static byte[] GenericMethodSpecificationAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var empty = typeof(Array)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(Array.Empty) && m.GetParameters().Length == 0)
                .MakeGenericMethod(typeof(SandboxValue));

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Call, empty);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
        });
}
