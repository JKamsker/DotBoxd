# ShaRPC WebSocket Transport Setup Guide

This guide covers implementing and using WebSocket transport with ShaRPC. WebSocket transport is ideal for browser clients, Unity WebGL builds, and scenarios requiring HTTP-based connectivity.

## Overview

ShaRPC is transport-agnostic. This guide shows how to create a WebSocket transport by implementing the core transport interfaces:

- `ITransport` - Client-side transport
- `IServerTransport` - Server-side transport
- `IRpcChannel` - Bidirectional connection abstraction

## 1. Create the WebSocket Transport Project

Create a new class library project:

```bash
dotnet new classlib -n ShaRPC.Transports.WebSocket -f netstandard2.1
```

Add the required references to `ShaRPC.Transports.WebSocket.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../ShaRPC.Core/ShaRPC.Core.csproj" />
</ItemGroup>
```

## 2. Implement WebSocketConnection

The connection class wraps a `WebSocket` and implements `IRpcChannel`:

```csharp
// WebSocketConnection.cs
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.WebSocket;

/// <summary>
/// WebSocket-based connection implementation.
/// </summary>
public sealed class WebSocketConnection : IRpcChannel
{
    private readonly System.Net.WebSockets.WebSocket _socket;
    private readonly string _remoteEndpoint;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    public WebSocketConnection(System.Net.WebSockets.WebSocket socket, string remoteEndpoint)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _remoteEndpoint = remoteEndpoint ?? "unknown";
    }

    public bool IsConnected => _socket.State == WebSocketState.Open && !_disposed;

    public string RemoteEndpoint => _remoteEndpoint;

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebSocketConnection));
        }

        await _sendLock.WaitAsync(ct);
        try
        {
            await _socket.SendAsync(
                data,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<Memory<byte>> ReceiveAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebSocketConnection));
        }

        // Read the complete WebSocket message
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024); // 16KB initial buffer
        var totalReceived = 0;

        try
        {
            WebSocketReceiveResult result;
            do
            {
                // Expand buffer if needed
                if (totalReceived >= buffer.Length - 4096)
                {
                    var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, totalReceived);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = newBuffer;
                }

                result = await _socket.ReceiveAsync(
                    buffer.AsMemory(totalReceived),
                    ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return Memory<byte>.Empty;
                }

                totalReceived += result.Count;
            }
            while (!result.EndOfMessage);

            if (totalReceived == 0)
            {
                return Memory<byte>.Empty;
            }

            // Validate message length
            if (totalReceived < 4)
            {
                throw new InvalidOperationException("Message too short");
            }

            var messageLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0, 4));
            if (messageLength != totalReceived || messageLength > 16 * 1024 * 1024)
            {
                throw new InvalidOperationException($"Invalid message length: {messageLength}");
            }

            // Copy to exact-sized array
            var message = new byte[totalReceived];
            Buffer.BlockCopy(buffer, 0, message, 0, totalReceived);
            return message;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    CancellationToken.None);
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _socket.Dispose();
        _sendLock.Dispose();
    }
}
```

## 3. Implement Client Transport

The client transport manages `ClientWebSocket` connections:

```csharp
// WebSocketTransport.cs
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.WebSocket;

/// <summary>
/// WebSocket client transport implementation.
/// </summary>
public sealed class WebSocketTransport : ITransport
{
    private readonly Uri _uri;
    private readonly Action<ClientWebSocketOptions>? _configureOptions;
    private ClientWebSocket? _socket;
    private WebSocketConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Creates a new WebSocket transport.
    /// </summary>
    /// <param name="uri">WebSocket server URI (e.g., ws://localhost:5050/rpc)</param>
    /// <param name="configureOptions">Optional callback to configure WebSocket options</param>
    public WebSocketTransport(string uri, Action<ClientWebSocketOptions>? configureOptions = null)
        : this(new Uri(uri), configureOptions)
    {
    }

    /// <summary>
    /// Creates a new WebSocket transport.
    /// </summary>
    /// <param name="uri">WebSocket server URI</param>
    /// <param name="configureOptions">Optional callback to configure WebSocket options</param>
    public WebSocketTransport(Uri uri, Action<ClientWebSocketOptions>? configureOptions = null)
    {
        _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        _configureOptions = configureOptions;
    }

    public IRpcChannel? Connection => _connection;

    public bool IsConnected => _connection?.IsConnected ?? false;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebSocketTransport));
        }

        if (_connection != null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        _socket = new ClientWebSocket();
        _configureOptions?.Invoke(_socket.Options);

        await _socket.ConnectAsync(_uri, ct);
        _connection = new WebSocketConnection(_socket, _uri.Host);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }
}
```

