namespace SafeIR.Runtime;

using SafeIR;

public static class DefaultSandboxBindings
{
    public static BindingRegistryBuilder AddDefaultPureBindings(this BindingRegistryBuilder builder)
        => builder.AddRange(MathBindings.All).AddRange(StringBindings.All);

    public static BindingRegistryBuilder AddFileBindings(this BindingRegistryBuilder builder)
        => builder.Add(SafeFileBindings.ReadText).Add(SafeFileBindings.WriteText);

    public static BindingRegistryBuilder AddTimeBindings(this BindingRegistryBuilder builder)
        => builder.Add(SafeTimeBindings.NowUnixMillis);

    public static BindingRegistryBuilder AddRandomBindings(this BindingRegistryBuilder builder)
        => builder.Add(SafeRandomBindings.NextI32);

    public static BindingRegistryBuilder AddNetworkBindings(
        this BindingRegistryBuilder builder,
        SafeInMemoryHttpMessageInvoker? invoker = null,
        SafeDnsResolver? dnsResolver = null)
        => builder.Add(SafeHttpBindings.GetText(invoker, dnsResolver));

    public static BindingRegistryBuilder AddLogBindings(this BindingRegistryBuilder builder)
        => builder.Add(SafeLogBindings.Info).Add(SafeLogBindings.Warn);
}
