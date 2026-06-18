namespace DotBoxD.Kernels;

internal sealed class ExecutionPlanEntrypointMetadata(
    IReadOnlyList<string> requiredCapabilities,
    bool hasAsyncBinding,
    bool hasHostBinding)
{
    public IReadOnlyList<string> RequiredCapabilities { get; } = requiredCapabilities;

    public bool HasAsyncBinding { get; } = hasAsyncBinding;

    public bool HasHostBinding { get; } = hasHostBinding;
}
