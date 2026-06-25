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
        catch (NotSupportedException ex)
        {
            return new InvokeAsyncResult(
                null,
                null,
                new PluginKernelDiagnostic(
                    "InvokeAsync call could not be lowered: " + ex.Message,
                    PluginDiagnosticLocation.From(invocation.GetLocation())));
        }
    }

    private static InvokeAsyncResult? TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: InvokeAsyncMethod })
        {
            if (IsDotBoxDInvokeAsync(model, invocation, cancellationToken))
            {
                throw new NotSupportedException(
                    "implicit InvokeAsync calls are not supported; call InvokeAsync on the generated plugin server receiver.");
            }

            return null;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax access ||
            !string.Equals(access.Name.Identifier.ValueText, InvokeAsyncMethod, StringComparison.Ordinal))
        {
            return null;
        }

        if (!TryServerInvocationSurface(
                model,
                access.Expression,
                cancellationToken,
                out var receiverType,
                out var serverAccessType,
                out var worldType))
        {
            if (IsDotBoxDInvokeAsync(model, invocation, cancellationToken))
            {
                throw new NotSupportedException(
                    "receiver must be a generated plugin server facade or generated server interface, not the erased IPluginServer<TWorld> surface.");
            }

            return null;
        }

        var shape = model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method
            ? InvokeAsyncCallShape.Create(invocation, method, model, cancellationToken)
            : null;
        shape ??= InvokeAsyncCallShape.Create(invocation, worldType, model, cancellationToken);
        if (shape is null)
        {
            throw new NotSupportedException(
                "lambda must use a supported block body and capture shape.");
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
        var interception = Interception(
            invocation,
            model,
            receiverType,
            serverAccessType,
            ns,
            packageName,
            pluginId,
            shape,
            cancellationToken);
        if (interception is null)
        {
            throw new NotSupportedException("call site is not interceptable by the C# compiler.");
        }

        var package = EmitPackage(ns, packageName, pluginId, shape, bodyJson, effects, capabilities);
        return new InvokeAsyncResult(package, interception, null);
    }

    private static bool IsDotBoxDInvokeAsync(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            !string.Equals(method.Name, InvokeAsyncMethod, StringComparison.Ordinal))
        {
            return false;
        }

        return IsPluginServerType(method.ContainingType);
    }

    private static bool IsPluginServerType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var original = named.OriginalDefinition.ToDisplayString();
        return string.Equals(original, "DotBoxD.Abstractions.IPluginServer<TWorld>", StringComparison.Ordinal);
    }

    private static bool TryServerInvocationSurface(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken,
        out string receiverType,
        out string? serverAccessType,
        out INamedTypeSymbol worldType)
        => InvokeAsyncReceiverResolver.TryResolve(
            model,
            receiver,
            cancellationToken,
            out receiverType,
            out serverAccessType,
            out worldType);

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

        return new GeneratedPluginPackage(
            HintName(ns, packageName),
            BuildSource(ns, packageName, json),
            ns,
            packageName);
    }

    private static InvokeAsyncInterception? Interception(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string receiverType,
        string? serverAccessType,
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

        var reader = new InvokeAsyncResultReaderSource("ReadInvokeAsyncResult_" + packageName + "_");
        var resultExpression = reader.ReadExpression(
            shape.ReturnType,
            shape.SyncOuts.Count == 0 ? "__result" : "__result.GetItem(0)");
        var syncOutAssignments = new string[shape.SyncOuts.Count];
        for (var i = 0; i < shape.SyncOuts.Count; i++)
        {
            var syncOut = shape.SyncOuts[i];
            var value = reader.ReadExpression(syncOut.Type, "__result.GetItem(" + (i + 1) + ")");
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
            : DotBoxDGenerationNames.TypeNames.GlobalPrefix + DotBoxDMetadataNames.ServerInvocationDelegateType +
              "<" + shape.WorldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
              ", " + captureType +
              ", " + shape.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">";

        return new InvokeAsyncInterception(
            location.GetInterceptsLocationAttributeSyntax(),
            receiverType,
            serverAccessType,
            shape.WorldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
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
