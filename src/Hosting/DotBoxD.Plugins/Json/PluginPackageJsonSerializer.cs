using System.Buffers;
using System.Text;
using System.Text.Json;
using DotBoxD.Kernels.Serialization.Json;
using DotBoxD.Kernels.Serialization.Json.Internal;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Json;

using static JsonImport;

public static partial class PluginPackageJsonSerializer
{
    public static string Export(PluginPackage package, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(package);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented });
        WritePackage(writer, package);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static PluginPackage Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            JsonImportBudgetGuard.Validate(json);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });

            var package = ReadPackage(document.RootElement);
            // A server extension package has its own shape (no event subscription/contract), so it is
            // validated by RpcKernelPackageValidator instead of the event-kernel validator.
            if (package.Manifest.RpcEntrypoint is not null)
            {
                RpcKernelPackageValidator.Validate(package);
            }
            else
            {
                PluginPackageValidator.Validate(package);
            }

            return package;
        }
        catch (JsonException ex)
        {
            throw Error("E-JSON-INVALID", ex.Message);
        }
        catch (FormatException ex)
        {
            throw Error("E-JSON-VERSION", ex.Message);
        }
    }

    private static PluginPackage ReadPackage(JsonElement element)
    {
        RequireAllowedProperties(element, "plugin package", ["manifest", "module", "entrypoints"]);
        var manifest = ReadManifest(Required(element, "manifest"));
        var moduleElement = Required(element, "module");
        var module = JsonImporter.Import(moduleElement, moduleElement.GetRawText());
        var entrypoints = manifest.RpcEntrypoint is not null
            ? ReadEntrypoints(Required(element, "entrypoints"), requireAliases: true)
            : element.TryGetProperty("entrypoints", out var entrypointElement)
                ? ReadEntrypoints(entrypointElement, requireAliases: false)
            : null;

        return PluginPackage.Create(manifest, module, entrypoints);
    }

    private static PluginManifest ReadManifest(JsonElement element)
    {
        RequireAllowedProperties(
            element,
            "plugin manifest",
            ["pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions", "requiredCapabilities", "rpcEntrypoint"]);

        return new PluginManifest(
            RequiredString(element, "pluginId"),
            RequiredString(element, "contract"),
            ReadExecutionMode(RequiredString(element, "mode")),
            ReadStringArray(RequiredArray(element, "effects"), "effects"),
            ReadLiveSettings(RequiredArray(element, "liveSettings")),
            ReadSubscriptions(RequiredArray(element, "subscriptions")))
        {
            // Optional for back-compat: manifests exported before required-capability derivation omit it.
            RequiredCapabilities = element.TryGetProperty("requiredCapabilities", out var requiredCapabilities)
                ? ReadStringArray(requiredCapabilities, "requiredCapabilities")
                : [],
            // Present only for server extension kernels; event kernels omit it.
            RpcEntrypoint = element.TryGetProperty("rpcEntrypoint", out var rpcEntrypoint)
                ? ReadStringValue(rpcEntrypoint, "rpcEntrypoint")
                : null
        };
    }

    private static ExecutionMode ReadExecutionMode(string value)
    {
        if (!Enum.TryParse<ExecutionMode>(value, ignoreCase: true, out var mode) ||
            !Enum.IsDefined(mode))
        {
            throw Error("E-JSON-MODE", $"unsupported execution mode '{value}'");
        }

        return mode;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement array, string name)
    {
        RequireArray(array, name);
        var values = AllocateArray<string>(array, out var count);
        if (count == 0)
        {
            return values;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            values[index] = ReadStringValue(item, $"{name}[{index}]");
            index++;
        }

        return values;
    }

    private static IReadOnlyList<LiveSettingDefinition> ReadLiveSettings(JsonElement array)
    {
        var settings = AllocateArray<LiveSettingDefinition>(array, out var count);
        if (count == 0)
        {
            return settings;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            settings[index++] = ReadLiveSetting(item);
        }

        return settings;
    }

    private static LiveSettingDefinition ReadLiveSetting(JsonElement element)
    {
        RequireAllowedProperties(element, "live setting", ["name", "type", "defaultValue", "min", "max"]);
        var type = RequiredString(element, "type");
        return new LiveSettingDefinition(
            RequiredString(element, "name"),
            type,
            ReadLiveSettingValue(Required(element, "defaultValue"), type, "defaultValue"),
            ReadOptionalLiveSettingValue(element, "min", type),
            ReadOptionalLiveSettingValue(element, "max", type));
    }

    private static object? ReadOptionalLiveSettingValue(JsonElement element, string name, string type)
        => element.TryGetProperty(name, out var value)
            ? ReadLiveSettingValue(value, type, name)
            : null;

    private static object? ReadLiveSettingValue(JsonElement value, string type, string name)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return type switch
        {
            "bool" => ReadBoolValue(value, name),
            "int" => ReadInt32Value(value, name),
            "long" => ReadInt64Value(value, name),
            "double" => ReadDoubleValue(value, name),
            "string" => ReadStringValue(value, name),
            _ => ReadJsonScalar(value, name)
        };
    }

    private static object ReadJsonScalar(JsonElement value, string name)
        => value.ValueKind switch
        {
            JsonValueKind.String => ReadStringValue(value, name),
            JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => ReadDoubleValue(value, name),
            _ => throw Error("E-JSON-TYPE", $"'{name}' must be a scalar value")
        };

    private static IReadOnlyList<HookSubscriptionManifest> ReadSubscriptions(JsonElement array)
    {
        var subscriptions = AllocateArray<HookSubscriptionManifest>(array, out var count);
        if (count == 0)
        {
            return subscriptions;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            subscriptions[index++] = ReadSubscription(item);
        }

        return subscriptions;
    }

    private static HookSubscriptionManifest ReadSubscription(JsonElement element)
    {
        RequireAllowedProperties(
            element,
            "hook subscription",
            [
                "event",
                "kernel",
                "indexedPredicates",
                "indexCoversPredicate",
                "localTerminal",
                "projectedType",
                "priority",
                "resultType",
                "resultLocalTerminal"
            ]);
        return new HookSubscriptionManifest(
            RequiredString(element, "event"),
            RequiredString(element, "kernel"))
        {
            // Optional for back-compat: manifests exported before index metadata omit both keys.
            IndexedPredicates = element.TryGetProperty("indexedPredicates", out var predicates)
                ? ReadIndexedPredicates(predicates)
                : [],
            IndexCoversPredicate = element.TryGetProperty("indexCoversPredicate", out var covers) &&
                ReadBoolValue(covers, "indexCoversPredicate"),
            // Optional: present only for lowered RunLocal (local-terminal) chains.
            LocalTerminal = element.TryGetProperty("localTerminal", out var localTerminal) &&
                ReadBoolValue(localTerminal, "localTerminal"),
            ProjectedType = element.TryGetProperty("projectedType", out var projectedType)
                ? ReadStringValue(projectedType, "projectedType")
                : null,
            Priority = element.TryGetProperty("priority", out var priority)
                ? ReadInt32Value(priority, "priority")
                : 0,
            ResultType = element.TryGetProperty("resultType", out var resultType)
                ? ReadStringValue(resultType, "resultType")
                : null,
            ResultLocalTerminal = element.TryGetProperty("resultLocalTerminal", out var resultLocalTerminal) &&
                ReadBoolValue(resultLocalTerminal, "resultLocalTerminal")
        };
    }

    private static IReadOnlyList<IndexedPredicate> ReadIndexedPredicates(JsonElement array)
    {
        RequireArray(array, "indexedPredicates");
        var predicates = AllocateArray<IndexedPredicate>(array, out var count);
        if (count == 0)
        {
            return predicates;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            predicates[index++] = ReadIndexedPredicate(item);
        }

        return predicates;
    }

    private static IndexedPredicate ReadIndexedPredicate(JsonElement element)
    {
        RequireAllowedProperties(element, "indexed predicate", ["path", "operator", "value", "valueType"]);
        var valueType = RequiredString(element, "valueType");
        // Unlike a live-setting default, an index predicate value is the constant being compared against —
        // null is not a valid index value (and the schema forbids it), so reject it rather than carry a
        // null the host can never match.
        var value = ReadLiveSettingValue(Required(element, "value"), valueType, "indexed predicate value")
            ?? throw Error("E-JSON-VALUE", "indexed predicate value must not be null");
        return new IndexedPredicate(
            RequiredString(element, "path"),
            ReadIndexPredicateOperator(RequiredString(element, "operator")),
            value,
            valueType);
    }

    private static IndexPredicateOperator ReadIndexPredicateOperator(string value)
    {
        if (!Enum.TryParse<IndexPredicateOperator>(value, ignoreCase: false, out var op) ||
            !Enum.IsDefined(op))
        {
            throw Error("E-JSON-OPERATOR", $"unsupported index predicate operator '{value}'");
        }

        return op;
    }

    private static KernelEntrypoints ReadEntrypoints(JsonElement element, bool requireAliases)
    {
        RequireAllowedProperties(element, "kernel entrypoints", ["shouldHandle", "handle"]);
        return new KernelEntrypoints(
            ReadEntrypointAlias(element, "shouldHandle", requireAliases, "ShouldHandle"),
            ReadEntrypointAlias(element, "handle", requireAliases, "Handle"));
    }

    private static string ReadEntrypointAlias(
        JsonElement element,
        string name,
        bool requireAlias,
        string defaultValue)
        => requireAlias ? RequiredString(element, name) : OptionalString(element, name) ?? defaultValue;
}

public static class PluginServerJsonExtensions
{
    public static ValueTask<InstalledKernel> InstallJsonAsync(
        this PluginServer server,
        string json,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        var package = PluginPackageJsonSerializer.Import(json);
        return package.Manifest.RpcEntrypoint is not null
            ? server.InstallServerExtensionAsync(package, policy, cancellationToken)
            : server.InstallAsync(package, policy, cancellationToken);
    }

    public static ValueTask<InstalledKernel> InstallJsonAsync(
        this PluginSession session,
        string json,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        var package = PluginPackageJsonSerializer.Import(json);
        return package.Manifest.RpcEntrypoint is not null
            ? session.InstallServerExtensionAsync(package, policy, cancellationToken)
            : session.InstallAsync(package, policy, cancellationToken);
    }
}
