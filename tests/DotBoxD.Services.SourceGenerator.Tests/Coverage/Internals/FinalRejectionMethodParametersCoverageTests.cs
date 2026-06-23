using System.Collections.Immutable;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

public sealed class FinalRejectionMethodParametersCoverageTests
{
    private static ParameterModel Normal(string name, string type) =>
        new(name, type, type);

    private static ParameterModel CancellationToken(string name) =>
        new(
            name,
            "global::System.Threading.CancellationToken",
            "global::System.Threading.CancellationToken",
            IsCancellationToken: true,
            HasDefaultValue: true);

    private static MethodModel Method(
        MethodReturnKind returnKind,
        bool hasCancellationToken,
        params ParameterModel[] parameters) =>
        new(
            Name: "Do",
            ExplicitImplementationType: "global::Test.IFoo",
            RpcName: "Do",
            ReturnKind: returnKind,
            DeclaredReturnType: "void",
            UnwrappedReturnType: null,
            ReturnRefKindKeyword: "",
            HasCancellationToken: hasCancellationToken,
            Parameters: new EquatableArray<ParameterModel>(ImmutableArray.Create(parameters)),
            AdditionalExplicitImplementationTypes: EquatableArray<string>.Empty);

    [Fact]
    public void Build_AsyncWithCancellationToken_ReturnsParametersUnchanged()
    {
        var original = Method(
            MethodReturnKind.TaskOf,
            hasCancellationToken: true,
            Normal("value", "int"),
            CancellationToken("token"));

        var result = FinalRejectionMethodParameters.Build(original, System.Threading.CancellationToken.None);

        Assert.True(original.Parameters.Equals(result));
        var names = result.ToArray().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "value", "token" }, names);
    }

    [Fact]
    public void Build_SyncMethod_FiltersExistingTokenAndAppendsSynthesizedCt()
    {
        var method = Method(
            MethodReturnKind.Sync,
            hasCancellationToken: true,
            Normal("count", "int"),
            CancellationToken("userToken"));

        var result = FinalRejectionMethodParameters.Build(method, System.Threading.CancellationToken.None);

        var parameters = result.ToArray();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("count", parameters[0].Name);
        Assert.False(parameters[0].IsCancellationToken);

        var synthesized = parameters[1];
        Assert.Equal("ct", synthesized.Name);
        Assert.True(synthesized.IsCancellationToken);
        Assert.True(synthesized.HasDefaultValue);
        Assert.Equal("global::System.Threading.CancellationToken", synthesized.Type);
        Assert.Equal("global::System.Threading.CancellationToken", synthesized.SignatureType);
        Assert.DoesNotContain(parameters, p => p.Name == "userToken");
    }

    [Fact]
    public void Build_AsyncWithoutCancellationToken_AppendsSynthesizedCt()
    {
        var method = Method(
            MethodReturnKind.Task,
            hasCancellationToken: false,
            Normal("name", "string"));

        var result = FinalRejectionMethodParameters.Build(method, System.Threading.CancellationToken.None);

        var parameters = result.ToArray();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("name", parameters[0].Name);
        Assert.Equal("ct", parameters[1].Name);
        Assert.True(parameters[1].IsCancellationToken);
    }

    [Fact]
    public void Build_SyncWithNoParameters_ProducesSingleSynthesizedCt()
    {
        var method = Method(MethodReturnKind.Void, hasCancellationToken: false);

        var result = FinalRejectionMethodParameters.Build(method, System.Threading.CancellationToken.None);

        var parameters = result.ToArray();
        Assert.Single(parameters);
        Assert.Equal("ct", parameters[0].Name);
        Assert.True(parameters[0].IsCancellationToken);
    }
}
