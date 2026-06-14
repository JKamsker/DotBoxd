namespace SafeIR.Hosting.Internal;

using SafeIR;

internal sealed class AutoExecutionHotness
{
    // The mode selector only needs recent execution history, so the table is bounded
    // with an LRU policy to keep host memory proportional to active plan-entrypoints
    // instead of every plan hash ever prepared (see PAL-0030).
    internal const int DefaultMaxEntries = 4096;

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<AutoHotnessState>> _states =
        new(StringComparer.Ordinal);
    private readonly LinkedList<AutoHotnessState> _recency = new();
    private readonly int _maxEntries;

    public AutoExecutionHotness()
        : this(DefaultMaxEntries)
    {
    }

    internal AutoExecutionHotness(int maxEntries)
    {
        if (maxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        _maxEntries = maxEntries;
    }

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _states.Count;
            }
        }
    }

    public AutoHotnessAttempt BeginAttempt(ExecutionPlan plan, string entrypoint)
    {
        var state = Touch(Key(plan.PlanHash, entrypoint), plan.PlanHash, entrypoint);
        return state.BeginAttempt();
    }

    private AutoHotnessState Touch(string key, string planHash, string entrypoint)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(key, out var existing))
            {
                _recency.Remove(existing);
                _recency.AddLast(existing);
                return existing.Value;
            }

            var node = _recency.AddLast(new AutoHotnessState(planHash, entrypoint));
            _states.Add(key, node);
            EvictIfNeeded(key);
            return node.Value;
        }
    }

    private void EvictIfNeeded(string addedKey)
    {
        while (_states.Count > _maxEntries)
        {
            var oldest = _recency.First;
            if (oldest is null)
            {
                return;
            }

            var oldestKey = Key(oldest.Value.PlanHash, oldest.Value.Entrypoint);
            if (StringComparer.Ordinal.Equals(oldestKey, addedKey))
            {
                // Never evict the entry we just added in response to this attempt.
                return;
            }

            _recency.RemoveFirst();
            _states.Remove(oldestKey);
        }
    }

    private static string Key(string planHash, string entrypoint)
        => planHash + "|" + entrypoint;
}

internal sealed class AutoHotnessAttempt(
    AutoHotnessState state,
    ModuleHotnessStats stats)
{
    public ModuleHotnessStats Stats { get; } = stats;

    public void Complete(SandboxExecutionResult result, TimeSpan elapsed)
        => state.RecordResult(result, elapsed);
}

internal sealed class AutoHotnessState(string planHash, string entrypoint)
{
    public string PlanHash { get; } = planHash;

    public string Entrypoint { get; } = entrypoint;

    private readonly object _gate = new();
    private int _runCount;
    private int _completedRunCount;
    private int _interpretedRunCount;
    private long _totalFuelUsed;
    private long _interpretedDurationTicks;
    private DateTimeOffset? _lastRunAt;
    private int _compileFailures;
    private string? _lastCompiledArtifactHash;

    public AutoHotnessAttempt BeginAttempt()
    {
        lock (_gate)
        {
            if (_runCount < int.MaxValue)
            {
                _runCount++;
            }

            return new AutoHotnessAttempt(this, Snapshot());
        }
    }

    public void RecordResult(SandboxExecutionResult result, TimeSpan elapsed)
    {
        lock (_gate)
        {
            if (_completedRunCount < int.MaxValue)
            {
                _completedRunCount++;
            }

            _totalFuelUsed = SaturatingAdd(_totalFuelUsed, result.ResourceUsage.FuelUsed);
            if (result.ActualMode == ExecutionMode.Interpreted)
            {
                if (_interpretedRunCount < int.MaxValue)
                {
                    _interpretedRunCount++;
                }

                _interpretedDurationTicks = SaturatingAdd(_interpretedDurationTicks, elapsed.Ticks);
            }

            if (IsCompileFailure(result) && _compileFailures < int.MaxValue)
            {
                _compileFailures++;
            }

            if (result.ActualMode == ExecutionMode.Compiled && !string.IsNullOrWhiteSpace(result.ArtifactHash))
            {
                _lastCompiledArtifactHash = result.ArtifactHash;
            }

            _lastRunAt = DateTimeOffset.UtcNow;
        }
    }

    private ModuleHotnessStats Snapshot()
    {
        var averageFuel = _completedRunCount == 0 ? 0 : _totalFuelUsed / _completedRunCount;
        var averageInterpretedTicks = _interpretedRunCount == 0 ? 0 : _interpretedDurationTicks / _interpretedRunCount;
        return new ModuleHotnessStats(
            PlanHash,
            Entrypoint,
            _runCount,
            _completedRunCount,
            TimeSpan.FromTicks(averageInterpretedTicks),
            averageFuel,
            _lastRunAt,
            _compileFailures,
            _lastCompiledArtifactHash);
    }

    private static bool IsCompileFailure(SandboxExecutionResult result)
        => !result.Succeeded &&
           ((!result.ExecutionDispatched && result.ActualMode == ExecutionMode.Compiled) ||
            result.AuditEvents.Any(e => e.Kind == "ExecutionFallback"));

    private static long SaturatingAdd(long left, long right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }
}
