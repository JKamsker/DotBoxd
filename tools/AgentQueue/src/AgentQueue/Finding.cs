namespace AgentQueue;

internal sealed class Finding
{
    public Finding(Dictionary<string, string> fields, string body, string? filePath)
    {
        Fields = fields;
        Body = body;
        FilePath = filePath;
    }

    public Dictionary<string, string> Fields { get; }

    public string Body { get; set; }

    public string? FilePath { get; set; }

    public string Id
    {
        get => Get("id");
        set => Set("id", value);
    }

    public string Area
    {
        get => Get("area");
        set => Set("area", value);
    }

    public string Status
    {
        get => Get("status");
        set => Set("status", value);
    }

    public string Priority
    {
        get => Get("priority");
        set => Set("priority", value);
    }

    public string Title
    {
        get => Get("title");
        set => Set("title", value);
    }

    public string DedupKey
    {
        get => Get("dedup_key");
        set => Set("dedup_key", value);
    }

    public string Get(string key) =>
        Fields.TryGetValue(key, out string? value) ? value : string.Empty;

    public void Set(string key, string? value) =>
        Fields[key] = (value ?? string.Empty).Trim();

    public DateTimeOffset CreatedAt
    {
        get
        {
            return DateTimeOffset.TryParse(Get("created_at"), out DateTimeOffset result)
                ? result
                : DateTimeOffset.MinValue;
        }
    }

    public static Finding Create(
        string id,
        AgentArea area,
        string priority,
        string title,
        string dedupKey,
        string agent,
        string commit,
        DateTimeOffset timestamp,
        string body)
    {
        Dictionary<string, string> fields = AgentQueueCatalog.MetadataKeys.ToDictionary(
            key => key,
            _ => string.Empty,
            StringComparer.Ordinal);

        Finding finding = new(fields, body, null)
        {
            Id = id,
            Area = area.Name,
            Status = "open",
            Priority = priority,
            Title = title,
            DedupKey = dedupKey
        };
        finding.Set("created_at", timestamp.ToString("O"));
        finding.Set("created_by", agent);
        finding.Set("created_commit", commit);
        finding.Set("updated_at", timestamp.ToString("O"));
        return finding;
    }
}
