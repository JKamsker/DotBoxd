using SafeIR.Runtime;

namespace SafeIR.Tests;

/// <summary>
/// ALG-0010: compiled binding dispatch recursively re-validates every argument value on
/// every call, even though the compiled IR was already verified against the binding
/// signature. The finding's better target is to trust the compiled type proof on the hot
/// path and, at most, shallow-check the wrapper kind/declared element metadata instead of
/// walking the entire collection per call.
///
/// These tests drive <see cref="CompiledRuntime.CallBinding"/> directly with a list
/// argument whose declared element type (<c>List&lt;I32&gt;</c>) matches the binding
/// parameter, so a shallow wrapper-type check passes. Because the per-call dispatcher today
/// performs a FULL recursive walk via SandboxValueValidator.RequireType, it descends into
/// the elements and rejects the call before the binding body runs.
///
/// <see cref="Compiled_binding_call_does_not_recursively_revalidate_list_argument_elements"/>
/// is RED now (the dispatcher throws ValidationError on the nested element) and turns GREEN
/// once the unconditional per-call recursive element walk is removed/reduced. The companion
/// test pins the boundary the fix must keep: a genuine wrapper-type mismatch is still caught.
/// </summary>
public sealed class Fix_ALG_0010_Tests
{
    private const string BindingId = "test.consume_list";

    [Fact]
    public void Compiled_binding_call_does_not_recursively_revalidate_list_argument_elements()
    {
        var invoked = false;
        var context = Context(ListConsumingBinding(() => invoked = true));

        // Declared element type is I32 (matches the binding parameter and any shallow
        // wrapper-type check), but one element carries a different runtime kind. Only a
        // FULL recursive per-call re-walk inspects elements and would reject this.
        var listArg = SandboxValue.FromList(
            new SandboxValue[]
            {
                SandboxValue.FromInt32(1),
                SandboxValue.FromInt64(2),
                SandboxValue.FromInt32(3),
            },
            SandboxType.I32);

        // RED today: the dispatcher recursively revalidates the argument and throws
        // SandboxRuntimeException(ValidationError) before the binding runs. After the fix,
        // the compiled type proof is trusted on the hot path and the binding is invoked.
        var result = CompiledRuntime.CallBinding(context, BindingId, new[] { listArg });

        Assert.True(invoked, "binding body must run instead of being blocked by a per-call recursive argument re-walk");
        Assert.Equal(SandboxType.I32, result.Type);
    }

    [Fact]
    public void Compiled_binding_call_still_rejects_a_wrong_wrapper_type_argument()
    {
        var invoked = false;
        var context = Context(ListConsumingBinding(() => invoked = true));

        // A scalar where the binding declares List<I32> is a wrapper-kind mismatch. This
        // must stay rejected before the binding runs even after the recursive element walk
        // is removed, so a shallow wrapper-type check is still required.
        var scalarArg = SandboxValue.FromInt32(42);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.CallBinding(context, BindingId, new[] { scalarArg }));

        Assert.Equal(SandboxErrorCode.ValidationError, error.Error.Code);
        Assert.False(invoked, "binding body must not run when the argument wrapper type does not match");
    }

    private static BindingDescriptor ListConsumingBinding(Action onInvoke)
        => new(
            BindingId,
            SemVersion.One,
            new[] { SandboxType.List(SandboxType.I32) },
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) =>
            {
                onInvoke();
                return ValueTask.FromResult(SandboxValue.FromInt32(0));
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static SandboxContext Context(BindingDescriptor binding)
    {
        var limits = new ResourceLimits(MaxFuel: 1_000_000, MaxAllocatedBytes: 1_000_000);
        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Add(binding).Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }
}
