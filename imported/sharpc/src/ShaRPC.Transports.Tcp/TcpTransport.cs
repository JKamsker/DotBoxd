using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.Tcp;

/// <summary>
/// TCP client transport implementation.
/// </summary>
public sealed class TcpTransport : ITransport
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private TcpConnection? _connection;
    private int _disposed;

    public TcpTransport(string host, int port)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
    }

    public IRpcChannel? Connection => _connection;

    public bool IsConnected => _connection?.IsConnected ?? false;

    /// <summary>
    /// Inter-read idle timeout applied to this connection's in-progress frame reads (slow-loris
    /// defense). <see langword="null"/> uses <see cref="TcpConnection.DefaultFrameReadIdleTimeout"/>;
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables it. See <see cref="TcpConnection"/>.
    /// </summary>
    public TimeSpan? FrameReadIdleTimeout { get; init; }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpTransport));
        }

        // Honour an already-cancelled token at entry, so cancellation does not depend on the WhenAny below
        // resolving on the cancelled-delay branch (Task.WhenAny returns its first argument when both tasks
        // are already complete). Matches every sibling entry point (TcpServerTransport.StartAsync/
        // AcceptAsync, NamedPipeClientTransport.ConnectAsync).
        ct.ThrowIfCancellationRequested();

        if (_connection != null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        // Connect against a fresh local client and publish it to the fields only once the connect
        // has succeeded. Any failure — connect error, timeout, or cancellation — disposes the client,
        // so a failed attempt never leaks a socket and a retry never overwrites an undisposed _client.
        var client = new TcpClient();
        try
        {
            // netstandard2.1 has no CancellationToken overload for ConnectAsync, so race it against an
            // infinite delay bound to the token. The delay is cancelled in the finally so its timer and
            // token registration are released on the success path instead of lingering for ct's lifetime.
            var connectTask = client.ConnectAsync(_host, _port);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                if (await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, connectCts.Token)).ConfigureAwait(false) != connectTask)
                {
                    // Cancelled before the connect completed. Observe the connect task's eventual fault
                    // (it faults once the client is disposed below) so it is not an unobserved exception.
                    ObserveFault(connectTask);
                    ct.ThrowIfCancellationRequested();
                }

                await connectTask.ConfigureAwait(false);
            }
            finally
            {
                connectCts.Cancel();
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        _connection = new TcpConnection(client, FrameReadIdleTimeout);

        // Full store-load fence so the _client/_connection publication above is globally visible before
        // _disposed is read. Without it an x86/x64 store-buffer (Dekker) interleaving could let this read
        // miss a concurrent DisposeAsync while that DisposeAsync misses _connection, leaking the socket.
        Interlocked.MemoryBarrier();

        // Close the window where DisposeAsync ran during the connect and observed null fields: tear
        // down the connection we just created so it cannot outlive a disposed transport.
        if (Volatile.Read(ref _disposed) != 0)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            client.Dispose();
            throw new ObjectDisposedException(nameof(TcpTransport));
        }
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _client?.Dispose();
    }
}
