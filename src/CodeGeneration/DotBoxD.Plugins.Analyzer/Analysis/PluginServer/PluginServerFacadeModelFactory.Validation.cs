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
        ValidateLiveSettingUpdateConstructor(serverType, liveSettingUpdateType, stringType);
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

            if (actual[i].RefKind != RefKind.None)
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateLiveSettingUpdateConstructor(
        INamedTypeSymbol serverType,
        ITypeSymbol liveSettingUpdateType,
        ITypeSymbol stringType)
    {
        if (liveSettingUpdateType is not INamedTypeSymbol named)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.Name}' live-setting update type '{liveSettingUpdateType.ToDisplayString()}' must be a named type.");
        }

        if (!IsAccessibleFromGeneratedServer(named))
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.Name}' live-setting update type '{liveSettingUpdateType.ToDisplayString()}' must be accessible from the generated facade.");
        }

        foreach (var constructor in named.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 2 &&
                constructor.Parameters[0].RefKind == RefKind.None &&
                constructor.Parameters[1].RefKind == RefKind.None &&
                SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, stringType) &&
                SymbolEqualityComparer.Default.Equals(constructor.Parameters[1].Type, stringType) &&
                IsAccessibleFromGeneratedServer(constructor.DeclaredAccessibility))
            {
                return;
            }
        }

        throw new NotSupportedException(
            $"Generated plugin server '{serverType.Name}' live-setting update type '{liveSettingUpdateType.ToDisplayString()}' must expose an accessible constructor '(string name, string value)'.");
    }

    private static bool IsAccessibleFromGeneratedServer(Accessibility accessibility)
        => accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool IsAccessibleFromGeneratedServer(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (!IsAccessibleFromGeneratedServer(current.DeclaredAccessibility))
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
        IReadOnlyList<PluginServerControlProperty> controls,
        bool emitsRemoteLocalEventSink)
    {
        var reserved = GeneratedReservedMemberNames();
        AddGeneratedFieldNames(reserved, controls, emitsRemoteLocalEventSink);
        var generatedMembers = new HashSet<string>(reserved, StringComparer.Ordinal);
        AddGeneratedNestedTypeNames(generatedMembers, controls, emitsRemoteLocalEventSink);

        foreach (var property in properties)
        {
            if (reserved.Contains(property.Name))
            {
                throw new NotSupportedException(
                    $"Generated plugin server world '{worldType.ToDisplayString()}' member '{property.Name}' collides with the generated facade surface.");
            }

            EnsureSingleFacadeCategory(generatedMembers, worldType, property.Name);
        }

        // Forwarded methods may legitimately repeat a name (overloads differing only by signature); ResolveMethods
        // keeps each distinct signature, so both reach the methods bucket. Dedupe names before the cross-category
        // check — overloads share ONE category and must not be flagged as a clash. A name also shared with a
        // property or control is still a genuine cross-category collision and is rejected.
        foreach (var methodName in methods
            .Select(static method => method.Name)
            .Distinct(StringComparer.Ordinal))
        {
            if (reserved.Contains(methodName))
            {
                throw new NotSupportedException(
                    $"Generated plugin server world '{worldType.ToDisplayString()}' member '{methodName}' collides with the generated facade surface.");
            }

            EnsureSingleFacadeCategory(generatedMembers, worldType, methodName);
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

        ValidateGeneratedSiblingTypeCollisions(serverType, worldType, controls);

        foreach (var member in serverType.GetMembers())
        {
            if (member.IsImplicitlyDeclared ||
                member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ||
                string.Equals(member.Name, "OnConfigured", StringComparison.Ordinal))
            {
                continue;
            }

            if (member is IMethodSymbol invokeAsync &&
                string.Equals(member.Name, "InvokeAsync", StringComparison.Ordinal))
            {
                if (IsGeneratedInvokeAsyncSignature(invokeAsync, worldType))
                {
                    throw new NotSupportedException(
                        $"Generated plugin server '{serverType.ToDisplayString()}' member '{member.Name}' collides with the generated facade surface.");
                }

                continue;
            }

            if (generatedMembers.Contains(member.Name))
            {
                throw new NotSupportedException(
                    $"Generated plugin server '{serverType.ToDisplayString()}' member '{member.Name}' collides with the generated facade surface.");
            }
        }
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

    private static void ValidateServerTargetShape(
        INamedTypeSymbol serverType,
        CancellationToken cancellationToken)
    {
        if (serverType.TypeKind != TypeKind.Class)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be a class.");
        }

        if (serverType.IsGenericType)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be non-generic.");
        }

        if (serverType.ContainingType is not null)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be non-nested.");
        }

        if (serverType.IsAbstract)
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be concrete.");
        }

        if (!IsPartialClass(serverType, cancellationToken))
        {
            throw new NotSupportedException(
                $"Generated plugin server '{serverType.ToDisplayString()}' must be partial.");
        }
    }
}
