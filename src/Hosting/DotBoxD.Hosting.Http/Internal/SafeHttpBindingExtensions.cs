using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Http.Internal;

public static class SafeHttpBindingExtensions
{
    public static BindingRegistryBuilder AddNetworkBindings(
        this BindingRegistryBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
        => Bindings.SafeHttpBindingExtensions.AddNetworkBindings(
            builder,
            invoker,
            dnsResolver);
}
