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
        foreach (var adapter in _adapters.Values)
        {
            var current = adapter.Shape;
            // The manifest event name may be fully qualified (Namespace.TypeName) while an adapter reports
            // only the simple name; EventNameMatch bridges that seam and still honours exact matches.
            if (EventNameMatch.Matches(current.EventName, eventName))
            {
                shape = current;
                return true;
            }
        }

        shape = default!;
        return false;
    }

    /// <summary>
    /// Resolves the type-erased, wire-capable adapter for <paramref name="eventName"/> (a manifest event name,
    /// possibly fully qualified) so the host-side router can wire an installed kernel to the right typed
    /// pipeline terminal with no reflection. Mirrors <see cref="TryResolveShape"/>'s by-name matching. Returns
    /// <c>false</c> when no registered adapter matches. Public as a composability seam — build custom by-name
    /// wiring on top of it when <see cref="PluginServer.WireHook"/>/<see cref="PluginServer.WireSubscription"/>
    /// don't fit; the adapter must be registered first (the router does not auto-register by name).
    /// </summary>
    public bool TryResolveErased(string eventName, out IErasedPluginEventAdapter adapter)
    {
        // Prefer an exact (ordinal) name match over a qualified-vs-simple suffix match, so a precisely-named
        // event always resolves to its own adapter and never to one that merely suffix-collides with it.
        IErasedPluginEventAdapter? suffixMatch = null;
        foreach (var registered in _adapters.Values)
        {
            if (string.Equals(registered.Shape.EventName, eventName, StringComparison.Ordinal))
            {
                adapter = registered.Erased;
                return true;
            }

            if (suffixMatch is null && EventNameMatch.Matches(registered.Shape.EventName, eventName))
            {
                suffixMatch = registered.Erased;
            }
        }

        adapter = suffixMatch!;
        return suffixMatch is not null;
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
