namespace SafeIR.Plugins;

using System.Buffers;
using System.Text;
using System.Text.Json;
using SafeIR.Serialization.Json.Internal;
using static SafeIR.JsonImport;

public static class PluginPackageJsonSerializer
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
            PluginPackageValidator.Validate(package);
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

    private static void WritePackage(Utf8JsonWriter writer, PluginPackage package)
    {
        writer.WriteStartObject();
        WriteManifest(writer, package.Manifest);
        writer.WritePropertyName("entrypoints");
        WriteEntrypoints(writer, package.Entrypoints);
        writer.WritePropertyName("module");
        SafeIrJsonExporter.Write(writer, package.Module);
        writer.WriteEndObject();
    }

    private static void WriteManifest(Utf8JsonWriter writer, PluginManifest manifest)
    {
        writer.WritePropertyName("manifest");
        writer.WriteStartObject();
        writer.WriteString("pluginId", manifest.PluginId);
        writer.WriteString("contract", manifest.Contract);
        writer.WriteString("mode", manifest.Mode.ToString());
        WriteStringArray(writer, "effects", manifest.Effects);
        WriteLiveSettings(writer, manifest.LiveSettings);
        WriteSubscriptions(writer, manifest.Subscriptions);
        writer.WriteEndObject();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyList<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values) {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteLiveSettings(
        Utf8JsonWriter writer,
        IReadOnlyList<LiveSettingDefinition> settings)
    {
        writer.WritePropertyName("liveSettings");
        writer.WriteStartArray();
        foreach (var setting in settings) {
            writer.WriteStartObject();
            writer.WriteString("name", setting.Name);
            writer.WriteString("type", setting.Type);
            writer.WritePropertyName("defaultValue");
            WriteLiveSettingValue(writer, setting.DefaultValue, "defaultValue");
            if (setting.Min is not null) {
                writer.WritePropertyName("min");
                WriteLiveSettingValue(writer, setting.Min, "min");
            }

            if (setting.Max is not null) {
                writer.WritePropertyName("max");
                WriteLiveSettingValue(writer, setting.Max, "max");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLiveSettingValue(Utf8JsonWriter writer, object? value, string name)
    {
        switch (value) {
            case null:
                writer.WriteNullValue();
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case int integer:
                writer.WriteNumberValue(integer);
                break;
            case long integer:
                writer.WriteNumberValue(integer);
                break;
            case double number when double.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case float number when float.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            default:
                throw Error("E-JSON-EXPORT", $"live setting value '{name}' must be a JSON scalar");
        }
    }

    private static void WriteSubscriptions(
        Utf8JsonWriter writer,
        IReadOnlyList<HookSubscriptionManifest> subscriptions)
    {
        writer.WritePropertyName("subscriptions");
        writer.WriteStartArray();
        foreach (var subscription in subscriptions) {
            writer.WriteStartObject();
            writer.WriteString("event", subscription.Event);
            writer.WriteString("kernel", subscription.Kernel);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteEntrypoints(Utf8JsonWriter writer, KernelEntrypoints entrypoints)
    {
        writer.WriteStartObject();
        writer.WriteString("shouldHandle", entrypoints.ShouldHandle);
        writer.WriteString("handle", entrypoints.Handle);
        writer.WriteEndObject();
    }

    private static PluginPackage ReadPackage(JsonElement element)
    {
        RequireAllowedProperties(element, "plugin package", ["manifest", "module", "entrypoints"]);
        var manifest = ReadManifest(Required(element, "manifest"));
        var moduleElement = Required(element, "module");
        var module = SafeIrJsonImporter.Import(moduleElement, moduleElement.GetRawText());
        var entrypoints = element.TryGetProperty("entrypoints", out var entrypointElement)
            ? ReadEntrypoints(entrypointElement)
            : null;

        return PluginPackage.Create(manifest, module, entrypoints);
    }

    private static PluginManifest ReadManifest(JsonElement element)
    {
        RequireAllowedProperties(
            element,
            "plugin manifest",
            ["pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions"]);

        return new PluginManifest(
            RequiredString(element, "pluginId"),
            RequiredString(element, "contract"),
            ReadExecutionMode(RequiredString(element, "mode")),
            ReadStringArray(RequiredArray(element, "effects"), "effects"),
            ReadLiveSettings(RequiredArray(element, "liveSettings")),
            ReadSubscriptions(RequiredArray(element, "subscriptions")));
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
        var values = AllocateArray<string>(array, out var count);
        if (count == 0) {
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
        if (count == 0) {
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
        if (count == 0) {
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
        RequireAllowedProperties(element, "hook subscription", ["event", "kernel"]);
        return new HookSubscriptionManifest(
            RequiredString(element, "event"),
            RequiredString(element, "kernel"));
    }

    private static KernelEntrypoints ReadEntrypoints(JsonElement element)
    {
        RequireAllowedProperties(element, "kernel entrypoints", ["shouldHandle", "handle"]);
        return new KernelEntrypoints(
            OptionalString(element, "shouldHandle") ?? "ShouldHandle",
            OptionalString(element, "handle") ?? "Handle");
    }
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
        return server.InstallAsync(PluginPackageJsonSerializer.Import(json), policy, cancellationToken);
    }
}
