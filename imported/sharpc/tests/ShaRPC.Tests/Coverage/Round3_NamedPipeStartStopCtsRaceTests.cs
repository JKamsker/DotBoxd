using ShaRPC.Transports.NamedPipes;
using Xunit;

namespace ShaRPC.Tests.Cov;

/// <summary>
/// DETERMINISTIC red→green regression for DEFECT #5: <see cref="NamedPipeServerTransport.StartAsync"/>
/// publishes <c>_started</c> (Interlocked) BEFORE assigning <c>_stopCts</c>, with no lock between them.
/// A <c>StopAsync</c> that runs in that window swaps <c>_started</c> back to 0 and captures
/// <c>_stopCts == null</c> (a no-op), then <c>StartAsync</c> assigns a fresh <c>_stopCts</c> — leaving
/// <c>_started == 0</c> with a live, undisposed <c>_stopCts</c> (a CancellationTokenSource / kernel
/// WaitHandle leak; a later <c>StartAsync</c> overwrites it without disposing).
///
/// The internal <c>_onStartTransitionForTest</c> seam fires in that exact window so the race is
/// deterministic. The fix publishes <c>_stopCts</c> before <c>_started</c> under <c>_sync</c> (and moves
/// the seam after the atomic publish), so the racing <c>StopAsync</c> observes a consistent
/// (<c>_started == 1</c>, <c>_stopCts != null</c>) state and disposes the source.
/// </summary>
public sealed class Round3_NamedPipeStartStopCtsRaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static string CreatePipeName() => "sharpc-round3-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task StartAsync_RacedByStopInStartedWindow_DoesNotLeakStopCts()
    {
        await using var server = new NamedPipeServerTransport(CreatePipeName());

        // Seam: inside StartAsync, race a full StopAsync into the _started/_stopCts window.
        server._onStartTransitionForTest = () => server.StopAsync();

        await server.StartAsync().WaitAsync(Timeout);

        // The leaked state is: the server reports not-started yet still holds a live stop source. The fix
        // makes the racing StopAsync observe and dispose the source, so this combination cannot occur.
        var leaked = server.StartedForTest == 0 && server.StopCtsForTest is not null;
        Assert.False(
            leaked,
            "StartAsync left a live _stopCts after a StopAsync raced its _started/_stopCts window");
    }
}
