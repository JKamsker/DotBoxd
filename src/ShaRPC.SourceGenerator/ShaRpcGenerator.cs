using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ShaRPC.SourceGenerator;

/// <summary>
/// Incremental source generator for ShaRPC client proxies and server dispatchers.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ShaRpcGenerator : IIncrementalGenerator
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";
    private const string CancellationTokenFullName = "System.Threading.CancellationToken";
    private const string SystemThreadingTasks = "System.Threading.Tasks";

    /// <summary>Display format that emits fully-qualified <c>global::</c> type names so
    /// generated code never depends on the user's <c>using</c> set.</summary>
    private static readonly SymbolDisplayFormat s_qualifiedFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly DiagnosticDescriptor s_generatorErrorRule = new(
        id: "SHARPC001",
        title: "ShaRPC source generator error",
        messageFormat: "ShaRPC failed to generate for '{0}': {1}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_unsupportedMethodRule = new(
        id: "SHARPC002",
        title: "Unsupported ShaRPC method shape",
        messageFormat: "ShaRPC cannot generate code for '{0}.{1}': {2}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_unsupportedServiceRule = new(
        id: "SHARPC003",
        title: "Unsupported ShaRPC service shape",
        messageFormat: "ShaRPC cannot generate code for service '{0}': {1}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal readonly record struct ServiceResult(
        ServiceModel? Model,
        GeneratorError? Error,
        EquatableArray<MethodDiagnostic> MethodDiagnostics,
        ServiceDiagnostic? ServiceDiagnostic);

    internal readonly record struct GeneratorError(string Where, string Message);

    /// <summary>Diagnostic about one method (SHARPC002) — emitted while still producing the rest of the service.</summary>
    internal readonly record struct MethodDiagnostic(string InterfaceName, string MethodName, string Reason);

    /// <summary>Diagnostic about the service as a whole (SHARPC003) — service is skipped entirely.</summary>
    internal readonly record struct ServiceDiagnostic(string InterfaceName, string Reason);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ShaRpcServiceAttributeName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => GetServiceResult(ctx, ct))
            .WithTrackingName("ServiceResults");

        var errors = results
            .Where(static r => r.Error is not null)
            .Select(static (r, _) => r.Error!.Value)
            .WithTrackingName("ServiceErrors");

        context.RegisterSourceOutput(errors, static (spc, error) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_generatorErrorRule,
                Location.None,
                error.Where,
                error.Message)));

        // SHARPC002 — per-method diagnostics for shapes ShaRPC cannot generate (ref/in/out).
        var methodDiagnostics = results
            .SelectMany(static (r, _) => r.MethodDiagnostics.Array)
            .WithTrackingName("MethodDiagnostics");

        context.RegisterSourceOutput(methodDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedMethodRule,
                Location.None,
                d.InterfaceName,
                d.MethodName,
                d.Reason)));

        // SHARPC003 — service-level diagnostics (generic / nested service interfaces).
        var serviceDiagnostics = results
            .Where(static r => r.ServiceDiagnostic is not null)
            .Select(static (r, _) => r.ServiceDiagnostic!.Value)
            .WithTrackingName("ServiceDiagnostics");

        context.RegisterSourceOutput(serviceDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedServiceRule,
                Location.None,
                d.InterfaceName,
                d.Reason)));

        var models = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!)
            .WithTrackingName("Services");

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            try
            {
                var proxySource = ProxyGenerator.Generate(model);
                spc.AddSource(
                    $"{model.InterfaceName}.ShaRpcProxy.g.cs",
                    SourceText.From(proxySource, Encoding.UTF8));

                var dispatcherSource = DispatcherGenerator.Generate(model);
                spc.AddSource(
                    $"{model.InterfaceName}.ShaRpcDispatcher.g.cs",
                    SourceText.From(dispatcherSource, Encoding.UTF8));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    model.InterfaceName,
                    ex.ToString()));
            }
        });

        var allServices = models
            .Collect()
            .Select(static (arr, _) => arr.ToEquatableArray())
            .WithTrackingName("AllServices");

        context.RegisterSourceOutput(allServices, static (spc, services) =>
        {
            if (services.IsEmpty)
            {
                return;
            }

            try
            {
                var extensionsSource = GenerateExtensions(services);
                spc.AddSource(
                    "ShaRpcExtensions.g.cs",
                    SourceText.From(extensionsSource, Encoding.UTF8));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    "ShaRpcExtensions",
                    ex.ToString()));
            }
        });
    }

    private static ServiceResult GetServiceResult(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        try
        {
            return BuildServiceResult(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var name = context.TargetSymbol?.ToDisplayString() ?? "<unknown>";
            return new ServiceResult(
                Model: null,
                Error: new GeneratorError(name, ex.ToString()),
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                ServiceDiagnostic: null);
        }
    }

    private static ServiceResult BuildServiceResult(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol interfaceSymbol)
        {
            return default;
        }

        var displayName = interfaceSymbol.ToDisplayString();

        // SHARPC003 — reject generic service interfaces. The generated proxy/dispatcher
        // would need matching type parameters; supporting this fully is non-trivial and
        // out of scope today, so we surface a clear diagnostic instead of emitting broken
        // output silently.
        if (interfaceSymbol.IsGenericType)
        {
            return new ServiceResult(
                Model: null,
                Error: null,
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                ServiceDiagnostic: new ServiceDiagnostic(
                    displayName,
                    "generic service interfaces are not supported; declare a non-generic interface and forward to a generic helper if needed"));
        }

        // SHARPC003 — reject nested service interfaces (declared inside another type). The
        // generated proxy/dispatcher class would need to live inside that containing type
        // (which would have to be partial), which is a deeper refactor than today's scope.
        if (interfaceSymbol.ContainingType is not null)
        {
            return new ServiceResult(
                Model: null,
                Error: null,
                MethodDiagnostics: EquatableArray<MethodDiagnostic>.Empty,
                ServiceDiagnostic: new ServiceDiagnostic(
                    displayName,
                    "nested service interfaces are not supported; declare the interface at namespace scope"));
        }

        ct.ThrowIfCancellationRequested();

        // Pick the service attribute (already guaranteed to be present by ForAttributeWithMetadataName).
        string? customName = null;
        foreach (var attr in context.Attributes)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                {
                    customName = s;
                }
            }
        }

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : interfaceSymbol.ContainingNamespace.ToDisplayString();
        var interfaceName = interfaceSymbol.Name;
        var serviceName = customName ?? interfaceName;

        var methods = new List<MethodModel>();
        var methodDiagnostics = new List<MethodDiagnostic>();
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);

        // Walk the declaring interface AND every base interface, so methods inherited from
        // `IBar` in `interface IFoo : IBar` are also emitted on the FooProxy. Without this
        // step the generated proxy would fail to implement IFoo (CS0535).
        IEnumerable<IMethodSymbol> EnumerateMethods()
        {
            foreach (var member in interfaceSymbol.GetMembers())
            {
                if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                {
                    yield return m;
                }
            }
            foreach (var baseInterface in interfaceSymbol.AllInterfaces)
            {
                foreach (var member in baseInterface.GetMembers())
                {
                    if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                    {
                        yield return m;
                    }
                }
            }
        }

        foreach (var methodSymbol in EnumerateMethods())
        {
            ct.ThrowIfCancellationRequested();

            // De-duplicate in case a derived interface re-declares (via `new`) a base method
            // with the same signature shape.
            var sigKey = methodSymbol.Name + "(" +
                string.Join(",", methodSymbol.Parameters.Select(p => p.Type.ToDisplayString())) + ")";
            if (!seenSignatures.Add(sigKey))
            {
                continue;
            }

            string? customMethodName = null;
            foreach (var attr in methodSymbol.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == ShaRpcMethodAttributeName)
                {
                    foreach (var namedArg in attr.NamedArguments)
                    {
                        if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                        {
                            customMethodName = s;
                        }
                    }
                }
            }

            var returnType = methodSymbol.ReturnType;
            var returnKind = ClassifyReturnType(returnType, out var unwrappedReturnType);

            // SHARPC002 — ref/in/out parameters cannot be marshalled across an RPC
            // boundary. We can't simply drop the method (the proxy still has to implement
            // the user's interface or compilation fails with CS0535), so we emit a
            // throwing stub and surface a diagnostic.
            var unsupportedRefKindParam = methodSymbol.Parameters.FirstOrDefault(
                p => p.RefKind != RefKind.None && p.Type.ToDisplayString() != CancellationTokenFullName);

            var parameters = new List<ParameterModel>();
            var hasCancellationToken = false;
            foreach (var param in methodSymbol.Parameters)
            {
                if (param.Type.ToDisplayString() == CancellationTokenFullName)
                {
                    hasCancellationToken = true;
                    continue;
                }

                parameters.Add(new ParameterModel(
                    EscapeIdentifier(param.Name),
                    param.Type.ToDisplayString(s_qualifiedFormat),
                    RefKindKeyword(param.RefKind)));
            }

            string? unsupportedReason = null;
            if (unsupportedRefKindParam is not null)
            {
                unsupportedReason =
                    $"parameter '{unsupportedRefKindParam.Name}' uses an unsupported pass-by-reference kind '{unsupportedRefKindParam.RefKind.ToString().ToLowerInvariant()}'";
                methodDiagnostics.Add(new MethodDiagnostic(
                    displayName,
                    methodSymbol.Name,
                    unsupportedReason));
            }

            methods.Add(new MethodModel(
                Name: EscapeIdentifier(methodSymbol.Name),
                RpcName: EscapeStringLiteral(customMethodName ?? methodSymbol.Name),
                ReturnKind: returnKind,
                UnwrappedReturnType: unwrappedReturnType,
                HasCancellationToken: hasCancellationToken,
                Parameters: parameters.ToEquatableArray(),
                UnsupportedReason: unsupportedReason));
        }

        static string RefKindKeyword(RefKind kind) => kind switch
        {
            RefKind.Ref => "ref ",
            RefKind.In => "in ",
            RefKind.Out => "out ",
            _ => string.Empty,
        };

        return new ServiceResult(
            Model: new ServiceModel(
                Namespace: ns,
                InterfaceName: interfaceName,
                ServiceName: EscapeStringLiteral(serviceName),
                Methods: methods.ToEquatableArray()),
            Error: null,
            MethodDiagnostics: methodDiagnostics.ToEquatableArray(),
            ServiceDiagnostic: null);
    }

    /// <summary>
    /// Classifies a method return type into one of the <see cref="MethodReturnKind"/>
    /// variants and computes the unwrapped payload type (for generic Task/ValueTask, this
    /// is the type argument; for sync returns, it's the return type itself).
    /// </summary>
    private static MethodReturnKind ClassifyReturnType(ITypeSymbol returnType, out string? unwrappedReturnType)
    {
        if (returnType is INamedTypeSymbol named && named.IsGenericType)
        {
            var nsName = named.ContainingNamespace?.ToDisplayString();
            if (nsName == SystemThreadingTasks)
            {
                if (named.Name == "Task")
                {
                    unwrappedReturnType = named.TypeArguments[0].ToDisplayString(s_qualifiedFormat);
                    return MethodReturnKind.TaskOf;
                }
                if (named.Name == "ValueTask")
                {
                    unwrappedReturnType = named.TypeArguments[0].ToDisplayString(s_qualifiedFormat);
                    return MethodReturnKind.ValueTaskOf;
                }
            }
        }

        var rNs = returnType.ContainingNamespace?.ToDisplayString();
        if (rNs == SystemThreadingTasks)
        {
            if (returnType.Name == "Task")
            {
                unwrappedReturnType = null;
                return MethodReturnKind.Task;
            }
            if (returnType.Name == "ValueTask")
            {
                unwrappedReturnType = null;
                return MethodReturnKind.ValueTask;
            }
        }

        if (returnType.SpecialType == SpecialType.System_Void)
        {
            unwrappedReturnType = null;
            return MethodReturnKind.Void;
        }

        unwrappedReturnType = returnType.ToDisplayString(s_qualifiedFormat);
        return MethodReturnKind.Sync;
    }

    /// <summary>
    /// Prefixes a C# identifier with <c>@</c> when it would otherwise collide with a
    /// reserved keyword (e.g. a parameter named <c>class</c> or <c>default</c>).
    /// </summary>
    private static string EscapeIdentifier(string name)
    {
        var kind = Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name);
        if (kind != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
        {
            return "@" + name;
        }
        return name;
    }

    /// <summary>
    /// Returns the user-facing identifier text WITHOUT the <c>@</c> prefix — used inside
    /// diagnostic messages where the prefix would look like syntax noise.
    /// </summary>
    private static string EscapeIdentifierForMessage(string name) => name;

    /// <summary>
    /// Escapes a value that will appear inside a C# string literal in generated source.
    /// Currently handles backslash and double-quote characters. The values come from
    /// user-supplied attribute arguments (<c>[ShaRpcService(Name = "...")]</c>), so we
    /// cannot trust them to be free of these characters.
    /// </summary>
    private static string EscapeStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.IndexOf('\\') < 0 && value.IndexOf('"') < 0)
        {
            return value;
        }
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string GenerateExtensions(EquatableArray<ServiceModel> services)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace ShaRPC.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Extension methods for registering generated ShaRPC services.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ShaRpcGeneratedExtensions");
        sb.AppendLine("    {");

        foreach (var service in services)
        {
            var serviceName = NamingHelpers.StripInterfacePrefix(service.InterfaceName);
            var proxyName = serviceName + "Proxy";
            var dispatcherName = serviceName + "Dispatcher";
            var fullInterfaceName = "global::" + (string.IsNullOrEmpty(service.Namespace)
                ? service.InterfaceName
                : $"{service.Namespace}.{service.InterfaceName}");
            var fullProxyName = "global::" + (string.IsNullOrEmpty(service.Namespace)
                ? proxyName
                : $"{service.Namespace}.{proxyName}");
            var fullDispatcherName = "global::" + (string.IsNullOrEmpty(service.Namespace)
                ? dispatcherName
                : $"{service.Namespace}.{dispatcherName}");

            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Creates a proxy for {service.InterfaceName}.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static {fullInterfaceName} Create{serviceName}Proxy(this global::ShaRPC.Core.Client.IShaRpcClient client)");
            sb.AppendLine($"            => new {fullProxyName}(client);");

            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// Registers {service.InterfaceName} with the server.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine($"        public static global::ShaRPC.Core.Server.ShaRpcServerBuilder Add{serviceName}(this global::ShaRPC.Core.Server.ShaRpcServerBuilder builder, {fullInterfaceName} implementation)");
            sb.AppendLine($"            => builder.AddDispatcher(new {fullDispatcherName}(implementation));");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
