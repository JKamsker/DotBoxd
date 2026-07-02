using System.Collections.Concurrent;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Serialization;

namespace DotBoxD.Services.Streaming.Core;

internal sealed partial class RpcStreamManager
{
    public const int WindowSize = 4;
    private readonly ConcurrentDictionary<int, RpcStreamReceiver> _receivers = new();
    private readonly ConcurrentDictionary<int, byte> _canceledOutbound = new();
    private readonly RpcCanceledInboundStreams _canceledInbound = new();
    private readonly object _inboundGate = new();
    private readonly ConcurrentDictionary<int, int> _pendingCredits = new();
    private readonly ConcurrentDictionary<int, byte> _reservedOutbound = new();
    private readonly ConcurrentDictionary<int, RpcStreamSendState> _senders = new();
    private readonly RpcStreamFrameSender _frameSender;
    private readonly ISerializer _serializer;
    private readonly Func<Exception, RpcErrorInfo?>? _exceptionTransformer;
    private int _outboundStreamIdCounter;
    private int _activeInboundCount;
    public RpcStreamManager(
        ISerializer serializer,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<Exception, RpcErrorInfo?>? exceptionTransformer,
        Func<PooledBufferWriter, CancellationToken, ValueTask>? sendFrameAsync = null)
    {
        _serializer = serializer;
        _frameSender = new RpcStreamFrameSender(sendAsync, sendFrameAsync);
        _exceptionTransformer = exceptionTransformer;
    }
    internal int InboundReceiverCount => Volatile.Read(ref _activeInboundCount);
    internal int OutboundSenderCount => _senders.Count;
    internal int PendingCreditCount => _pendingCredits.Count;
    internal int CanceledInboundCount => _canceledInbound.Count;
    internal int CanceledInboundTrackingCount => _canceledInbound.TrackingCount;
    internal int OutboundStreamIdCounterForTest { set => Volatile.Write(ref _outboundStreamIdCounter, value); }
    internal void DecrementActiveInbound() => Interlocked.Decrement(ref _activeInboundCount);
    internal Action<int, RpcStreamReceiver>? AfterInboundReceiverObservedForTest { get; set; }
    internal Action<int>? AfterReservedOutboundCreditObservedForTest { get; set; }
    internal Action<int>? AfterOutboundSenderMissForTest { get; set; }
    public void Stop()
    {
        var receivers = new List<RpcStreamReceiver>();
        lock (_inboundGate)
        {
            _canceledInbound.Clear();
            foreach (var pair in _receivers)
            {
                try
                {
                    _canceledInbound.Add(pair.Key);
                }
                catch (Exception ex)
                {
                    RpcDiagnostics.Report("Stopped inbound stream tracking failed", ex);
                }
                if (_receivers.TryRemove(pair.Key, out var receiver))
                {
                    receivers.Add(receiver);
                }
            }
        }
        foreach (var receiver in receivers)
        {
            receiver.Abort(new ServiceConnectionException("Connection closed."));
        }
        foreach (var pair in _senders)
        {
            RemoveOutbound(pair.Key);
        }
        _pendingCredits.Clear();
        _reservedOutbound.Clear();
        _canceledOutbound.Clear();
    }
}
