using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static void ValidateControlServiceContract(
        INamedTypeSymbol serverType,
        Compilation compilation,
        INamedTypeSymbol controlServiceType,
        ITypeSymbol liveSettingUpdateType)
    {
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);
        var cancellationTokenType = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        var valueTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        var valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        var wireClientType = compilation.GetTypeByMetadataName("DotBoxD.Abstractions.IServerExtensionWireClient");
        if (cancellationTokenType is null ||
            valueTaskType is null ||
            valueTaskOfT is null ||
            wireClientType is null)
        {
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(controlServiceType, wireClientType) &&
            !controlServiceType.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, wireClientType)))
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.Name}' control-plane contract '{controlServiceType.ToDisplayString()}' must implement DotBoxD.Abstractions.IServerExtensionWireClient.");
        }

        var valueTaskString = valueTaskOfT.Construct(stringType);
        EnsureControlMethod(
            serverType,
            controlServiceType,
            "InstallPluginAsync",
            valueTaskString,
            [stringType, cancellationTokenType]);
        EnsureControlMethod(
            serverType,
            controlServiceType,
            "InstallSubscriptionAsync",
            valueTaskString,
            [stringType, cancellationTokenType]);
        EnsureControlMethod(
            serverType,
            controlServiceType,
            "InstallServerExtensionAsync",
            valueTaskString,
            [stringType, cancellationTokenType]);
        EnsureControlMethod(
            serverType,
            controlServiceType,
            "UpdateSettingsAsync",
            valueTaskType,
            [stringType, compilation.CreateArrayTypeSymbol(liveSettingUpdateType), boolType, cancellationTokenType]);
        EnsureControlMethod(
            serverType,
            controlServiceType,
            "HoldUntilShutdownAsync",
            valueTaskType,
            [cancellationTokenType]);
    }

    private static void EnsureControlMethod(
        INamedTypeSymbol serverType,
        INamedTypeSymbol controlServiceType,
        string name,
        ITypeSymbol returnType,
        IReadOnlyList<ITypeSymbol> parameterTypes)
    {
        foreach (var member in MembersIncludingInherited(controlServiceType))
        {
            if (member is IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    IsStatic: false
                } method &&
                string.Equals(method.Name, name, StringComparison.Ordinal) &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, returnType) &&
                ParametersMatch(method.Parameters, parameterTypes))
            {
                return;
            }
        }

        throw new NotSupportedException(
            $"Generated plugin server '{serverType.Name}' control-plane contract '{controlServiceType.ToDisplayString()}' must declare {name} with the generated facade signature.");
    }

    private static bool ParametersMatch(
        IReadOnlyList<IParameterSymbol> actual,
        IReadOnlyList<ITypeSymbol> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < actual.Count; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(actual[i].Type, expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidatePublicFacadeSignatureTypes(
        INamedTypeSymbol serverType,
        INamedTypeSymbol worldType,
        INamedTypeSymbol controlServiceType,
        ITypeSymbol liveSettingUpdateType)
    {
        if (serverType.DeclaredAccessibility != Accessibility.Public)
        {
            return;
        }

        EnsurePublicSignatureType(worldType, "world interface");
        EnsurePublicSignatureType(controlServiceType, "control-plane contract");
        EnsurePublicSignatureType(liveSettingUpdateType, "live-setting update type");
    }

    private static void EnsurePublicSignatureType(ITypeSymbol type, string description)
    {
        if (type is IArrayTypeSymbol array)
        {
            EnsurePublicSignatureType(array.ElementType, description);
            return;
        }

        if (type is not INamedTypeSymbol named)
        {
            return;
        }

        for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                throw new NotSupportedException(
                    $"Generated plugin server public {description} '{type.ToDisplayString()}' must be public.");
            }
        }

        foreach (var argument in named.TypeArguments)
        {
            EnsurePublicSignatureType(argument, description);
        }
    }

    private static void ValidateGeneratedSurfaceCollisions(
        INamedTypeSymbol serverType,
        INamedTypeSymbol worldType,
        IReadOnlyList<PluginServerForwardedProperty> properties,
        IReadOnlyList<PluginServerForwardedMethod> methods,
        IReadOnlyList<PluginServerControlProperty> controls)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            // Public facade surface.
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
            // Generated private helpers, consts, and nested types. A forwarded world member named like one of
            // these otherwise emits alongside the generated member as a raw CS0111/CS0102 instead of DBXK100.
            "NotStartedMessage",
            "NoWorldProxyMessage",
            "Initialize",
            "RequireControl",
            "RequireWorld",
            "ThrowIfDisposed",
            "RecordSetup",
            "ReplaySetupAsync",
            "AwaitAnonymousKernelAsync",
            "RemoveAnonymousKernel",
            "InstallPluginPackageAsync",
            "InstallSubscriptionPackageAsync",
            "InstallServerExtensionPackageAsync",
            "RecordedInstall",
            "RecordedInstallKind",
            "SetupRecorder",
            "LiveSettingsHandle",
            "RemoteLocalEventSink",
        };
        var generatedMembers = new HashSet<string>(reserved, StringComparer.Ordinal);

        foreach (var property in properties)
        {
            if (reserved.Contains(property.Name))
            {
                throw new NotSupportedException(
                    $"Generated plugin server world '{worldType.ToDisplayString()}' member '{property.Name}' collides with the generated facade surface.");
            }

            EnsureSingleFacadeCategory(generatedMembers, worldType, property.Name);
        }

        foreach (var method in methods)
        {
            if (reserved.Contains(method.Name))
            {
                throw new NotSupportedException(
                    $"Generated plugin server world '{worldType.ToDisplayString()}' member '{method.Name}' collides with the generated facade surface.");
            }

            EnsureSingleFacadeCategory(generatedMembers, worldType, method.Name);
        }

        foreach (var control in controls)
        {
            if (reserved.Contains(control.Name))
            {
                throw new NotSupportedException(
                $"Generated plugin server control '{control.Name}' collides with the generated facade surface.");
            }

            EnsureSingleFacadeCategory(generatedMembers, worldType, control.Name);
        }

        ValidateUserPartialCollisions(serverType, generatedMembers);
    }

    // Each forwarded category dedupes within itself, but a name shared across categories (e.g. a forwarded
    // property and a control both named the same, inherited from different interfaces) would emit twice as
    // CS0102. Surface the cross-category clash as the designed DBXK100 instead.
    private static void EnsureSingleFacadeCategory(
        HashSet<string> generatedMembers,
        INamedTypeSymbol worldType,
        string name)
    {
        if (!generatedMembers.Add(name))
        {
            throw new NotSupportedException(
                $"Generated plugin server world '{worldType.ToDisplayString()}' member '{name}' is generated in more than one facade category (forwarded property, method, or control).");
        }
    }

    private static void ValidateUserPartialCollisions(
        INamedTypeSymbol serverType,
        HashSet<string> generatedMembers)
    {
        foreach (var member in serverType.GetMembers())
        {
            if (member.IsImplicitlyDeclared ||
                member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ||
                string.Equals(member.Name, "OnConfigured", StringComparison.Ordinal) ||
                string.Equals(member.Name, "InvokeAsync", StringComparison.Ordinal))
            {
                continue;
            }

            if (generatedMembers.Contains(member.Name))
            {
                throw new NotSupportedException(
                    $"Generated plugin server '{serverType.ToDisplayString()}' member '{member.Name}' collides with the generated facade surface.");
            }
        }
    }
}
