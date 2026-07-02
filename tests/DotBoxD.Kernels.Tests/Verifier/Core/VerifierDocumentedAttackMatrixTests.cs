using DotBoxD.Kernels.Tests.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core;

public sealed class VerifierDocumentedAttackMatrixTests
{
    public static TheoryData<string, Func<byte[]>, string[], Func<string, bool>?> DocumentedAttackCases()
        => new() {
            { "exception handlers", VerifierDocumentedAttackAssemblies.ExceptionHandler, ["V-EXCEPTION"], null },
            { "embedded resources", VerifierDocumentedAttackAssemblies.EmbeddedResource, ["V-RESOURCE"], null },
            { "Thread.Start", VerifierDocumentedAttackAssemblies.ThreadStart, ["V-TYPE-FORBIDDEN", "V-MEMBER"], null },
            { "raw Stream", VerifierDocumentedAttackAssemblies.RawStream, ["V-TYPE-FORBIDDEN", "V-MEMBER"], null },
            {
                "IServiceProvider.GetService",
                VerifierDocumentedAttackAssemblies.ServiceProvider,
                ["V-TYPE-FORBIDDEN", "V-MEMBER"],
                null
            },
            {
                "unmanaged function pointer signature",
                VerifierDocumentedAttackAssemblies.FunctionPointerSignature,
                ["V-FUNCTION-SIGNATURE"],
                null
            },
            {
                "unmanaged pointer local signature",
                VerifierDocumentedAttackAssemblies.PointerLocalSignature,
                ["V-FUNCTION-SIGNATURE"],
                null
            },
            {
                "unmetered type array allocation",
                VerifierDocumentedAttackAssemblies.TypeArrayAllocation,
                ["V-COMPILED-SHAPE", "V-MEMBER"],
                static message => message.Contains("CreateTypeArray", StringComparison.Ordinal) ||
                                  message.Contains("type array", StringComparison.OrdinalIgnoreCase)
            }
        };

    [Theory]
    [MemberData(nameof(DocumentedAttackCases))]
    public async Task Verifier_rejects_documented_boundary_attacks(
        string name,
        Func<byte[]> build,
        string[] expectedCodes,
        Func<string, bool>? messagePredicate)
    {
        var result = await VerifierTestHelpers.VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            expectedCodes.Contains(d.Code) &&
            (messagePredicate is null || messagePredicate(d.Message)));
        Assert.NotEmpty(name);
    }

    [Fact]
    public async Task Verifier_rejects_initlocals_disabled_with_unassigned_local_read()
    {
        var result = await VerifierTestHelpers.VerifyAsync(VerifierDocumentedAttackAssemblies.InitLocalsDisabled());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-COMPILED-SHAPE" &&
            d.Message.Contains("local", StringComparison.OrdinalIgnoreCase) &&
            (d.Message.Contains("initlocals", StringComparison.OrdinalIgnoreCase) ||
                d.Message.Contains("initializ", StringComparison.OrdinalIgnoreCase)));
    }
}
