namespace DotBoxd.Hosting.Http.Internal;

using DotBoxd.Kernels;

public static class SafeHttpBindingExtensions
{
    public static BindingRegistryBuilder AddNetworkBindings(
        this BindingRegistryBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
        => DotBoxd.Hosting.Http.SafeHttpBindingExtensions.AddNetworkBindings(
            builder,
            invoker,
            dnsResolver);
}
