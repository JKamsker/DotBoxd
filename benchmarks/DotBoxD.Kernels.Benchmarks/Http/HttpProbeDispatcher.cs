namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class HttpProbeDispatcher
{
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--probe-http-metadata", StringComparer.OrdinalIgnoreCase))
        {
            HttpMetadataAccountingProbe.Run();
            return true;
        }

        if (args.Contains("--probe-http-request-bytes", StringComparer.OrdinalIgnoreCase))
        {
            HttpRequestByteAccountingProbe.Run();
            return true;
        }

        if (args.Contains("--probe-http-allowed-host", StringComparer.OrdinalIgnoreCase))
        {
            HttpAllowedHostProbe.Run();
            return true;
        }

        if (args.Contains("--probe-http-audit-path-sanitizer", StringComparer.OrdinalIgnoreCase))
        {
            HttpAuditPathSanitizerProbe.Run();
            return true;
        }

        if (args.Contains("--probe-safe-ip-classifier", StringComparer.OrdinalIgnoreCase))
        {
            SafeIpClassifierProbe.Run();
            return true;
        }

        if (args.Contains("--probe-http-redirect-validation", StringComparer.OrdinalIgnoreCase))
        {
            HttpRedirectValidationProbe.Run();
            return true;
        }

        return false;
    }
}
