namespace DotBoxD.Plugins.Indexing;

/// <summary>
/// Marks an event property the host keeps a dispatch index for. DotBoxD owns predicate lowering and
/// exposes index metadata on the plugin manifest (<see cref="HookSubscriptionManifest.IndexedPredicates"/>);
/// this attribute is how a host declares which of those property paths it can actually serve from an
/// equality/range bucket. <see cref="EventIndexMatcher{TEvent}"/> reads it (once, with a compiled getter)
/// and ignores manifest predicates whose path is not an index key, leaving them to the verified IR.
/// <para>
/// Promoted to the framework as the first-class declaration surface (issue #50) so any host can opt into
/// index-based prefiltering without reimplementing the matcher. It stays purely declarative: a property
/// the host does not mark is simply never served from the index.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class EventIndexKeyAttribute : Attribute;
