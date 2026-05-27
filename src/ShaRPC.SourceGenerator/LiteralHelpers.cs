using System.Text;

namespace ShaRPC.SourceGenerator;

internal static class LiteralHelpers
{
    /// <summary>
    /// Escapes a value that will appear inside a regular C# string literal in generated
    /// source.
    /// </summary>
    public static string EscapeStringLiteral(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
