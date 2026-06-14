namespace SafeIR.Plugins;

using System.Text.Json;
using SafeIR.Serialization.Json;
using static SafeIR.JsonImport;

/// <summary>
/// The write (export) half of <see cref="PluginPackageJsonSerializer"/>. The read half (with the strict
/// <c>RequireAllowedProperties</c> shape that the schema-sync regression pins) stays in the main file.
/// The optional <see cref="PluginManifest.RpcEntrypoint"/> is emitted only for kernel RPC service kernels.
/// </summary>
public static partial class PluginPackageJsonSerializer
{
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
        WriteStringArray(writer, "requiredCapabilities", manifest.RequiredCapabilities);
        if (manifest.RpcEntrypoint is { } rpcEntrypoint)
        {
            writer.WriteString("rpcEntrypoint", rpcEntrypoint);
        }

        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteLiveSettings(Utf8JsonWriter writer, IReadOnlyList<LiveSettingDefinition> settings)
    {
        writer.WritePropertyName("liveSettings");
        writer.WriteStartArray();
        foreach (var setting in settings)
        {
            writer.WriteStartObject();
            writer.WriteString("name", setting.Name);
            writer.WriteString("type", setting.Type);
            writer.WritePropertyName("defaultValue");
            WriteLiveSettingValue(writer, setting.DefaultValue, "defaultValue");
            if (setting.Min is not null)
            {
                writer.WritePropertyName("min");
                WriteLiveSettingValue(writer, setting.Min, "min");
            }

            if (setting.Max is not null)
            {
                writer.WritePropertyName("max");
                WriteLiveSettingValue(writer, setting.Max, "max");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLiveSettingValue(Utf8JsonWriter writer, object? value, string name)
    {
        switch (value)
        {
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

    private static void WriteSubscriptions(Utf8JsonWriter writer, IReadOnlyList<HookSubscriptionManifest> subscriptions)
    {
        writer.WritePropertyName("subscriptions");
        writer.WriteStartArray();
        foreach (var subscription in subscriptions)
        {
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
}