## 4. Implement Server Transport

The server transport uses `HttpListener` for WebSocket upgrades:

```csharp
// WebSocketServerTransport.cs
using System;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.WebSocket;

/// <summary>
/// WebSocket server transport implementation using HttpListener.
/// </summary>
public sealed class WebSocketServerTransport : IServerTransport
{
    private readonly HttpListener _listener;
    private readonly string _path;
    private readonly Channel<IRpcChannel> _connectionChannel;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;

    /// <summary>
    /// Creates a new WebSocket server transport.
    /// </summary>
    /// <param name="port">Port to listen on</param>
    /// <param name="path">WebSocket path (e.g., "/rpc")</param>
    public WebSocketServerTransport(int port, string path = "/rpc")
        : this($"http://+:{port}{path}/")
    {
        _path = path;
    }

    /// <summary>
    /// Creates a new WebSocket server transport.
    /// </summary>
    /// <param name="prefix">HTTP listener prefix (e.g., "http://localhost:5050/rpc/")</param>
    public WebSocketServerTransport(string prefix)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _path = new Uri(prefix.Replace("+", "localhost").Replace("*", "localhost")).AbsolutePath;
        _connectionChannel = Channel.CreateUnbounded<IRpcChannel>();
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebSocketServerTransport));
        }

        _listener.Start();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptTask = AcceptConnectionsAsync(_cts.Token);

        await Task.CompletedTask;
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                _ = HandleWebSocketAsync(context, ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            var wsContext = await context.AcceptWebSocketAsync(null);
            var remoteEndpoint = context.Request.RemoteEndPoint?.ToString() ?? "unknown";
            var connection = new WebSocketConnection(wsContext.WebSocket, remoteEndpoint);

            await _connectionChannel.Writer.WriteAsync(connection, ct);
        }
        catch
        {
            context.Response.StatusCode = 500;
            context.Response.Close();
        }
    }

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebSocketServerTransport));
        }

        return await _connectionChannel.Reader.ReadAsync(ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        _listener.Stop();
        _connectionChannel.Writer.Complete();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        _listener.Close();
        _cts?.Dispose();
    }
}
```

## 5. Using WebSocket Transport

### Server Setup

```csharp
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.WebSocket;

// A host turns every accepted WebSocket connection into a full peer.
await using var host = RpcHost
    .Listen(new WebSocketServerTransport(5050, "/rpc"), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvideMyService(new MyService()));

await host.StartAsync();
Console.WriteLine("WebSocket server running on ws://localhost:5050/rpc");

// Optional lifecycle hooks:
// host.PeerConnected    += (_, args) => { /* args.Peer is the new RpcPeer */ };
// host.PeerDisconnected += (_, args) => { /* peer closed */ };
// host.AcceptError      += (_, args) => { /* transport-level accept failure */ };

// Shutdown: await host.StopAsync();  (DisposeAsync also stops the host)
```

### Client Setup

```csharp
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.WebSocket;

var transport = new WebSocketTransport("ws://localhost:5050/rpc");
await transport.ConnectAsync();

// RejectInboundCalls signals a get-only intent: this peer calls out but refuses callbacks.
await using var peer = RpcPeer
    .Over(transport.Connection!, new MessagePackRpcSerializer(),
          new RpcPeerOptions { RejectInboundCalls = true })
    .Start();

var service = peer.GetMyService();
var response = await service.GreetAsync(new GreetingRequest { Name = "World" });
Console.WriteLine(response.Message);
```

### Secure WebSocket (WSS)

For production, use secure WebSocket connections:

