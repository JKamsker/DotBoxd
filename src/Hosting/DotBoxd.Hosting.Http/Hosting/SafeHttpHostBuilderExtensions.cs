namespace DotBoxd.Hosting.Http;

using DotBoxd.Kernels.Runtime;
using DotBoxd.Hosting.Http.Internal;

public static class SafeHttpHostBuilderExtensions
{
    public static SandboxHostBuilder AddNetworkBindings(
        this SandboxHostBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddBinding(SafeHttpBindings.GetText(invoker, dnsResolver));
        return builder;
    }
}
