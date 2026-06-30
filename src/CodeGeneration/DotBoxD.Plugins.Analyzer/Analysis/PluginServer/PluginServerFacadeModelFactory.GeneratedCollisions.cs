using Microsoft.CodeAnalysis;
using static DotBoxD.Plugins.Analyzer.Analysis.PluginServer.PluginServerFacadeNameFormatter;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static HashSet<string> GeneratedReservedMemberNames()
        => new(StringComparer.Ordinal)
        {
            "Services",
            "ServerExtensions",
            "Hooks",
            "Subscriptions",
            "WireClient",
            "StartAsync",
            "RunAsync",
            "HoldUntilShutdownAsync",
            "Dispose",
            "DisposeAsync",
            "InvokeAsync",
            "Get",
            "PluginId",
            "InvokeServerExtensionAsync",
            "EnsureAnonymousKernelAsync",
            "NotStartedMessage",
            "NoWorldProxyMessage",
            "Initialize",
            "RequireControl",
            "RequireWorld",
            "RequireInstalledKernel",
            "ThrowIfDisposed",
            "RecordSetup",
            "ReplaySetupAsync",
            "AwaitAnonymousKernelAsync",
            "RemoveAnonymousKernel",
            "InstallPluginPackageAsync",
            "InstallSubscriptionPackageAsync",
            "InstallServerExtensionPackageAsync",
        };

    private static void ValidateGeneratedSiblingTypeCollisions(
        INamedTypeSymbol serverType,
        INamedTypeSymbol worldType,
        IReadOnlyList<PluginServerControlProperty> controls)
    {
        var generatedTypeNames = new List<string>
        {
            serverType.Name + "Builder",
            ServerInterfaceName(worldType),
            SetupInterfaceName(serverType.Name),
            HookRegistryName(serverType.Name),
            SubscriptionRegistryName(serverType.Name)
        };
        foreach (var control in controls)
        {
            generatedTypeNames.Add(control.AccumulatorInterfaceName);
        }

        foreach (var generatedName in generatedTypeNames)
        {
            foreach (var existing in serverType.ContainingNamespace.GetTypeMembers(generatedName))
            {
                if (!SymbolEqualityComparer.Default.Equals(existing, serverType))
                {
                    throw new NotSupportedException(
                        $"Generated plugin server type '{generatedName}' collides with an existing type in namespace '{serverType.ContainingNamespace.ToDisplayString()}'.");
                }
            }
        }
    }

    private static void AddGeneratedNestedTypeNames(
        HashSet<string> generatedMembers,
        IReadOnlyList<PluginServerControlProperty> controls,
        bool emitsRemoteLocalEventSink)
    {
        generatedMembers.Add("RecordedInstallKind");
        generatedMembers.Add("RecordedInstall");
        generatedMembers.Add("SetupRecorder");
        generatedMembers.Add("LiveSettingsHandle");
        if (emitsRemoteLocalEventSink)
        {
            generatedMembers.Add("RemoteLocalEventSink");
        }

        foreach (var control in controls)
        {
            generatedMembers.Add(control.WrapperName);
            generatedMembers.Add(control.Name + "SetupAccumulator");
        }
    }

    private static void AddGeneratedFieldNames(
        HashSet<string> generatedMembers,
        IReadOnlyList<PluginServerControlProperty> controls,
        bool emitsRemoteLocalEventSink)
    {
        foreach (var fieldName in ReservedFacadeFieldNames())
        {
            if (!emitsRemoteLocalEventSink &&
                string.Equals(fieldName, "_localHandlers", StringComparison.Ordinal))
            {
                continue;
            }

            generatedMembers.Add(fieldName);
        }

        foreach (var control in controls)
        {
            generatedMembers.Add(control.FieldName);
        }
    }

    private static bool IsGeneratedInvokeAsyncSignature(IMethodSymbol method, INamedTypeSymbol worldType)
        => IsGeneratedSimpleInvokeAsyncSignature(method, worldType) ||
           IsGeneratedCaptureInvokeAsyncSignature(method, worldType);

    private static bool IsGeneratedSimpleInvokeAsyncSignature(IMethodSymbol method, INamedTypeSymbol worldType)
    {
        if (method.TypeParameters.Length != 1 ||
            method.Parameters.Length != 2 ||
            !IsValueTaskOf(method.ReturnType, method.TypeParameters[0]) ||
            !IsCancellationToken(method.Parameters[1].Type) ||
            method.Parameters[0].Type is not INamedTypeSymbol
            {
                Name: "Func",
                TypeArguments.Length: 2
            } func ||
            !SymbolEqualityComparer.Default.Equals(func.TypeArguments[0], worldType))
        {
            return false;
        }

        return IsValueTaskOf(func.TypeArguments[1], method.TypeParameters[0]);
    }

    private static bool IsGeneratedCaptureInvokeAsyncSignature(IMethodSymbol method, INamedTypeSymbol worldType)
    {
        if (method.TypeParameters.Length != 2 ||
            method.Parameters.Length != 3 ||
            !IsValueTaskOf(method.ReturnType, method.TypeParameters[1]) ||
            !IsCancellationToken(method.Parameters[2].Type) ||
            method.Parameters[1].Type is not INamedTypeSymbol
            {
                Name: "RemoteServerInvocation",
                TypeArguments.Length: 3
            } invocation ||
            !SymbolEqualityComparer.Default.Equals(invocation.TypeArguments[0], worldType) ||
            !SymbolEqualityComparer.Default.Equals(invocation.TypeArguments[1], method.TypeParameters[0]))
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(invocation.TypeArguments[2], method.TypeParameters[1]);
    }

    private static bool IsValueTaskOf(ITypeSymbol type, ITypeSymbol argument)
        => type is INamedTypeSymbol
        {
            Name: "ValueTask",
            TypeArguments.Length: 1
        } valueTask &&
           string.Equals(valueTask.ContainingNamespace.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal) &&
           SymbolEqualityComparer.Default.Equals(valueTask.TypeArguments[0], argument);

    private static bool IsCancellationToken(ITypeSymbol type)
        => string.Equals(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            "global::System.Threading.CancellationToken",
            StringComparison.Ordinal);
}
