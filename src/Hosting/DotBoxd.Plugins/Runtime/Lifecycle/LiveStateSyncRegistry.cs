namespace DotBoxd.Plugins;

internal sealed class LiveStateSyncRegistry(Func<Type, LiveUpdateMode> getUpdateMode)
{
    private readonly List<LiveStateSynchronizer> _synchronizers = [];

    public void Register(Type stateType, Action synchronize)
        => _synchronizers.Add(new LiveStateSynchronizer(stateType, synchronize));

    public IReadOnlyList<Action> SynchronizeForInput()
    {
        var deferredUpdates = new List<Action>();
        foreach (var synchronizer in _synchronizers)
        {
            var mode = getUpdateMode(synchronizer.StateType);
            if ((mode & LiveUpdateMode.AsyncSet) == LiveUpdateMode.AsyncSet)
            {
                deferredUpdates.Add(synchronizer.Synchronize);
                continue;
            }

            synchronizer.Synchronize();
        }

        return deferredUpdates;
    }

    public void SynchronizeForFlush()
    {
        foreach (var synchronizer in _synchronizers)
        {
            if ((getUpdateMode(synchronizer.StateType) & LiveUpdateMode.AsyncSet) == LiveUpdateMode.AsyncSet)
            {
                synchronizer.Synchronize();
            }
        }
    }

    private sealed record LiveStateSynchronizer(Type StateType, Action Synchronize);
}
