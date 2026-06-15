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
            !IsServerInvocationSurface(model, access.Expression, cancellationToken))
        {
            return null;
        }

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            InvokeAsyncCallShape.Create(invocation, method, model, cancellationToken) is not { } shape)
        {
            return null;
        }

        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var lowerer = new DotBoxDRpcJsonLowerer(model, capabilities, effects, cancellationToken);
        var bodyJson = shape.LowerBody(lowerer, shape.Block);
        effects.Add(DotBoxDGenerationNames.Effects.Cpu);
        if (lowerer.Allocates)
        {
            effects.Add(DotBoxDGenerationNames.Effects.Alloc);
        }

        var id = HookChainIdentity.Compute(invocation);
        var pluginId = "$anon:" + id;
        var packageName = "InvokeAsync_" + id + DotBoxDGenerationNames.PluginPackageSuffix;
        var ns = HookChainIdentity.Namespace(invocation);
        var package = EmitPackage(ns, packageName, pluginId, shape, bodyJson, effects, capabilities);
        var interception = Interception(invocation, model, ns, packageName, pluginId, shape, cancellationToken);
        return new InvokeAsyncResult(package, interception);
    }

    private static bool IsServerInvocationSurface(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken)
        => string.Equals(
            model.GetTypeInfo(receiver, cancellationToken).Type?.ToDisplayString(),
            DotBoxDGenerationNames.Metadata.ServerInvocationSurfaceType,
            StringComparison.Ordinal);

    private static GeneratedPluginPackage EmitPackage(
        string ns,
        string packageName,
        string pluginId,
        InvokeAsyncCallShape shape,
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
            $"\"parameters\":{shape.ParametersJson}," +
            $"\"returnType\":{shape.ReturnTypeJson}," +
            $"\"body\":{bodyJson}}}]}}}}";

        return new GeneratedPluginPackage(HintName(ns, packageName), BuildSource(ns, packageName, json));
    }

    private static InvokeAsyncInterception? Interception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string ns,
        string packageName,
        string pluginId,
        InvokeAsyncCallShape shape,
        CancellationToken cancellationToken)
    {
        var location = model.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var reader = new InvokeAsyncResultReaderSource();
        var resultExpression = reader.ReadExpression(
            shape.ReturnType,
            shape.SyncOuts.Count == 0 ? "__result" : "__fields[0]");
        var syncOutAssignments = new string[shape.SyncOuts.Count];
        for (var i = 0; i < shape.SyncOuts.Count; i++)
        {
            var syncOut = shape.SyncOuts[i];
            var value = reader.ReadExpression(syncOut.Type, "__fields[" + (i + 1) + "]");
            syncOutAssignments[i] = shape.UsesReflectionCaptures
                ? "__WriteCapture(lambda, " + Str(syncOut.TargetName) + ", " + value + ")"
                : "captures." + syncOut.TargetName + " = " + value;
        }

        var packageFullName = string.IsNullOrEmpty(ns)
            ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + packageName
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + ns + "." + packageName;
        var captureType = shape.CaptureType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var captureDelegateType = shape.CaptureType is null
            ? null
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDGenerationNames.Metadata.ServerInvocationDelegateType;

        return new InvokeAsyncInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDGenerationNames.Metadata.ServerInvocationSurfaceType,
            DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDGenerationNames.Metadata.GameWorldAccessType,
            shape.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            captureType,
            captureDelegateType,
            pluginId,
            packageFullName,
            shape.ArgumentsExpression,
            resultExpression,
            new EquatableArray<string>(syncOutAssignments),
            shape.UsesReflectionCaptures,
            reader.Helpers);
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
