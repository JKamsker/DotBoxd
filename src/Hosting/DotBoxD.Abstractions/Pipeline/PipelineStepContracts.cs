namespace DotBoxD.Abstractions;

/// <summary>
/// The semantic role a fluent event-pipeline method plays when the source generator lowers a chain such as
/// <c>On&lt;TEvent&gt;().Where(..).Select(..).Run(..)</c>. Marking a method with
/// <see cref="PipelineStepAttribute"/> lets the generator recognize it by role instead of by a hardcoded
/// method name, so a consumer can give a stage or terminal any name they like and still opt into lowering.
/// </summary>
/// <remarks>
/// The integer values are a wire contract with the analyzer's mirror enum
/// (<c>DotBoxD.Plugins.Analyzer.Analysis.HookChains.PipelineStepRole</c>) — never reorder or renumber them.
/// </remarks>
public enum PipelineStepRole
{
    /// <summary>The chain entry point (e.g. <c>On&lt;TEvent&gt;()</c>) that seeds the flowing element type.</summary>
    Seed = 0,

    /// <summary>A predicate stage that AND-composes into the generated <c>ShouldHandle</c> (e.g. <c>Where</c>).</summary>
    Filter = 1,

    /// <summary>A projection stage that advances the flowing element shape (e.g. <c>Select</c>).</summary>
    Projection = 2,

    /// <summary>A terminal whose handler body lowers into the generated kernel <c>Handle</c> (e.g. <c>Run</c>).</summary>
    Run = 3,

    /// <summary>A terminal whose handler stays native in-process; only the filters/projections lower (e.g. <c>RunLocal</c>).</summary>
    RunLocal = 4,

    /// <summary>A result-returning terminal whose handler lowers into the kernel <c>Handle</c> (e.g. <c>Register</c>).</summary>
    Register = 5,

    /// <summary>A result-returning terminal whose handler stays native in-process (e.g. <c>RegisterLocal</c>).</summary>
    RegisterLocal = 6,
}

/// <summary>
/// Whether a pipeline surface runs entirely in-host or ships its lowered kernel across the remote transport.
/// The integer values are a wire contract with the analyzer's mirror — never reorder or renumber them.
/// </summary>
public enum PipelineTransport
{
    /// <summary>An in-host pipeline (e.g. <c>HookPipeline</c>, <c>SubscriptionPipeline</c>).</summary>
    Local = 0,

    /// <summary>A pipeline whose lowered kernel is shipped to and verified by the remote host
    /// (e.g. <c>RemoteHookPipeline</c>, <c>RemoteSubscriptionPipeline</c>).</summary>
    Remote = 1,
}

/// <summary>
/// Marks a fluent pipeline method with the <see cref="PipelineStepRole"/> the source generator should lower
/// it as, replacing hardcoded method-name recognition (<c>Where</c>/<c>Select</c>/<c>Run</c>/<c>Register</c>/…).
/// This is opt-in sugar: a consumer can mark methods on their own pipeline type with the same attribute to
/// participate in lowering, and the generated output only ever calls public API a consumer could hand-write.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PipelineStepAttribute(PipelineStepRole role) : Attribute
{
    /// <summary>The role this method plays in a lowered event pipeline.</summary>
    public PipelineStepRole Role { get; } = role;
}

/// <summary>
/// Marks a fluent pipeline/stage type as a recognized event-pipeline surface, replacing the generator's
/// hardcoded receiver-type allow-list. The <see cref="Transport"/> selects whether a lowered chain rooted on
/// this type installs as an in-host or remote kernel.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class PipelineSurfaceAttribute(PipelineTransport transport) : Attribute
{
    /// <summary>Whether chains rooted on this surface run in-host or ship to the remote host.</summary>
    public PipelineTransport Transport { get; } = transport;
}
