# ShaRPC Unity Integration Guide

A comprehensive guide to integrating ShaRPC into your Unity project for type-safe, high-performance client-server communication.

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Project Setup](#project-setup)
4. [Defining Service Contracts](#defining-service-contracts)
5. [Server Implementation](#server-implementation)
6. [Unity Client Setup](#unity-client-setup)
7. [Connection Management](#connection-management)
8. [Error Handling](#error-handling)
9. [IL2CPP and AOT Considerations](#il2cpp-and-aot-considerations)
10. [Best Practices](#best-practices)
11. [Troubleshooting](#troubleshooting)
12. [Advanced Topics](#advanced-topics)

---

## Overview

ShaRPC is a transport-agnostic RPC framework designed with Unity compatibility as a primary goal. It uses compile-time source generation to create type-safe proxies and dispatchers, avoiding runtime reflection that causes issues with IL2CPP builds.

### Key Features for Unity

- **IL2CPP Safe**: No runtime reflection or dynamic code generation
- **Source Generators**: Compile-time proxy generation
- **Shared Contracts**: Same C# interfaces on client and server
- **MessagePack**: Fast binary serialization with Unity support
- **Transport Agnostic**: TCP, WebSocket, or custom transports

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Shared Library                          │
│  [ShaRpcService] IGameService + Models (PlayerId, PlayerState)  │
└─────────────────────────────────────────────────────────────────┘
                    │                           │
                    ▼                           ▼
┌─────────────────────────────┐   ┌─────────────────────────────┐
│      Unity Client           │   │      .NET Server            │
│  ┌───────────────────────┐  │   │  ┌───────────────────────┐  │
│  │ GameServiceProxy (gen)│  │   │  │GameServiceDispatcher  │  │
│  └───────────────────────┘  │   │  │       (generated)     │  │
│  ┌───────────────────────┐  │   │  └───────────────────────┘  │
│  │    RpcPeer            │  │   │  ┌───────────────────────┐  │
│  └───────────────────────┘  │   │  │     RpcHost           │  │
│  ┌───────────────────────┐  │   │  └───────────────────────┘  │
│  │    TcpTransport       │◄─┼───┼─►│  TcpServerTransport   │  │
│  └───────────────────────┘  │   │  └───────────────────────┘  │
└─────────────────────────────┘   └─────────────────────────────┘
```

---

## Prerequisites

### Unity Requirements
- Unity 2021.3 LTS or newer (for .NET Standard 2.1 support)
- Scripting Backend: Mono or IL2CPP
- API Compatibility Level: .NET Standard 2.1

### Server Requirements
- .NET 6.0 or newer (recommended: .NET 8.0+)

### NuGet Packages
- `MessagePack` (2.5.x or newer)

---

## Project Setup

### Step 1: Solution Structure

Create a solution with shared contracts accessible to both Unity and server:

```
YourGame/
├── src/
│   ├── YourGame.Shared/           # Shared contracts (netstandard2.1)
│   │   ├── IGameService.cs
│   │   └── Models.cs
│   └── YourGame.Server/           # Server (net8.0)
│       ├── GameService.cs
│       └── Program.cs
├── unity/
│   └── YourGameClient/            # Unity project
│       └── Assets/
│           ├── Plugins/
│           │   ├── ShaRPC.Core.dll
│           │   ├── ShaRPC.Transports.Tcp.dll
│           │   ├── ShaRPC.Serializers.MessagePack.dll
│           │   ├── YourGame.Shared.dll
│           │   └── MessagePack.dll
│           └── Scripts/
│               └── Networking/
└── YourGame.sln
```

### Step 2: Shared Library Project

Create `YourGame.Shared.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MessagePack" Version="2.5.187" />
  </ItemGroup>

  <!-- Reference source generator for proxy/dispatcher generation -->
  <ItemGroup>
    <ProjectReference Include="..\ShaRPC.Core\ShaRPC.Core.csproj" />
    <ProjectReference Include="..\ShaRPC.SourceGenerator\ShaRPC.SourceGenerator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

### Step 3: Build DLLs for Unity

Create a build script to copy DLLs to Unity:

```bash
#!/bin/bash
# build-for-unity.sh

UNITY_PLUGINS="unity/YourGameClient/Assets/Plugins"

dotnet build src/YourGame.Shared/YourGame.Shared.csproj -c Release

# Copy ShaRPC libraries
cp src/ShaRPC.Core/bin/Release/netstandard2.1/ShaRPC.Core.dll "$UNITY_PLUGINS/"
cp src/ShaRPC.Transports.Tcp/bin/Release/netstandard2.1/ShaRPC.Transports.Tcp.dll "$UNITY_PLUGINS/"
cp src/ShaRPC.Serializers.MessagePack/bin/Release/netstandard2.1/ShaRPC.Serializers.MessagePack.dll "$UNITY_PLUGINS/"

# Copy shared library
cp src/YourGame.Shared/bin/Release/netstandard2.1/YourGame.Shared.dll "$UNITY_PLUGINS/"

# Copy MessagePack (get from NuGet cache)
cp ~/.nuget/packages/messagepack/2.5.187/lib/netstandard2.0/MessagePack.dll "$UNITY_PLUGINS/"

echo "DLLs copied to Unity"
```

---

## Defining Service Contracts

### Basic Service Interface

```csharp
// YourGame.Shared/IGameService.cs
using ShaRPC.Core.Attributes;

namespace YourGame.Shared;

[ShaRpcService]
public interface IGameService
{
    // Simple request/response
    Task<ServerInfo> GetServerInfoAsync(CancellationToken ct = default);

    // With parameters
    Task<PlayerState> GetPlayerAsync(string playerId, CancellationToken ct = default);

    // With complex request object
    Task<JoinResult> JoinGameAsync(JoinRequest request, CancellationToken ct = default);

    // Fire-and-forget style (still awaitable)
    Task SendInputAsync(PlayerInput input, CancellationToken ct = default);
}
```

### Model Classes

Models must be serializable by MessagePack. Use attributes for best compatibility:

```csharp
// YourGame.Shared/Models.cs
using MessagePack;

namespace YourGame.Shared;

[MessagePackObject]
public class ServerInfo
{
    [Key(0)] public string ServerName { get; set; } = "";
    [Key(1)] public int PlayerCount { get; set; }
    [Key(2)] public int MaxPlayers { get; set; }
    [Key(3)] public string Version { get; set; } = "";
}

[MessagePackObject]
public class PlayerState
{
    [Key(0)] public string PlayerId { get; set; } = "";
    [Key(1)] public string DisplayName { get; set; } = "";
    [Key(2)] public float PositionX { get; set; }
    [Key(3)] public float PositionY { get; set; }
    [Key(4)] public float PositionZ { get; set; }
    [Key(5)] public float RotationY { get; set; }
    [Key(6)] public int Health { get; set; }
    [Key(7)] public int MaxHealth { get; set; }
}

[MessagePackObject]
public class JoinRequest
{
    [Key(0)] public string DisplayName { get; set; } = "";
    [Key(1)] public string AuthToken { get; set; } = "";
}

[MessagePackObject]
public class JoinResult
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? PlayerId { get; set; }
    [Key(2)] public string? ErrorMessage { get; set; }
    [Key(3)] public PlayerState? InitialState { get; set; }
}

[MessagePackObject]
public class PlayerInput
{
    [Key(0)] public float MoveX { get; set; }
    [Key(1)] public float MoveY { get; set; }
    [Key(2)] public bool Jump { get; set; }
    [Key(3)] public bool Fire { get; set; }
    [Key(4)] public float AimYaw { get; set; }
    [Key(5)] public float AimPitch { get; set; }
    [Key(6)] public long Timestamp { get; set; }
}
```

### Custom Method Names (Optional)

```csharp
[ShaRpcService(Name = "Game")]
public interface IGameService
{
    [ShaRpcMethod(Name = "Info")]
    Task<ServerInfo> GetServerInfoAsync(CancellationToken ct = default);
}
```

---

## Server Implementation

### Service Implementation

```csharp
// YourGame.Server/GameService.cs
using YourGame.Shared;

namespace YourGame.Server;

public class GameService : IGameService
{
    private readonly GameState _gameState;

    public GameService(GameState gameState)
    {
        _gameState = gameState;
    }

    public Task<ServerInfo> GetServerInfoAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new ServerInfo
        {
            ServerName = _gameState.ServerName,
            PlayerCount = _gameState.Players.Count,
            MaxPlayers = _gameState.MaxPlayers,
            Version = "1.0.0"
        });
    }

    public Task<PlayerState> GetPlayerAsync(string playerId, CancellationToken ct = default)
    {
        if (!_gameState.Players.TryGetValue(playerId, out var player))
        {
            throw new KeyNotFoundException($"Player '{playerId}' not found");
        }
        return Task.FromResult(player.ToPlayerState());
    }

    public Task<JoinResult> JoinGameAsync(JoinRequest request, CancellationToken ct = default)
    {
        // Validate auth token
        if (!ValidateToken(request.AuthToken))
        {
            return Task.FromResult(new JoinResult
            {
                Success = false,
                ErrorMessage = "Invalid authentication"
            });
        }

        // Create player
        var player = _gameState.CreatePlayer(request.DisplayName);

        return Task.FromResult(new JoinResult
        {
            Success = true,
            PlayerId = player.Id,
            InitialState = player.ToPlayerState()
        });
    }

    public Task SendInputAsync(PlayerInput input, CancellationToken ct = default)
    {
        _gameState.ProcessInput(input);
        return Task.CompletedTask;
    }

    private bool ValidateToken(string token) => !string.IsNullOrEmpty(token);
}
```

### Server Startup

```csharp
// YourGame.Server/Program.cs
using YourGame.Server;
using YourGame.Shared;
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

const int Port = 7777;

Console.WriteLine($"Starting game server on port {Port}...");

var gameState = new GameState();
var gameService = new GameService(gameState);

// RpcHost listens and turns every accepted connection into a peer. ForEachPeer
// runs once per connection to provide the services that peer exposes.
await using var host = RpcHost
    .Listen(new TcpServerTransport(Port), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvideGameService(gameService)); // Generated extension method

await host.StartAsync();
Console.WriteLine("Server started. Press Ctrl+C to stop.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) { }

await host.StopAsync();
Console.WriteLine("Server stopped.");
```

---

## Unity Client Setup

### NetworkManager Component

```csharp
// Assets/Scripts/Networking/NetworkManager.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;
using YourGame.Shared;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    [Header("Connection Settings")]
    [SerializeField] private string serverHost = "localhost";
    [SerializeField] private int serverPort = 7777;
    [SerializeField] private float connectionTimeout = 10f;

    public IGameService GameService { get; private set; }
    public bool IsConnected => _peer?.IsConnected ?? false;

    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<string> OnConnectionError;

    private RpcPeer _peer;
    private TcpTransport _transport;
    private CancellationTokenSource _cts;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    public async Task<bool> ConnectAsync(string host = null, int? port = null)
    {
        if (IsConnected)
        {
            Debug.LogWarning("Already connected");
            return true;
        }

        host ??= serverHost;
        port ??= serverPort;

        _cts = new CancellationTokenSource();

        try
        {
            Debug.Log($"Connecting to {host}:{port}...");

            _transport = new TcpTransport(host, port.Value);
            var serializer = new MessagePackRpcSerializer();

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(connectionTimeout));

            // Establish the underlying connection, then layer a peer over it.
            await _transport.ConnectAsync(connectCts.Token);

            _peer = RpcPeer
                .Over(_transport.Connection!, serializer, new RpcPeerOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(connectionTimeout),
                    // This client only calls out; it never serves inbound calls.
                    RejectInboundCalls = true
                })
                .Start();

            // Create the service proxy
            GameService = _peer.GetGameService();

            Debug.Log("Connected to server!");
            OnConnected?.Invoke();

            return true;
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("Connection cancelled");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Connection failed: {ex.Message}");
            OnConnectionError?.Invoke(ex.Message);
            return false;
        }
    }

    public void Disconnect()
    {
        if (_peer == null) return;

        _cts?.Cancel();

        try
        {
            // Disposing the peer stops its read loop; dispose the transport to close the socket.
            _peer.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            _transport?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch { }

        _peer = null;
        _transport = null;
        GameService = null;
        _cts?.Dispose();
        _cts = null;

        Debug.Log("Disconnected from server");
        OnDisconnected?.Invoke();
    }

    // Helper for fire-and-forget calls with error logging
    public async void SendAsync(Func<Task> action)
    {
        if (!IsConnected)
        {
            Debug.LogWarning("Not connected");
            return;
        }

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Debug.LogError($"RPC call failed: {ex.Message}");
        }
    }
}
```

### Usage in Game Scripts

```csharp
// Assets/Scripts/Game/GameClient.cs
using System.Threading.Tasks;
using UnityEngine;
using YourGame.Shared;

public class GameClient : MonoBehaviour
{
    [SerializeField] private string playerName = "Player";

    private string _playerId;

    private async void Start()
    {
        var network = NetworkManager.Instance;

        // Connect to server
        if (!await network.ConnectAsync())
        {
            Debug.LogError("Failed to connect!");
            return;
        }

        // Get server info
        var serverInfo = await network.GameService.GetServerInfoAsync();
        Debug.Log($"Connected to '{serverInfo.ServerName}' ({serverInfo.PlayerCount}/{serverInfo.MaxPlayers})");

        // Join the game
        var joinResult = await network.GameService.JoinGameAsync(new JoinRequest
        {
            DisplayName = playerName,
            AuthToken = GetAuthToken()
        });

        if (!joinResult.Success)
        {
            Debug.LogError($"Failed to join: {joinResult.ErrorMessage}");
            return;
        }

        _playerId = joinResult.PlayerId;
        Debug.Log($"Joined as {joinResult.InitialState.DisplayName} (ID: {_playerId})");

        // Initialize player position
        transform.position = new Vector3(
            joinResult.InitialState.PositionX,
            joinResult.InitialState.PositionY,
            joinResult.InitialState.PositionZ
        );
    }

    private void Update()
    {
        if (string.IsNullOrEmpty(_playerId)) return;

        // Send input to server
        var input = new PlayerInput
        {
            MoveX = Input.GetAxis("Horizontal"),
            MoveY = Input.GetAxis("Vertical"),
            Jump = Input.GetButtonDown("Jump"),
            Fire = Input.GetButton("Fire1"),
            AimYaw = transform.eulerAngles.y,
            AimPitch = Camera.main.transform.eulerAngles.x,
            Timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Fire-and-forget with error handling
        NetworkManager.Instance.SendAsync(() =>
            NetworkManager.Instance.GameService.SendInputAsync(input));
    }

    private string GetAuthToken()
    {
        // Implement your authentication logic
        return "demo-token";
    }
}
```

### UI Integration

```csharp
// Assets/Scripts/UI/ConnectionUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ConnectionUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField hostInput;
    [SerializeField] private TMP_InputField portInput;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private TMP_Text statusText;

    private void Start()
    {
        var network = NetworkManager.Instance;

        network.OnConnected += () => UpdateUI(true);
        network.OnDisconnected += () => UpdateUI(false);
        network.OnConnectionError += error => statusText.text = $"Error: {error}";

        connectButton.onClick.AddListener(OnConnectClicked);
        disconnectButton.onClick.AddListener(OnDisconnectClicked);

        UpdateUI(false);
    }

    private async void OnConnectClicked()
    {
        connectButton.interactable = false;
        statusText.text = "Connecting...";

        var host = string.IsNullOrEmpty(hostInput.text) ? "localhost" : hostInput.text;
        var port = int.TryParse(portInput.text, out var p) ? p : 7777;

        var success = await NetworkManager.Instance.ConnectAsync(host, port);

        if (success)
        {
            statusText.text = "Connected!";
        }
        else
        {
            connectButton.interactable = true;
        }
    }

    private void OnDisconnectClicked()
    {
        NetworkManager.Instance.Disconnect();
    }

    private void UpdateUI(bool connected)
    {
        connectButton.gameObject.SetActive(!connected);
        disconnectButton.gameObject.SetActive(connected);
        hostInput.interactable = !connected;
        portInput.interactable = !connected;

        if (!connected)
        {
            statusText.text = "Disconnected";
            connectButton.interactable = true;
        }
    }
}
```

---

## Connection Management

### Automatic Reconnection

```csharp
// Assets/Scripts/Networking/ReconnectionHandler.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class ReconnectionHandler : MonoBehaviour
{
    [SerializeField] private float reconnectDelay = 2f;
    [SerializeField] private float maxReconnectDelay = 30f;
    [SerializeField] private int maxAttempts = 10;

    private CancellationTokenSource _cts;
    private bool _shouldReconnect = true;

    private void Start()
    {
        NetworkManager.Instance.OnDisconnected += OnDisconnected;
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async void OnDisconnected()
    {
        if (!_shouldReconnect) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        await AttemptReconnection(_cts.Token);
    }

    private async Task AttemptReconnection(CancellationToken ct)
    {
        var delay = reconnectDelay;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) return;

            Debug.Log($"Reconnection attempt {attempt}/{maxAttempts}...");

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);

                if (await NetworkManager.Instance.ConnectAsync())
                {
                    Debug.Log("Reconnected successfully!");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Exponential backoff
            delay = Math.Min(delay * 1.5f, maxReconnectDelay);
        }

        Debug.LogError("Failed to reconnect after maximum attempts");
    }

    public void StopReconnection()
    {
        _shouldReconnect = false;
        _cts?.Cancel();
    }

    public void EnableReconnection()
    {
        _shouldReconnect = true;
    }
}
```

### Connection Health Monitoring

```csharp
// Assets/Scripts/Networking/ConnectionHealth.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class ConnectionHealth : MonoBehaviour
{
    [SerializeField] private float pingInterval = 5f;

    public float LastPingMs { get; private set; }
    public event Action<float> OnPingUpdated;

    private CancellationTokenSource _cts;

    private void Start()
    {
        NetworkManager.Instance.OnConnected += StartPinging;
        NetworkManager.Instance.OnDisconnected += StopPinging;
    }

    private void OnDestroy()
    {
        StopPinging();
    }

    private async void StartPinging()
    {
        _cts = new CancellationTokenSource();

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(pingInterval), _cts.Token);

                var start = DateTime.UtcNow;
                await NetworkManager.Instance.GameService.GetServerInfoAsync(_cts.Token);
                LastPingMs = (float)(DateTime.UtcNow - start).TotalMilliseconds;

                OnPingUpdated?.Invoke(LastPingMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Ping failed: {ex.Message}");
            }
        }
    }

    private void StopPinging()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
```

---

## Error Handling

### Exception Types

```csharp
using ShaRPC.Core.Exceptions;

try
{
    var result = await NetworkManager.Instance.GameService.GetPlayerAsync(playerId);
}
catch (ShaRpcTimeoutException)
{
    // Request timed out
    Debug.LogWarning("Request timed out - server may be overloaded");
}
catch (ShaRpcRemoteException ex)
{
    // Server threw an exception
    Debug.LogError($"Server error ({ex.RemoteExceptionType}): {ex.Message}");
}
catch (ShaRpcConnectionException)
{
    // Connection lost
    Debug.LogError("Connection lost");
    // Trigger reconnection logic
}
catch (ShaRpcException ex)
{
    // Other RPC errors
    Debug.LogError($"RPC error: {ex.Message}");
}
```

### Wrapper for Safe Calls

```csharp
// Assets/Scripts/Networking/RpcHelper.cs
using System;
using System.Threading.Tasks;
using UnityEngine;
using ShaRPC.Core.Exceptions;

public static class RpcHelper
{
    public static async Task<T> SafeCallAsync<T>(
        Func<Task<T>> call,
        T defaultValue = default,
        Action<Exception> onError = null)
    {
        try
        {
            return await call();
        }
        catch (ShaRpcTimeoutException ex)
        {
            Debug.LogWarning($"RPC timeout: {ex.Message}");
            onError?.Invoke(ex);
            return defaultValue;
        }
        catch (ShaRpcRemoteException ex)
        {
            Debug.LogError($"Server error: {ex.Message}");
            onError?.Invoke(ex);
            return defaultValue;
        }
        catch (ShaRpcConnectionException ex)
        {
            Debug.LogError($"Connection error: {ex.Message}");
            onError?.Invoke(ex);
            return defaultValue;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Unexpected error: {ex}");
            onError?.Invoke(ex);
            return defaultValue;
        }
    }

    public static async Task SafeCallAsync(
        Func<Task> call,
        Action<Exception> onError = null)
    {
        try
        {
            await call();
        }
        catch (Exception ex)
        {
            Debug.LogError($"RPC error: {ex.Message}");
            onError?.Invoke(ex);
        }
    }
}

// Usage:
var serverInfo = await RpcHelper.SafeCallAsync(
    () => NetworkManager.Instance.GameService.GetServerInfoAsync(),
    defaultValue: new ServerInfo { ServerName = "Unknown" },
    onError: ex => ShowErrorPopup(ex.Message)
);
```

---

## IL2CPP and AOT Considerations

### MessagePack AOT Configuration

For IL2CPP builds, MessagePack needs AOT code generation hints:

```csharp
// Assets/Scripts/AOT/MessagePackAOTSetup.cs
using MessagePack;
using MessagePack.Resolvers;
using UnityEngine;

public static class MessagePackAOTSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Configure MessagePack for AOT
        StaticCompositeResolver.Instance.Register(
            GeneratedResolver.Instance,  // Your generated resolver
            StandardResolver.Instance
        );

        var options = MessagePackSerializerOptions.Standard
            .WithResolver(StaticCompositeResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData);

        MessagePackSerializer.DefaultOptions = options;
    }
}
```

### Generate MessagePack Formatters

Add to your shared project:

```csharp
// YourGame.Shared/Generated/GeneratedResolver.cs
// This file is auto-generated by MessagePack.Generator

using MessagePack;
using MessagePack.Formatters;

public class GeneratedResolver : IFormatterResolver
{
    public static readonly GeneratedResolver Instance = new();

    public IMessagePackFormatter<T> GetFormatter<T>()
    {
        return FormatterCache<T>.Formatter;
    }

    private static class FormatterCache<T>
    {
        public static readonly IMessagePackFormatter<T> Formatter;

        static FormatterCache()
        {
            // Formatter lookup logic
        }
    }
}
```

Use `mpc` (MessagePack Compiler) to generate formatters:

```bash
dotnet tool install -g MessagePack.Generator
mpc -i src/YourGame.Shared/YourGame.Shared.csproj -o src/YourGame.Shared/Generated
```

### Link.xml for IL2CPP

Create `Assets/link.xml` to prevent code stripping:

```xml
<linker>
    <!-- Preserve ShaRPC -->
    <assembly fullname="ShaRPC.Core" preserve="all"/>
    <assembly fullname="ShaRPC.Transports.Tcp" preserve="all"/>
    <assembly fullname="ShaRPC.Serializers.MessagePack" preserve="all"/>

    <!-- Preserve your shared library -->
    <assembly fullname="YourGame.Shared" preserve="all"/>

    <!-- Preserve MessagePack -->
    <assembly fullname="MessagePack" preserve="all"/>

    <!-- Preserve System.Buffers for ArrayPool -->
    <assembly fullname="System.Buffers" preserve="all"/>
</linker>
```

---

## Best Practices

### 1. Main Thread Dispatching

RPC callbacks may not be on the main thread. Use a dispatcher:

```csharp
// Assets/Scripts/Util/MainThreadDispatcher.cs
using System;
using System.Collections.Concurrent;
using UnityEngine;

public class MainThreadDispatcher : MonoBehaviour
{
    public static MainThreadDispatcher Instance { get; private set; }

    private readonly ConcurrentQueue<Action> _actions = new();

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        while (_actions.TryDequeue(out var action))
        {
            action?.Invoke();
        }
    }

    public void Enqueue(Action action)
    {
        _actions.Enqueue(action);
    }

    public static void RunOnMainThread(Action action)
    {
        Instance?.Enqueue(action);
    }
}

// Usage in async code:
var result = await service.GetPlayerAsync(id);
MainThreadDispatcher.RunOnMainThread(() => {
    // Safe to access Unity APIs here
    playerObject.transform.position = new Vector3(result.PositionX, ...);
});
```

### 2. Request Batching

Batch multiple requests to reduce overhead:

```csharp
public class RequestBatcher
{
    private readonly List<PlayerInput> _pendingInputs = new();
    private readonly object _lock = new();
    private float _lastSendTime;
    private const float SendInterval = 0.05f; // 20 times per second

    public void QueueInput(PlayerInput input)
    {
        lock (_lock)
        {
            _pendingInputs.Add(input);
        }
    }

    public void Update()
    {
        if (Time.time - _lastSendTime < SendInterval) return;

        PlayerInput[] toSend;
        lock (_lock)
        {
            if (_pendingInputs.Count == 0) return;
            toSend = _pendingInputs.ToArray();
            _pendingInputs.Clear();
        }

        // Send batched inputs
        NetworkManager.Instance.SendAsync(() =>
            NetworkManager.Instance.GameService.SendBatchedInputAsync(toSend));

        _lastSendTime = Time.time;
    }
}
```

### 3. Cancellation Token Usage

Always pass cancellation tokens for proper cleanup:

```csharp
public class GameClient : MonoBehaviour
{
    private CancellationTokenSource _cts;

    private void OnEnable()
    {
        _cts = new CancellationTokenSource();
    }

    private void OnDisable()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async Task FetchDataAsync()
    {
        try
        {
            var data = await NetworkManager.Instance.GameService
                .GetPlayerAsync(playerId, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Component was disabled, ignore
        }
    }
}
```

### 4. Connection State Checks

Always verify connection before making calls:

```csharp
public async Task<bool> TrySendInput(PlayerInput input)
{
    if (!NetworkManager.Instance.IsConnected)
    {
        Debug.LogWarning("Cannot send input: not connected");
        return false;
    }

    try
    {
        await NetworkManager.Instance.GameService.SendInputAsync(input);
        return true;
    }
    catch
    {
        return false;
    }
}
```

---

## Troubleshooting

### Common Issues

#### "TypeLoadException" or "MissingMethodException" in IL2CPP

**Cause**: Code stripping removed required types.

**Solution**:
1. Add types to `link.xml`
2. Ensure MessagePack AOT generation is set up
3. Use `[Preserve]` attribute on model classes

#### "Connection refused" error

**Cause**: Server not running or wrong port/host.

**Solution**:
1. Verify server is running: `netstat -an | grep <port>`
2. Check firewall settings
3. Use correct IP (not `localhost` for device builds)

#### Timeout on all requests

**Cause**: Serialization mismatch or protocol error.

**Solution**:
1. Ensure same MessagePack version on client and server
2. Rebuild shared library and update Unity DLLs
3. Check for model property mismatches

#### "Operation cancelled" immediately

**Cause**: CancellationToken cancelled too early.

**Solution**:
1. Don't pass disposed/cancelled tokens
2. Check component lifecycle (OnDisable cancelling tokens)

#### High latency / slow responses

**Cause**: Network issues or server overload.

**Solution**:
1. Use connection health monitoring
2. Implement request timeouts
3. Check for blocking operations on server

### Debug Logging

Enable detailed logging:

```csharp
// Add to NetworkManager
#if UNITY_EDITOR || DEVELOPMENT_BUILD
private void LogRpcCall(string method, object request = null)
{
    Debug.Log($"[RPC] {method}" + (request != null ? $": {JsonUtility.ToJson(request)}" : ""));
}
#endif
```

---

## Advanced Topics

### Custom Transport

Implement your own transport for platforms like Steam or Epic:

```csharp
public class SteamTransport : ITransport
{
    private CSteamID _serverId;
    private SteamConnection _connection;

    public IRpcChannel Connection => _connection;
    public bool IsConnected => _connection?.IsConnected ?? false;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Implement Steam networking connection
        var result = await SteamNetworking.ConnectP2P(_serverId);
        _connection = new SteamConnection(result);
    }

    public ValueTask DisposeAsync()
    {
        _connection?.Dispose();
        return default;
    }
}
```

### Multiple Services

Register multiple services on the same server:

```csharp
// Shared
[ShaRpcService] public interface IGameService { ... }
[ShaRpcService] public interface IChatService { ... }
[ShaRpcService] public interface IInventoryService { ... }

// Server: provide every service on each accepted peer
await using var host = RpcHost
    .Listen(serverTransport, serializer)
    .ForEachPeer(peer =>
    {
        peer.ProvideGameService(gameService);
        peer.ProvideChatService(chatService);
        peer.ProvideInventoryService(inventoryService);
    });

// Client: get a proxy per service from the same peer
var game = peer.GetGameService();
var chat = peer.GetChatService();
var inventory = peer.GetInventoryService();
```

### Server-to-Client Notifications (Future)

While ShaRPC currently supports request/response patterns, you can implement push notifications using a polling pattern or extend the protocol:

```csharp
// Polling approach
public class NotificationPoller : MonoBehaviour
{
    private async void Start()
    {
        while (NetworkManager.Instance.IsConnected)
        {
            var notifications = await NetworkManager.Instance.GameService
                .PollNotificationsAsync(_lastNotificationId);

            foreach (var notification in notifications)
            {
                ProcessNotification(notification);
                _lastNotificationId = notification.Id;
            }

            await Task.Delay(100); // Poll every 100ms
        }
    }
}
```

---

## Version Compatibility

| ShaRPC Version | Unity Version | .NET Server | MessagePack |
|----------------|---------------|-------------|-------------|
| 1.0.x          | 2021.3+       | 6.0+        | 2.5.x       |

---

## Additional Resources

- [MessagePack-CSharp Documentation](https://github.com/neuecc/MessagePack-CSharp)
- [Unity .NET Profile](https://docs.unity3d.com/Manual/dotnetProfileSupport.html)
- [IL2CPP Code Stripping](https://docs.unity3d.com/Manual/ManagedCodeStripping.html)
