using System.Security.Cryptography;
using System.Text;

namespace DotBoxd.Kernels;

internal static class CanonicalEncoding
{
    public static string Record(params string?[] fields)
        => Record((IEnumerable<string?>)fields);

    public static string Record(IEnumerable<string?> fields)
    {
        var builder = new StringBuilder();
        AppendRecord(builder, fields);
        return builder.ToString();
    }

    public static void AppendRecord(StringBuilder builder, IEnumerable<string?> fields)
    {
        foreach (var field in fields) {
            AppendField(builder, field);
        }
    }

    public static string HashRecords(IEnumerable<string> records)
    {
        var text = string.Join('\n', records);
        return HashText(text);
    }

    public static string HashRecord(string record)
        => HashText(record);

    private static string HashText(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void AppendField(StringBuilder builder, string? value)
    {
        if (value is null) {
            builder.Append('n').Append(';');
            return;
        }

        var escaped = Escape(value);
        builder.Append('s').Append(escaped.Length).Append(':').Append(escaped).Append(';');
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value) {
            AppendEscaped(builder, character);
        }

        return builder.ToString();
    }

    private static void AppendEscaped(StringBuilder builder, char character)
    {
        switch (character) {
            case '\\':
                builder.Append(@"\\");
                break;
            case '\r':
                builder.Append(@"\r");
                break;
            case '\n':
                builder.Append(@"\n");
                break;
            case '\t':
                builder.Append(@"\t");
                break;
            default:
                if (char.IsControl(character)) {
                    builder.Append(@"\u");
                    builder.Append(((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                }

                builder.Append(character);
                break;
        }
    }
}
