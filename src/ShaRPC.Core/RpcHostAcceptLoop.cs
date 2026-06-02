using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

internal sealed class RpcHostAcceptLoop
{
    private static readonly TimeSpan AcceptErrorBackoff = TimeSpan.FromMilliseconds(50);

    private readonly IServerTransport _listener;
    private readonly Func<IConnection, Task> _addPeerAsync;
    private readonly Action<Exception> _acceptError;

    public RpcHostAcceptLoop(
        IServerTransport listener,
        Func<IConnection, Task> addPeerAsync,
        Action<Exception> acceptError)
    {
        _listener = listener;
        _addPeerAsync = addPeerAsync;
        _acceptError = acceptError;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IConnection connection;
            try
            {
                connection = await _listener.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _acceptError(ex);
                if (!await DelayAfterErrorAsync(ct).ConfigureAwait(false))
                {
                    break;
                }

                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _addPeerAsync(connection).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _acceptError(ex);
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            });
        }
    }

    private static async Task<bool> DelayAfterErrorAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(AcceptErrorBackoff, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
