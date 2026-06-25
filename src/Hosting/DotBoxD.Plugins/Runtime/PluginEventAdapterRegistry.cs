using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Plugins.Runtime;

public sealed class PluginEventAdapterRegistry
{
    private readonly Dictionary<Type, RegisteredPluginEventAdapter> _adapters = [];

    public void Register<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        var parameters = adapter.Parameters;
        PluginEventValueWriterShapeValidator.Validate(adapter, parameters);
        var shape = new PluginEventShape(adapter.EventName, parameters);
        ValidateEventNameShape(typeof(TEvent), shape);
        // Capture the type-erased wire closure here — the single store site both the explicit Register path and
        // the lazy Resolve auto-register path flow through — so the router can wire by event name with no
        // reflection, over the SAME adapter instance Resolve returns (preserving pipeline adapter identity).
        _adapters[typeof(TEvent)] = new(adapter, shape, new ErasedPluginEventAdapter<TEvent>(adapter));
    }

    public IPluginEventAdapter<TEvent> Resolve<TEvent>()
    {
        if (_adapters.TryGetValue(typeof(TEvent), out var registered))
        {
            return (IPluginEventAdapter<TEvent>)registered.Adapter;
        }

        var discovered = TryDiscoverAdapter<TEvent>() ?? ConventionEventAdapter<TEvent>.Create();
        Register(discovered);
        return discovered;
    }

    internal bool TryResolveShape(string eventName, out PluginEventShape shape)
    {
        if (TryResolveRegistered(eventName, out var registered))
        {
            shape = registered.Shape;
            return true;
        }

        shape = default!;
        return false;
    }

    /// <summary>
    /// Resolves the type-erased, wire-capable adapter for <paramref name="eventName"/> (a manifest event name,
    /// possibly fully qualified) so the host-side router can wire an installed kernel to the right typed
    /// pipeline terminal with no reflection. Uses the same precedence as <see cref="TryResolveShape"/>, so a
    /// package is validated against the SAME adapter it is wired to. Returns <c>false</c> when no registered
    /// adapter matches (or the match is ambiguous). Public as a composability seam — build custom by-name wiring
    /// on top of it when <see cref="PluginServer.WireHook"/>/<see cref="PluginServer.WireSubscription"/> don't
    /// fit; the adapter must be registered first (the router does not auto-register by name).
    /// </summary>
    public bool TryResolveErased(string eventName, out IErasedPluginEventAdapter adapter)
    {
        if (TryResolveRegistered(eventName, out var registered))
        {
            adapter = registered.Erased;
            return true;
        }

        adapter = null!;
        return false;
    }

    /// <summary>
    /// Single by-name resolution shared by wiring (<see cref="TryResolveErased"/>) and shape validation
    /// (<see cref="TryResolveShape"/>) so a package can never be validated against one event and wired to another.
    /// Precedence, refusing to guess on a collision:
    ///   1. An unambiguous exact (ordinal) match on the adapter's reported name; two adapters reporting the same
    ///      name is genuinely ambiguous, so reject rather than pick the first.
    ///   2. A fully-qualified match on the event TYPE's name (the dictionary key). Convention/hand-written
    ///      adapters report only the simple name, so two same-simple-name events in different namespaces are
    ///      indistinguishable by (1) and (3); the manifest records the FQN and the type's FullName is unique.
    ///   3. A qualified-vs-simple suffix bridge — only when it resolves to a single adapter.
    /// </summary>
    private bool TryResolveRegistered(string eventName, out RegisteredPluginEventAdapter resolved)
    {
        RegisteredPluginEventAdapter exactMatch = default;
        var exactCount = 0;
        RegisteredPluginEventAdapter typeNameMatch = default;
        var hasTypeNameMatch = false;
        RegisteredPluginEventAdapter suffixMatch = default;
        var suffixCount = 0;

        foreach (var entry in _adapters)
        {
            var registered = entry.Value;
            if (string.Equals(registered.Shape.EventName, eventName, StringComparison.Ordinal))
            {
                exactMatch = registered;
                exactCount++;
                continue;
            }

            if (!hasTypeNameMatch && string.Equals(entry.Key.FullName, eventName, StringComparison.Ordinal))
            {
                typeNameMatch = registered;
                hasTypeNameMatch = true;
            }

            if (EventNameMatch.Matches(registered.Shape.EventName, eventName))
            {
                if (suffixCount == 0)
                {
                    suffixMatch = registered;
                }

                suffixCount++;
            }
        }

        if (exactCount == 1)
        {
            resolved = exactMatch;
            return true;
        }

        if (exactCount == 0 && hasTypeNameMatch)
        {
            resolved = typeNameMatch;
            return true;
        }

        if (exactCount == 0 && !hasTypeNameMatch && suffixCount == 1)
        {
            resolved = suffixMatch;
            return true;
        }

        // Zero matches, or an ambiguous collision we refuse to resolve by registration order.
        resolved = default;
        return false;
    }

    private static IPluginEventAdapter<TEvent>? TryDiscoverAdapter<TEvent>()
    {
        var adapterType = typeof(IPluginEventAdapter<TEvent>);
        foreach (var type in typeof(TEvent).Assembly.GetTypes())
        {
            if (type.IsAbstract || !adapterType.IsAssignableFrom(type))
            {
                continue;
            }

            var instance = StaticInstance(type) ?? Activator.CreateInstance(type);
            return (IPluginEventAdapter<TEvent>)instance!;
        }

        return null;
    }

    private void ValidateEventNameShape(Type eventType, PluginEventShape shape)
    {
        foreach (var registered in _adapters)
        {
            if (registered.Key == eventType)
            {
                continue;
            }

            var current = registered.Value.Shape;
            if (!string.Equals(current.EventName, shape.EventName, StringComparison.Ordinal) ||
                PluginParameterShape.Matches(current.Parameters, shape.Parameters))
            {
                continue;
            }
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK034", $"Event adapter name '{shape.EventName}' is already registered with a different parameter shape.")
            ]);
        }
    }

    private static object? StaticInstance(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(p => string.Equals(p.Name, "Instance", StringComparison.Ordinal) &&
                                 type.IsAssignableFrom(p.PropertyType))
            ?.GetValue(null);
}

internal readonly record struct RegisteredPluginEventAdapter(
    object Adapter,
    PluginEventShape Shape,
    IErasedPluginEventAdapter Erased);
