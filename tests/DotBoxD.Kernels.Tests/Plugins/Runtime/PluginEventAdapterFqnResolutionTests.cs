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

        [Fact]
        public void TryResolveShape_validates_ambiguous_same_name_against_the_common_shape()
        {
            // DBXK034 lets two adapters share a simple EventName when their parameter shapes are identical. A
            // legacy simple-name manifest must still resolve THAT shape so install-time parameter validation
            // (DBXK033) runs — otherwise a malformed kernel would install and only fail later. Wiring still rejects
            // the ambiguity (it must not guess which event to route to), but validation does not.
            var registry = new PluginEventAdapterRegistry();
            registry.Register(new SimpleNameDamageAdapter<First.DamageEvent>());
            registry.Register(new SimpleNameDamageAdapter<Second.DamageEvent>());

            Assert.True(registry.TryResolveShape("DamageEvent", out var shape));
            Assert.Equal("DamageEvent", shape.EventName);
            Assert.False(registry.TryResolveErased("DamageEvent", out _));
        }

        [Fact]
        public void TryResolveShape_rejects_ambiguous_suffix_with_differing_shapes()
        {
            // Two adapters whose exact names only share a simple-name tail ("DamageEvent") are NOT constrained by
            // DBXK034 (which compares exact names), so their shapes may differ. A simple-name manifest must NOT be
            // validated against an arbitrary one chosen by registration order — resolution returns false (and
            // wiring rejects it too), rather than relaxing the way an exact same-name collision does.
            var registry = new PluginEventAdapterRegistry();
            registry.Register(new QualifiedNameAdapter<First.DamageEvent>(
                "Game.A.DamageEvent", [new("e_a", SandboxType.I32)]));
            registry.Register(new QualifiedNameAdapter<Second.DamageEvent>(
                "Game.B.DamageEvent", [new("e_a", SandboxType.I32), new("e_b", SandboxType.I32)]));

            Assert.False(registry.TryResolveShape("DamageEvent", out _));
            Assert.False(registry.TryResolveErased("DamageEvent", out _));
        }

        // Reports only the SIMPLE event name (the convention default), reproducing the collision scenario.
        private sealed class SimpleNameDamageAdapter<TEvent> : IPluginEventAdapter<TEvent>
        {
            public string EventName => "DamageEvent";

            public IReadOnlyList<Parameter> Parameters => [new("e_Amount", SandboxType.I32)];

            public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e) => [SandboxValue.FromInt32(0)];
        }

        // Reports a fully-qualified event name with a caller-supplied shape, to build a suffix-tail collision whose
        // shapes differ (which DBXK034 does not forbid, since the exact names differ).
        private sealed class QualifiedNameAdapter<TEvent>(string eventName, IReadOnlyList<Parameter> parameters)
            : IPluginEventAdapter<TEvent>
        {
            public string EventName { get; } = eventName;

            public IReadOnlyList<Parameter> Parameters { get; } = parameters;

            public IReadOnlyList<SandboxValue> ToSandboxValues(TEvent e) => [];
        }
    }
}
