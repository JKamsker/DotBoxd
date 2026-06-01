using System.Collections.Concurrent;

namespace ShaRPC.Core.Server;

/// <summary>
/// Default <see cref="IInstanceRegistry"/>. Backed by a single
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed on
/// <c>(serviceName, instanceId)</c>. One registry per connection.
/// </summary>
public sealed class InstanceRegistry : IInstanceRegistry
{
    internal const int DefaultMaxInstances = 1024;

    private readonly ConcurrentDictionary<(string Service, string Id), object> _entries = new();
    private readonly int _maxInstances;

    public InstanceRegistry() : this(DefaultMaxInstances) { }

    public InstanceRegistry(int maxInstances)
    {
        _maxInstances = maxInstances;
    }

    /// <inheritdoc />
    public string Register(string serviceName, object instance)
    {
        if (instance is null) throw new ArgumentNullException(nameof(instance));

        if (_entries.Count >= _maxInstances)
        {
            throw new InvalidOperationException(
                $"Instance registry limit reached ({_maxInstances}). Release unused instances before registering new ones.");
        }

        var id = Guid.NewGuid().ToString("N");
        _entries[(serviceName, id)] = instance;
        return id;
    }

    /// <inheritdoc />
    public bool TryGet(string serviceName, string instanceId, out object instance)
    {
        if (_entries.TryGetValue((serviceName, instanceId), out var value))
        {
            instance = value;
            return true;
        }
        instance = null!;
        return false;
    }

    /// <inheritdoc />
    public void Release(string serviceName, string instanceId) =>
        _entries.TryRemove((serviceName, instanceId), out _);

    /// <inheritdoc />
    public void ReleaseAll() => _entries.Clear();
}
