using System.Text;

namespace AgentQueue;

internal static class FrontmatterDocument
{
    public static Finding Read(string path)
    {
        string content = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
        if (!content.StartsWith("---\n", StringComparison.Ordinal))
        {
            throw new AgentQueueException($"Missing frontmatter start in {path}.", ExitCodes.ValidationError);
        }

        int end = content.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new AgentQueueException($"Missing frontmatter end in {path}.", ExitCodes.ValidationError);
        }

        string frontmatter = content[4..end];
        string body = content[(end + "\n---\n".Length)..];
        Dictionary<string, string> fields = new(StringComparer.Ordinal);

        foreach (string line in frontmatter.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new AgentQueueException($"Malformed frontmatter line in {path}: {line}.", ExitCodes.ValidationError);
            }

            fields[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return new Finding(fields, body, path);
    }

    public static string Write(Finding finding)
    {
        StringBuilder builder = new();
        builder.AppendLine("---");
        foreach (string key in AgentQueueCatalog.MetadataKeys)
        {
            builder.Append(key);
            builder.Append(": ");
            builder.AppendLine(NormalizeScalar(finding.Get(key)));
        }

        foreach (string key in finding.Fields.Keys.Order(StringComparer.Ordinal))
        {
            if (AgentQueueCatalog.MetadataKeys.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            builder.Append(key);
            builder.Append(": ");
            builder.AppendLine(NormalizeScalar(finding.Get(key)));
        }

        builder.AppendLine("---");
        builder.AppendLine();
        builder.Append(finding.Body.Trim());
        builder.AppendLine();
        return builder.ToString();
    }

    private static string NormalizeScalar(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
