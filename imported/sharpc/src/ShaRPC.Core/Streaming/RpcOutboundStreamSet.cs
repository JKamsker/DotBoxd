using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcOutboundStreamSet : IAsyncDisposable
{
    private readonly RpcStreamManager _manager;
    private readonly ISerializer _serializer;
    private readonly (RpcStreamAttachment Attachment, RpcStreamSendState State)[] _streams;
    private readonly CancellationTokenSource? _waitCancellation;
    private readonly CancellationToken _waitToken;
    private Task[]? _tasks;
    private int _disposed;
    private int _started;

    public static RpcOutboundStreamSet Empty { get; } = new();

    private RpcOutboundStreamSet()
    {
        _manager = null!;
        _serializer = null!;
        _streams = Array.Empty<(RpcStreamAttachment, RpcStreamSendState)>();
        _tasks = Array.Empty<Task>();
        _started = 1;
    }

    public RpcOutboundStreamSet(
        RpcStreamManager manager,
        ISerializer serializer,
        (RpcStreamAttachment Attachment, RpcStreamSendState State)[] streams)
    {
        _manager = manager;
        _serializer = serializer;
        _streams = streams;
        if (streams.Length == 1)
        {
            _waitToken = streams[0].State.Token;
        }
        else
        {
            _waitCancellation = CreateLinkedCancellationSource(streams);
            _waitToken = _waitCancellation.Token;
        }
    }

    public bool IsEmpty => _streams.Length == 0;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || IsEmpty)
        {
            return;
        }

        var tasks = new Task[_streams.Length];
        for (var i = 0; i < _streams.Length; i++)
        {
            var pair = _streams[i];
            tasks[i] = Task.Run(() => PumpAsync(pair.Attachment, pair.State));
        }

        Volatile.Write(ref _tasks, tasks);
    }

    public async Task WaitAsync()
    {
        var tasks = Volatile.Read(ref _tasks);
        if (tasks is not { Length: > 0 })
        {
            return;
        }

        try
        {
            await WaitForPumpTasksAsync(tasks).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            foreach (var pair in _streams)
            {
                pair.State.Cancel();
            }

            var tasks = Volatile.Read(ref _tasks);
            if (tasks is { Length: > 0 })
            {
                if (HasRunningTask(tasks))
                {
                    await DisposeSourcesBestEffortAsync().ConfigureAwait(false);
                }

                try
                {
                    await WaitForPumpTasksAsync(tasks).ConfigureAwait(false);
                }
                catch
                {
                }
            }
            else if (Interlocked.Exchange(ref _started, 1) == 0)
            {
                await DisposeSourcesBestEffortAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var pair in _streams)
            {
                _manager.RemoveOutbound(pair.State.StreamId);
            }

            _waitCancellation?.Dispose();
        }
    }

    private async Task PumpAsync(RpcStreamAttachment attachment, RpcStreamSendState state)
    {
        try
        {
            await attachment.PumpCoreAsync(_manager, _serializer, state.Token).ConfigureAwait(false);
            await SendStreamCompleteAsync(state).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (state.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Outbound stream pump failed", ex);
            try
            {
                await SendStreamErrorAsync(state, ex).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (state.IsCancellationRequested)
            {
            }
            catch (Exception sendError)
            {
                RpcDiagnostics.Report("Outbound stream error notification failed", sendError);
            }
        }
        finally
        {
            _manager.RemoveCompletedOutbound(state.StreamId);
        }
    }

    private async Task SendStreamCompleteAsync(RpcStreamSendState state)
    {
        var send = _manager.SendStreamCompleteAsync(state.StreamId, state.Token);
        await RpcTaskWaiter.WaitAsync(send, state.Token).ConfigureAwait(false);
    }

    private async Task SendStreamErrorAsync(RpcStreamSendState state, Exception error)
    {
        var send = _manager.SendStreamErrorAsync(state.StreamId, error, state.Token);
        await RpcTaskWaiter.WaitAsync(send, state.Token).ConfigureAwait(false);
    }

    private async Task WaitForPumpTasksAsync(Task[] tasks)
    {
        var wait = tasks.Length == 1 ? tasks[0] : Task.WhenAll(tasks);
        await RpcTaskWaiter.WaitAsync(wait, _waitToken).ConfigureAwait(false);
    }

    private static CancellationTokenSource CreateLinkedCancellationSource(
        (RpcStreamAttachment Attachment, RpcStreamSendState State)[] streams)
    {
        var tokens = new CancellationToken[streams.Length];
        for (var i = 0; i < streams.Length; i++)
        {
            tokens[i] = streams[i].State.Token;
        }

        return CancellationTokenSource.CreateLinkedTokenSource(tokens);
    }

    private async ValueTask DisposeSourcesBestEffortAsync()
    {
        foreach (var pair in _streams)
        {
            await pair.Attachment.DisposeSourceBestEffortAsync("Outbound stream source cleanup failed")
                .ConfigureAwait(false);
        }
    }

    private static bool HasRunningTask(Task[] tasks)
    {
        foreach (var task in tasks)
        {
            if (!task.IsCompleted)
            {
                return true;
            }
        }

        return false;
    }
}
