using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.AdapterFqn.First
{
    public sealed record DamageEvent(int Amount);
}

namespace DotBoxD.Kernels.Tests.Plugins.AdapterFqn.Second
{
    public sealed record DamageEvent(int Amount);
}

namespace DotBoxD.Kernels.Tests.Plugins.AdapterFqn
{
    public sealed class PluginEventAdapterFqnResolutionTests
    {
        [Fact]
        public void TryResolveErased_disambiguates_same_simple_name_events_by_fully_qualified_name()
        {
            // Two distinct event types share the simple name "DamageEvent" in different namespaces. Convention
            // (and hand-written) adapters report only the SIMPLE name, so neither an exact-name match nor the
            // qualified-vs-simple suffix bridge can tell them apart — both would match. The manifest, however,
            // records the fully-qualified name, and the event type's full name is unique, so resolution must use
            // it to wire each event to its own adapter instead of resolving whichever was registered first.
            var registry = new PluginEventAdapterRegistry();
            registry.Register(new SimpleNameDamageAdapter<First.DamageEvent>());
            registry.Register(new SimpleNameDamageAdapter<Second.DamageEvent>());

            Assert.True(registry.TryResolveErased(typeof(First.DamageEvent).FullName!, out var first));
            Assert.Equal(typeof(First.DamageEvent), first.EventType);

            Assert.True(registry.TryResolveErased(typeof(Second.DamageEvent).FullName!, out var second));
            Assert.Equal(typeof(Second.DamageEvent), second.EventType);

            Assert.NotSame(first, second);

            // The bare simple name matches both adapters and is therefore genuinely ambiguous; resolution must
            // refuse rather than silently pick whichever was registered first.
            Assert.False(registry.TryResolveErased("DamageEvent", out _));
        }

        // Reports only the SIMPLE event name (the convention default), reproducing the collision scenario.
        private sealed class SimpleNameDamageAdapter<TEvent> : IPluginEventAdapter<TEvent>
        {
            public string EventName => "DamageEvent";

            public IReadOnlyList<Parameter> Parameters => [new("e_Amount", SandboxType.I32)];

            public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e) => [SandboxValue.FromInt32(0)];
        }
    }
}
