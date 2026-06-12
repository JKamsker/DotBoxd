namespace SafeIR.Runtime;

using SafeIR;

public static class SafeHttpBindings
{
    public static BindingDescriptor GetText(SafeInMemoryHttpMessageInvoker? invoker = null, SafeDnsResolver? dnsResolver = null)
        => new(
            "net.http.get",
            SemVersion.One,
            [SandboxType.Scalar("SandboxUri")],
            SandboxType.String,
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.Network,
            "net.http.get",
            BindingCostModel.Fixed(75),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            async (context, args, cancellationToken) => {
                var text = await SafeHttpClient.GetTextAsync(
                    context,
                    ((SandboxUriValue)args[0]).Value,
                    invoker,
                    dnsResolver,
                    cancellationToken).ConfigureAwait(false);
                return SandboxValue.FromString(text);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            SafeHttpGrantValidator.Validate);
}
