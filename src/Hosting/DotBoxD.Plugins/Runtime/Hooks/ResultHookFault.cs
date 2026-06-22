namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// A fault caught while dispatching a result-returning hook (<c>.Register(...)</c> / <c>.RegisterLocal(...)</c>)
/// for one hook context type. <c>HookRegistry.FireAsync</c> isolates a faulting
/// handler — it abstains and falls through to the next registration so one bad handler cannot break the hook
/// point — but that isolation is otherwise silent, which would let a veto-bearing handler (a successful result
/// carrying e.g. <c>CanDie = false</c>) that throws fail open to the host default with no trace. The host
/// observes these faults via <c>PluginServer.Create(onResultHookFault: ...)</c> to surface the failure in its
/// log, mirroring <see cref="SubscriptionDeliveryFault"/> for fire-and-forget subscriptions. Control flow is
/// unchanged: a faulted handler is still skipped and dispatch still falls through to the next registration.
/// </summary>
/// <param name="EventType">The hook context/event type whose dispatch faulted.</param>
/// <param name="Exception">The exception that was caught and isolated.</param>
public readonly record struct ResultHookFault(Type EventType, Exception Exception);
