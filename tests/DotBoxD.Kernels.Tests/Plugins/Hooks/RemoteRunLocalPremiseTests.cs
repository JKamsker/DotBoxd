using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

/// <summary>
/// Premise guards for the remote <c>RunLocal</c> model (2-process: server + plugin, separated by IPC). The
/// filter (<c>Where</c>) and projection (<c>Select</c>) must ALWAYS lower to server-side verified IR and run
/// on the server; the client builder must never EVALUATE those lambdas. These tests hard-fail if the remote
/// builder ever starts running the predicate/projection on the client — which would mean filtering or
/// projection leaked off the server and across (or before) the IPC boundary, breaking the premise.
/// </summary>
public sealed class RemoteRunLocalPremiseTests
{
    private sealed record Aggro(string MonsterId, int Distance);

    // Install must not fire while only building the chain — reaching it here would mean a terminal ran.
    private static RemoteHookRegistry Hooks()
        => new(_ => throw new InvalidOperationException("install must not run while only building the chain"));

    private static RemoteSubscriptionRegistry Subscriptions()
        => new(_ => throw new InvalidOperationException("install must not run while only building the chain"));

    [Fact]
    public void Remote_hook_Where_does_not_evaluate_the_predicate_on_the_client()
    {
        var evaluatedClientSide = false;
        Hooks().On<Aggro>().Where(e =>
        {
            evaluatedClientSide = true;
            return e.Distance <= 4;
        });

        Assert.False(evaluatedClientSide, "remote Where must lower to server-side IR, never run on the client");
    }

    [Fact]
    public void Remote_hook_Select_does_not_evaluate_the_projection_on_the_client()
    {
        var projectedClientSide = false;
        Hooks().On<Aggro>().Select(e =>
        {
            projectedClientSide = true;
            return e.MonsterId;
        });

        Assert.False(projectedClientSide, "remote Select must lower to server-side IR, never run on the client");
    }

    [Fact]
    public void Remote_hook_staged_Where_after_Select_does_not_evaluate_on_the_client()
    {
        var evaluatedClientSide = false;
        Hooks().On<Aggro>()
            .Select(e => e.MonsterId)
            .Where(id =>
            {
                evaluatedClientSide = true;
                return id.Length > 0;
            });

        Assert.False(evaluatedClientSide, "remote staged Where must lower to server-side IR, never run on the client");
    }

    [Fact]
    public void Remote_subscription_Where_and_Select_do_not_evaluate_on_the_client()
    {
        var ranClientSide = false;
        Subscriptions().On<Aggro>()
            .Where(e =>
            {
                ranClientSide = true;
                return e.Distance <= 4;
            })
            .Select(e =>
            {
                ranClientSide = true;
                return e.MonsterId;
            });

        Assert.False(ranClientSide, "remote subscription Where/Select must lower to server-side IR, never run on the client");
    }

    [Fact]
    public void Remote_hook_whole_event_Where_does_not_evaluate_on_the_client()
    {
        // A no-Select (whole-event) RunLocal still lowers its Where to server-side IR — the client builder
        // must never run the predicate.
        var evaluatedClientSide = false;
        Hooks().On<Aggro>().Where(e =>
        {
            evaluatedClientSide = true;
            return e.Distance <= 4;
        });

        Assert.False(evaluatedClientSide, "remote whole-event Where must lower to server-side IR, never run on the client");
    }

    [Fact]
    public void Remote_subscription_whole_event_Where_does_not_evaluate_on_the_client()
    {
        var evaluatedClientSide = false;
        Subscriptions().On<Aggro>().Where(e =>
        {
            evaluatedClientSide = true;
            return e.Distance <= 4;
        });

        Assert.False(evaluatedClientSide, "remote whole-event subscription Where must lower to server-side IR, never run on the client");
    }
}
