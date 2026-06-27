namespace DotBoxD.Kernels.Sandbox.Values;

internal sealed class SandboxTraversalState<TFrame>
{
    private const int MaxRetainedCapacity = 256;

    [ThreadStatic]
    private static SandboxTraversalState<TFrame>? s_cached;

    private SandboxTraversalState()
    {
    }

    public HashSet<object> Active { get; } = new(ReferenceEqualityComparer.Instance);

    public Stack<TFrame> Stack { get; } = new();

    public static SandboxTraversalState<TFrame> Rent()
    {
        var state = s_cached;
        if (state is null)
        {
            return new SandboxTraversalState<TFrame>();
        }

        s_cached = null;
        return state;
    }

    public static void Return(SandboxTraversalState<TFrame> state)
    {
        state.Active.Clear();
        state.Stack.Clear();
        if (state.Active.EnsureCapacity(0) > MaxRetainedCapacity ||
            state.Stack.EnsureCapacity(0) > MaxRetainedCapacity)
        {
            return;
        }

        s_cached = state;
    }
}
