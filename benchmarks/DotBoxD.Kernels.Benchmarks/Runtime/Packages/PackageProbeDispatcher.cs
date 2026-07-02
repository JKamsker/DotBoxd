namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class PackageProbeDispatcher
{
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--probe-server-extension-proxy-lookup", StringComparer.OrdinalIgnoreCase))
        {
            ServerExtensionProxyLookupProbe.Run();
            return true;
        }

        if (args.Contains("--probe-server-extension-proxy-arguments", StringComparer.OrdinalIgnoreCase))
        {
            ServerExtensionProxyArgumentsProbe.Run();
            return true;
        }

        if (args.Contains("--probe-kernel-package-registry-resolve", StringComparer.OrdinalIgnoreCase))
        {
            KernelPackageRegistryResolveProbe.Run();
            return true;
        }

        if (args.Contains("--probe-host-service-arguments", StringComparer.OrdinalIgnoreCase))
        {
            HostServiceArgumentConversionProbe.Run();
            return true;
        }

        return false;
    }
}
