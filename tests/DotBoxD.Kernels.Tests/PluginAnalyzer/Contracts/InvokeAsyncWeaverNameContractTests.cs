using System.Runtime.CompilerServices;
using AnalyzerTypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;
using WeaverNames = DotBoxD.Plugins.Fody.DotBoxDInvokeAsyncWeaverNames;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

/// <summary>
/// Pins the string names the Fody InvokeAsync weaver resolves by hand at IL-weave time. Two kinds of
/// drift are guarded: (1) BCL member names the weaver matches (async state-machine + compiler-generated
/// attributes, the state-machine MoveNext, the delegate Target getter) are anchored to the real
/// reflection metadata; (2) the cross-generator contract — the weaver's generated-interceptors namespace
/// must stay identical to the one the source generator emits into, since the weaver rewrites the types
/// the generator produced. A mismatch would make weaving silently no-op.
/// </summary>
public sealed class InvokeAsyncWeaverNameContractTests
{
    [Fact]
    public void Bcl_attribute_full_names_match_reflection_metadata()
    {
        Assert.Equal(WeaverNames.AsyncStateMachineAttribute, typeof(AsyncStateMachineAttribute).FullName);
        Assert.Equal(WeaverNames.CompilerGeneratedAttribute, typeof(CompilerGeneratedAttribute).FullName);
    }

    [Fact]
    public void State_machine_move_next_name_matches()
        => Assert.Equal(nameof(IAsyncStateMachine.MoveNext), WeaverNames.MoveNextMethodName);

    [Fact]
    public void Delegate_target_getter_name_matches()
        => Assert.Equal("get_" + nameof(System.Delegate.Target), WeaverNames.DelegateTargetGetterName);

    [Fact]
    public void Generated_interceptors_namespace_matches_source_generator()
        => Assert.Equal(AnalyzerTypeNames.GeneratedInterceptorsNamespace, WeaverNames.GeneratedInterceptorsNamespace);

    [Fact]
    public void Generated_interceptors_full_name_is_namespace_dot_type()
        => Assert.Equal(
            WeaverNames.GeneratedInterceptorsNamespace + "." + WeaverNames.GeneratedInterceptorsTypeName,
            WeaverNames.GeneratedInterceptorsFullName);
}
