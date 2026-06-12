using System.Text.Json;

namespace AgentQueue;

internal static class JsonOutput
{
    public static void Write(TextWriter output, object value)
    {
        output.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = false
        }));
    }
}
