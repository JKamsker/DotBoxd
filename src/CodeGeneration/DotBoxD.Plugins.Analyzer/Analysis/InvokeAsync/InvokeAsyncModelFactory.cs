using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DotBoxD.Plugins.Analyzer.Analysis.Rpc.DotBoxDRpcJsonLowerer;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncModelFactory
{
    private const string InvokeAsyncMethod = "InvokeAsync";

    public static InvokeAsyncResult? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        try
        {
            return TryCreate(invocation, context.SemanticModel, cancellationToken);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static InvokeAsyncResult? TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access ||
            !string.Equals(access.Name.Identifier.ValueText, InvokeAsyncMethod, StringComparison.Ordinal) ||
            !IsKernelInvocationSurface(model, access.Expression, cancellationToken) ||
            !TryLambda(invocation, out var lambda) ||
            lambda.Body is not BlockSyntax block ||
            !HasSupportedParameter(lambda, model, cancellationToken) ||
            HasCaptures(lambda, model))
        {
            return null;
        }

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol
            {
                TypeArguments.Length: 1
            } method)
        {
            return null;
        }

        var returnType = method.TypeArguments[0];
        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var lowerer = new DotBoxDRpcJsonLowerer(model, capabilities, effects, cancellationToken);
        var bodyJson = lowerer.LowerBody(block);
        effects.Add(DotBoxDGenerationNames.Effects.Cpu);
        if (lowerer.Allocates)
        {
            effects.Add(DotBoxDGenerationNames.Effects.Alloc);
        }

        var id = HookChainIdentity.Compute(invocation);
        var pluginId = "$anon:" + id;
        var packageName = "InvokeAsync_" + id + DotBoxDGenerationNames.PluginPackageSuffix;
        var ns = HookChainIdentity.Namespace(invocation);
        var package = EmitPackage(ns, packageName, pluginId, returnType, bodyJson, effects, capabilities);
        var interception = Interception(invocation, model, ns, packageName, pluginId, returnType, cancellationToken);
        return new InvokeAsyncResult(package, interception);
    }

    private static bool IsKernelInvocationSurface(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken)
        => string.Equals(
            model.GetTypeInfo(receiver, cancellationToken).Type?.ToDisplayString(),
            DotBoxDGenerationNames.Metadata.KernelInvocationSurfaceType,
            StringComparison.Ordinal);

    private static bool TryLambda(InvocationExpressionSyntax invocation, out LambdaExpressionSyntax lambda)
    {
        lambda = null!;
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 ||
            arguments[0].Expression is not LambdaExpressionSyntax lambdaExpression)
        {
            return false;
        }

        lambda = lambdaExpression;
        return true;
    }

    private static bool HasSupportedParameter(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda is not ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized)
        {
            return false;
        }

        var parameter = parenthesized.ParameterList.Parameters[0];
        return parameter.Type is not null &&
               string.Equals(
                   model.GetTypeInfo(parameter.Type, cancellationToken).Type?.ToDisplayString(),
                   DotBoxDGenerationNames.Metadata.GameWorldAccessType,
                   StringComparison.Ordinal);
    }

    private static bool HasCaptures(LambdaExpressionSyntax lambda, SemanticModel model)
    {
        if (lambda.Body is not BlockSyntax block ||
            lambda is not ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized)
        {
            return true;
        }

        var lambdaParameter = model.GetDeclaredSymbol(parenthesized.ParameterList.Parameters[0]);
        var flow = model.AnalyzeDataFlow(block);
        if (!flow.Succeeded)
        {
            return true;
        }

        foreach (var symbol in flow.DataFlowsIn)
        {
            if (!SymbolEqualityComparer.Default.Equals(symbol, lambdaParameter))
            {
                return true;
            }
        }

        return false;
    }

    private static GeneratedPluginPackage EmitPackage(
        string ns,
        string packageName,
        string pluginId,
        ITypeSymbol returnType,
        string bodyJson,
        IEnumerable<string> effects,
        IEnumerable<string> capabilities)
    {
        var json =
            "{" +
            "\"manifest\":{" +
            $"\"pluginId\":{Str(pluginId)}," +
            "\"contract\":\"AnonymousInvokeAsync\"," +
            "\"mode\":\"Auto\"," +
            $"\"effects\":[{JoinStrings(effects)}]," +
            "\"liveSettings\":[]," +
            "\"subscriptions\":[]," +
            $"\"requiredCapabilities\":[{JoinStrings(capabilities)}]," +
            "\"rpcEntrypoint\":\"Invoke\"}," +
            "\"entrypoints\":{\"shouldHandle\":\"Invoke\",\"handle\":\"Invoke\"}," +
            "\"module\":{" +
            $"\"id\":{Str(pluginId)},\"version\":\"1.0.0\",\"targetSandboxVersion\":\"1.0.0\"," +
            "\"capabilityRequests\":[]," +
            $"\"metadata\":{{\"kernel\":\"AnonymousInvokeAsync\",\"pluginId\":{Str(pluginId)}}}," +
            "\"functions\":[{" +
            "\"id\":\"Invoke\",\"visibility\":\"entrypoint\"," +
            "\"parameters\":[]," +
            $"\"returnType\":{DotBoxDRpcTypeMapper.JsonType(returnType)}," +
            $"\"body\":{bodyJson}}}]}}}}";

        return new GeneratedPluginPackage(HintName(ns, packageName), BuildSource(ns, packageName, json));
    }

    private static InvokeAsyncInterception? Interception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string ns,
        string packageName,
        string pluginId,
        ITypeSymbol returnType,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var (resultExpression, helpers) = InvokeAsyncResultReaderSource.Create(returnType, "__result");
        var packageFullName = string.IsNullOrEmpty(ns)
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + packageName
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + ns + "." + packageName;

        return new InvokeAsyncInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDGenerationNames.Metadata.KernelInvocationSurfaceType,
            DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDGenerationNames.Metadata.GameWorldAccessType,
            returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            pluginId,
            packageFullName,
            resultExpression,
            helpers);
    }

    private static string BuildSource(string ns, string packageName, string json)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        if (!string.IsNullOrEmpty(ns))
        {
            builder.Append("namespace ").Append(ns).AppendLine(";");
            builder.AppendLine();
        }

        builder.Append("public static class ").AppendLine(packageName);
        builder.AppendLine("{");
        builder.Append("    public static ").Append(DotBoxDGenerationNames.TypeNames.GlobalPluginPackage).AppendLine(" Create()");
        builder.Append("        => ").Append(DotBoxDGenerationNames.TypeNames.GlobalPluginPackageJsonSerializer)
            .Append(".Import(\"").Append(json.Replace("\\", "\\\\").Replace("\"", "\\\"")).AppendLine("\");");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string HintName(string ns, string packageName)
        => string.IsNullOrWhiteSpace(ns)
            ? packageName + ".g.cs"
            : ns.Replace("@", string.Empty) + "." + packageName + ".g.cs";

    private static string JoinStrings(IEnumerable<string> values)
    {
        var parts = new List<string>();
        foreach (var value in values)
        {
            parts.Add(Str(value));
        }

        return string.Join(",", parts);
    }
}
