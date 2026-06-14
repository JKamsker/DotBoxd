namespace SafeIR.Transport.Http.Internal;

public static class SafeHttpHostBuilderExtensions
{
    public static SandboxHostBuilder AddNetworkBindings(
        this SandboxHostBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
        => SafeIR.Transport.Http.SafeHttpHostBuilderExtensions.AddNetworkBindings(
            builder,
            invoker,
            dnsResolver);
}
