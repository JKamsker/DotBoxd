namespace SafeIR.Serialization.Json.Internal;

using System.Text;
using System.Text.Json;
using static JsonImport;

internal static class JsonImportBudgetGuard
{
    private const int MaxDepth = 64;
    private const int MaxJsonBytes = 1_048_576;
    private const int MaxTokens = 100_000;
    private const int MaxContainerItems = 10_000;
    private const int MaxStringBytes = 65_536;
    private const int MaxTotalStringBytes = 524_288;

    public static void Validate(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var utf8 = Encoding.UTF8.GetBytes(json);
        if (utf8.Length > MaxJsonBytes)
        {
            throw Error("E-JSON-LIMIT", "JSON IR exceeds maximum byte length");
        }

        Scan(utf8);
    }

    private static void Scan(byte[] utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaxDepth + 1
        });
        var stack = new ContainerFrame[MaxDepth];
        var depth = -1;
        var tokens = 0;
        long totalStringBytes = 0;
        while (reader.Read())
        {
            tokens++;
            if (tokens > MaxTokens)
            {
                throw Error("E-JSON-LIMIT", "JSON IR exceeds maximum token count");
            }

            HandleToken(reader.TokenType, reader.ValueSpan.Length, stack, ref depth, ref totalStringBytes);
        }
    }

    private static void HandleToken(
        JsonTokenType token,
        int valueBytes,
        ContainerFrame[] stack,
        ref int depth,
        ref long totalStringBytes)
    {
        switch (token)
        {
            case JsonTokenType.StartObject:
                CountArrayItem(stack, depth);
                Push(stack, ref depth, ContainerKind.Object);
                break;
            case JsonTokenType.StartArray:
                CountArrayItem(stack, depth);
                Push(stack, ref depth, ContainerKind.Array);
                break;
            case JsonTokenType.EndObject or JsonTokenType.EndArray:
                depth--;
                break;
            case JsonTokenType.PropertyName:
                CountObjectProperty(stack, depth);
                CountStringBytes(valueBytes, ref totalStringBytes);
                break;
            case JsonTokenType.String:
                CountArrayItem(stack, depth);
                CountStringBytes(valueBytes, ref totalStringBytes);
                break;
            default:
                CountArrayItem(stack, depth);
                break;
        }
    }

    private static void Push(ContainerFrame[] stack, ref int depth, ContainerKind kind)
    {
        depth++;
        if (depth >= stack.Length)
        {
            throw Error("E-JSON-LIMIT", "JSON IR exceeds maximum depth");
        }

        stack[depth] = new ContainerFrame(kind, 0);
    }

    private static void CountArrayItem(ContainerFrame[] stack, int depth)
    {
        if (depth < 0 || stack[depth].Kind != ContainerKind.Array)
        {
            return;
        }

        IncrementCount(stack, depth, "array");
    }

    private static void CountObjectProperty(ContainerFrame[] stack, int depth)
    {
        if (depth < 0 || stack[depth].Kind != ContainerKind.Object)
        {
            return;
        }

        IncrementCount(stack, depth, "object");
    }

    private static void IncrementCount(ContainerFrame[] stack, int depth, string container)
    {
        var frame = stack[depth] with { Count = stack[depth].Count + 1 };
        if (frame.Count > MaxContainerItems)
        {
            throw Error("E-JSON-LIMIT", $"JSON IR {container} exceeds maximum breadth");
        }

        stack[depth] = frame;
    }

    private static void CountStringBytes(int bytes, ref long totalStringBytes)
    {
        if (bytes > MaxStringBytes)
        {
            throw Error("E-JSON-LIMIT", "JSON IR string exceeds maximum byte length");
        }

        totalStringBytes += bytes;
        if (totalStringBytes > MaxTotalStringBytes)
        {
            throw Error("E-JSON-LIMIT", "JSON IR exceeds maximum total string byte length");
        }
    }

    private enum ContainerKind
    {
        Object,
        Array
    }

    private readonly record struct ContainerFrame(ContainerKind Kind, int Count);
}
