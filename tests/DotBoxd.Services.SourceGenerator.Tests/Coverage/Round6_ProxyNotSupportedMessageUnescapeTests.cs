using System;
using System.Linq;
using DotBoxd.Services.SourceGenerator.Tests;
using Xunit;

namespace DotBoxd.Services.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 6 regression for <c>ProxyGenerator.GenerateProxyMethod</c>. The runtime
/// <c>NotSupportedException</c> emitted for an unmarshalable method embedded <c>method.Name</c> — the
/// already-escaped C# identifier — directly into the message, so a keyword-named method produced
/// <c>"DotBoxd cannot marshal '@event': ..."</c> instead of the human-readable <c>'event'</c>. The message
/// must use <c>IdentifierHelpers.UnescapeIdentifier</c>.
/// </summary>
public sealed class Round6_ProxyNotSupportedMessageUnescapeTests
{
    private const string Source = @"
using DotBoxd.Services.Attributes;

namespace Bug.KeywordOutParam
{
    [DotBoxdService]
    public interface IKw
    {
        void @event(out int x);
    }
}";

    [Fact]
    public void Generator_UnescapesKeywordMethodName_InNotSupportedExceptionMessage()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        var proxy = runResult.GeneratedTrees
            .First(t => t.FilePath.Contains("DotBoxdRpcProxy"))
            .GetText()
            .ToString();

        Assert.Contains("cannot marshal 'event'", proxy);
        Assert.DoesNotContain("cannot marshal '@event'", proxy);
    }
}
