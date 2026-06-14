namespace SafeIR.Transport.Http.Internal;

using SafeIR;

public static class SafeHttpBindingExtensions
{
    public static BindingRegistryBuilder AddNetworkBindings(
        this BindingRegistryBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
        => SafeIR.Transport.Http.SafeHttpBindingExtensions.AddNetworkBindings(
            builder,
            invoker,
            dnsResolver);
}
