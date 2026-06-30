using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Execution;

internal readonly record struct HostServiceBindingRegistration(
    BindingDescriptor Descriptor,
    HostServiceBindingRouteSignature Signature);
