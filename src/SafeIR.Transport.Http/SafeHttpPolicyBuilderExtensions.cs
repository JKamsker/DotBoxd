namespace SafeIR;

using System.Globalization;

public static class SafeHttpPolicyBuilderExtensions
{
    public static SandboxPolicyBuilder GrantHttpGet(
        this SandboxPolicyBuilder builder,
        IEnumerable<string> allowedHosts,
        long maxResponseBytes,
        IEnumerable<string>? allowedSchemes = null,
        TimeSpan? timeout = null,
        bool allowIpLiterals = false,
        bool allowPrivateNetwork = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ThrowIfNegative(maxResponseBytes, nameof(maxResponseBytes));
        if (timeout is not null && timeout.Value < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var schemes = allowedSchemes?.ToArray() ?? ["https"];
        return builder.Grant(
            "net.http.get",
            new Dictionary<string, string> {
                ["allowedHosts"] = string.Join(',', allowedHosts),
                ["allowedSchemes"] = string.Join(',', schemes),
                ["maxResponseBytes"] = maxResponseBytes.ToString(CultureInfo.InvariantCulture),
                ["timeoutMs"] = ((long)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                ["allowIpLiterals"] = allowIpLiterals.ToString(CultureInfo.InvariantCulture),
                ["allowPrivateNetwork"] = allowPrivateNetwork.ToString(CultureInfo.InvariantCulture)
            },
            SandboxEffect.Network,
            limits => limits with {
                MaxNetworkBytesRead = Math.Max(limits.MaxNetworkBytesRead, maxResponseBytes)
            });
    }

    private static void ThrowIfNegative(long value, string paramName)
    {
        if (value < 0) {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }
}
