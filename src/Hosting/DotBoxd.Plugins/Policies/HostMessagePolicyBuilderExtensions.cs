namespace DotBoxd.Plugins;

using System.Globalization;
using DotBoxd.Kernels;

public static class HostMessagePolicyBuilderExtensions
{
    public static SandboxPolicyBuilder GrantHostMessageWrite(this SandboxPolicyBuilder builder)
        => builder.GrantHostMessageWrite(allowedTargets: null, targetPrefixes: null, maxMessageLength: null);

    public static SandboxPolicyBuilder GrantHostMessageWrite(
        this SandboxPolicyBuilder builder,
        IEnumerable<string>? allowedTargets = null,
        IEnumerable<string>? targetPrefixes = null,
        int? maxMessageLength = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        AddCsvParameter(parameters, "allowedTargets", allowedTargets);
        AddCsvParameter(parameters, "targetPrefixes", targetPrefixes);
        if (maxMessageLength is { } limit)
        {
            if (limit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxMessageLength));
            }

            parameters["maxMessageLength"] = limit.ToString(CultureInfo.InvariantCulture);
        }

        return builder.Grant(
            PluginMessageBindings.CapabilityId,
            parameters,
            SandboxEffect.HostStateWrite | SandboxEffect.Audit);
    }

    private static void AddCsvParameter(
        IDictionary<string, string> parameters,
        string key,
        IEnumerable<string>? values)
    {
        if (values is null) return;

        var normalized = values
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"{key} must contain at least one non-empty value", key);
        }

        if (normalized.Any(value => value.Contains(',')))
        {
            throw new ArgumentException($"{key} values must not contain commas", key);
        }

        parameters[key] = string.Join(',', normalized);
    }
}