```csharp
// Client with WSS
var transport = new WebSocketTransport("wss://myserver.com/rpc");

// Server requires HTTPS certificate configuration
// Configure via netsh or use Kestrel for easier HTTPS setup
```

## 6. Using with ASP.NET Core (Recommended for Production)

For production servers, use ASP.NET Core's built-in WebSocket support:

```csharp
// Program.cs
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

// We accept WebSocket upgrades ourselves, then wrap each connection in an RpcPeer.
var serializer = new MessagePackRpcSerializer();
var gameService = new GameService();

app.Map("/rpc", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();

    // WebSocketConnection implements IRpcChannel, so it plugs
    // straight into RpcPeer.Over — no builder or manual dispatch loop required.
    await using var connection = new WebSocketConnection(
        ws, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    await using var peer = RpcPeer
        .Over(connection, serializer)
        .ProvideGameService(gameService)
        .Start();

    // Keep the request alive while the peer pumps the connection. The Disconnected
    // event fires when the read loop ends after a remote close or read error.
    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    peer.Disconnected += (_, _) => closed.TrySetResult();
    await closed.Task.WaitAsync(context.RequestAborted);
});

app.Run();
```

## 7. Unity WebGL Integration

WebSocket transport is essential for Unity WebGL builds since raw TCP sockets are not available:

```csharp
// Unity NetworkManager using WebSocket
public class NetworkManager : MonoBehaviour
{
    [SerializeField] private string serverUrl = "wss://game.example.com/rpc";

    private WebSocketTransport _transport;
    private RpcPeer _peer;
    private IGameService _gameService;

    public async Task ConnectAsync()
    {
        _transport = new WebSocketTransport(serverUrl);
        var serializer = MessagePackRpcSerializer.CreateUnityCompatible();

        await _transport.ConnectAsync();

        _peer = RpcPeer
            .Over(_transport.Connection!, serializer,
                  new RpcPeerOptions
                  {
                      RequestTimeout = TimeSpan.FromSeconds(30),
                      RejectInboundCalls = true, // get-only client
                  })
            .Start();

        _gameService = _peer.GetGameService();
    }

    public async Task DisconnectAsync()
    {
        if (_peer != null) await _peer.DisposeAsync();
        if (_transport != null) await _transport.DisposeAsync();
    }
}
```

## 8. Configuration Options

### Client Options

Configure WebSocket options during connection:

```csharp
var transport = new WebSocketTransport("ws://localhost:5050/rpc", options =>
{
    // Set custom headers
    options.SetRequestHeader("Authorization", "Bearer token");

    // Configure keep-alive
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);

    // Set buffer sizes
    options.SetBuffer(8192, 8192);
});
```

### Server Options

```csharp
// Using HttpListener
var transport = new WebSocketServerTransport(5050, "/rpc");

// Or with full prefix control
var transport = new WebSocketServerTransport("http://192.168.1.100:5050/rpc/");
```

## Key Differences from TCP Transport

| Aspect | TCP | WebSocket |
|--------|-----|-----------|
| Browser support | No | Yes |
| Unity WebGL | No | Yes |
| Firewall friendly | Sometimes blocked | Usually allowed (HTTP ports) |
| Proxy support | Limited | Full (HTTP proxies) |
| SSL/TLS | Manual setup | Built-in (WSS) |
| Protocol overhead | Minimal | HTTP upgrade + framing |

## Troubleshooting

### Connection Refused
- Verify the server URL includes the correct path (e.g., `/rpc`)
- Check firewall rules for HTTP/HTTPS ports
- On Windows, `HttpListener` may require admin rights or URL reservations

### HttpListener Access Denied (Windows)
Run as administrator or reserve the URL:
```cmd
netsh http add urlacl url=http://+:5050/rpc/ user=Everyone
```

### WebSocket Handshake Failed
- Ensure the server is configured for WebSocket connections
- Check that the path matches between client and server
- Verify no proxy is interfering with WebSocket upgrades

## Next Steps

- [Quick Start Guide](./quick-start.md) - Basic ShaRPC setup
- [Unity Integration](./unity-integration.md) - Full Unity setup including WebGL
- [API Reference](./api-reference.md) - Detailed API documentation
