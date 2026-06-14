namespace DotBoxd.Hosting.Http.Internal;

public static class SafeHttpHostBuilderExtensions
{
    public static SandboxHostBuilder AddNetworkBindings(
        this SandboxHostBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
        => DotBoxd.Hosting.Http.SafeHttpHostBuilderExtensions.AddNetworkBindings(
            builder,
            invoker,
            dnsResolver);
}
