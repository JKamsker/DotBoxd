using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Model;

public sealed class EntrypointBinderTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void BindArguments_reuses_empty_array_for_zero_parameter_entrypoints()
    {
        var arguments = EntrypointBinder.BindArguments(ZeroParameterFunction(), SandboxValue.Unit);

        Assert.Same(Array.Empty<SandboxValue>(), arguments);
    }

    private static SandboxFunction ZeroParameterFunction()
        => new(
            "main",
            IsEntrypoint: true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)]);
}
