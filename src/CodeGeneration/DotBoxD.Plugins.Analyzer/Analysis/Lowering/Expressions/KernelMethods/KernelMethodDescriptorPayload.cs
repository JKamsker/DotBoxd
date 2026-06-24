using System.Security.Cryptography;
using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal sealed record KernelMethodDescriptorPayload(
    int Version,
    string ContextType,
    string MethodMetadataName,
    string NormalizedSignature,
    string ReturnType,
    bool Allocates,
    EquatableArray<string> Capabilities,
    EquatableArray<string> Effects,
    EquatableArray<KernelMethodDescriptorParameter> Parameters,
    string Source)
{
    public const int CurrentVersion = 1;

    public string ToJson()
    {
        var builder = new StringBuilder();
        builder.Append('{');
        AppendProperty(builder, "allocates", Allocates ? "true" : "false");
        AppendProperty(builder, "capabilities", StringArray(Capabilities));
        AppendProperty(builder, "contextType", JsonString(ContextType));
        AppendProperty(builder, "effects", StringArray(Effects));
        AppendProperty(builder, "methodMetadataName", JsonString(MethodMetadataName));
        AppendProperty(builder, "normalizedSignature", JsonString(NormalizedSignature));
        AppendProperty(builder, "parameters", ParametersJson());
        AppendProperty(builder, "returnType", JsonString(ReturnType));
        AppendProperty(builder, "source", JsonString(Source));
        AppendProperty(builder, "version", Version.ToString(System.Globalization.CultureInfo.InvariantCulture), last: true);
        builder.Append('}');
        return builder.ToString();
    }

    public static string Hash(string payload)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static bool TryParse(string payload, out KernelMethodDescriptorPayload? descriptor)
        => KernelMethodDescriptorPayloadParser.TryParse(payload, out descriptor);

    private static void AppendProperty(StringBuilder builder, string name, string value, bool last = false)
    {
        builder.Append(JsonString(name)).Append(':').Append(value);
        if (!last)
        {
            builder.Append(',');
        }
    }

    private string ParametersJson()
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < Parameters.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append('{');
            AppendProperty(builder, "placeholder", JsonString(Parameters[i].Placeholder));
            AppendProperty(builder, "type", JsonString(Parameters[i].Type), last: true);
            builder.Append('}');
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static string StringArray(EquatableArray<string> values)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(JsonString(values[i]));
        }

        builder.Append(']');
        return builder.ToString();
    }

    internal static string JsonString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString()
            });
        }

        builder.Append('"');
        return builder.ToString();
    }
}

internal sealed record KernelMethodDescriptorParameter(string Placeholder, string Type);
