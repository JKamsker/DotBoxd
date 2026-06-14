using System.Reflection;
using System.Reflection.Emit;

namespace DotBoxd.Kernels.Tests;

public sealed class VerifierGenericShapeTests
{
    [Fact]
    public async Task Verifier_rejects_generic_generated_types()
    {
        var result = await VerifierTestHelpers.VerifyAsync(
            VerifierTestHelpers.BuildGeneratedAssembly(type => {
                type.DefineGenericParameters("TValue");
                VerifierTestHelpers.DefineValidExecute(type);
            }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-GENERIC");
    }

    [Fact]
    public async Task Verifier_rejects_generic_generated_methods()
    {
        var result = await VerifierTestHelpers.VerifyAsync(
            VerifierTestHelpers.BuildGeneratedAssembly(type => {
                VerifierTestHelpers.DefineValidExecute(type);
                var method = type.DefineMethod(
                    "Fn_0",
                    MethodAttributes.Private | MethodAttributes.Static,
                    typeof(SandboxValue),
                    [typeof(SandboxContext)]);
                method.DefineGenericParameters("TValue");

                var il = method.GetILGenerator();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-GENERIC");
    }
}
