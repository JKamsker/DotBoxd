---
title: 'Host bindings'
description: 'How kernels call host-owned APIs safely through explicit, policy-gated bindings instead of arbitrary CLR access.'
---
Host bindings are how a kernel calls host-owned APIs without giving sandboxed code arbitrary CLR
access. The host chooses the callable surface, registers descriptors for it, grants capabilities in a
policy, and the kernel can only call those registered binding ids.

This already supports arbitrary external APIs. "Arbitrary" means any host API can be wrapped by the
host. It does not mean a plugin can name and invoke arbitrary .NET methods from sandboxed code.

## Recommended route: host services

For normal host APIs, define a service contract and register its implementation with
`SandboxHostBuilder.AddBindingsFrom<TService>()`. The analyzer lowers calls to the service into
verified kernel IR, while the host registration creates matching runtime bindings from the same
contract metadata.

```csharp
using DotBoxD.Abstractions;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Services.Attributes;

namespace Example;

[DotBoxDService]
public interface IWeatherApi
{
    [HostCapability("weather.current.read", HostBindingEffect.HostStateRead)]
    int CurrentCelsius(string city);
}

public sealed class WeatherApi : IWeatherApi
{
    public int CurrentCelsius(string city)
    {
        // Call any host-owned API here: database, process-local service, external SDK, HTTP client,
        // etc.
        return city.Equals("Vienna", StringComparison.OrdinalIgnoreCase) ? 24 : 18;
    }
}

using var server = PluginServer.Create(
    configureHost: host => host.AddBindingsFrom<IWeatherApi>(new WeatherApi()),
    defaultPolicy: SandboxPolicyBuilder.Create()
        .Grant("weather.current.read", new { }, SandboxEffect.HostStateRead)
        .WithFuel(100_000)
        .WithMaxHostCalls(1_000)
        .WithWallTime(TimeSpan.FromSeconds(10))
        .Build());
```

Kernel authoring code calls the service through `HookContext.Host<T>()` or through an injected service
field. The generator lowers the call into a sandbox `CallExpression` and adds the declared capability
and effects to the generated manifest.

```csharp
using DotBoxD.Abstractions;
using DotBoxD.Plugins;

namespace Example;

[ServerExtension("weather-score")]
public sealed partial class WeatherScoreKernel
{
    public int Score(string city, HookContext context)
    {
        var celsius = context.Host<IWeatherApi>().CurrentCelsius(city);
        return celsius >= 25 ? 1 : 0;
    }
}
```

For auto-bound `[DotBoxDService]` methods, the binding id is derived as
`host.{namespace}.{type-metadata-name}.{method-name}`. For the example above that is
`host.Example.IWeatherApi.CurrentCelsius`. The capability and effects come from `[HostCapability]`.

Use `HostBindingEffect.Allocates` when the return shape allocates, such as `string`, `Guid`, records,
lists, or maps:

```csharp
[HostCapability("weather.forecast.read", HostBindingEffect.HostStateRead | HostBindingEffect.Allocates)]
WeatherForecast Forecast(string city);
```

If a host method returns `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`, grant the runtime async
capability with `SandboxPolicyBuilder.AllowRuntimeAsync()`.

## Explicit binding route

Use a hand-written `BindingDescriptor` when you need exact control over binding ids, cost models,
resource audit ids, custom grant validation, or a shape that is not naturally represented by a
`[DotBoxDService]` contract.

```csharp
using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

static int ReadCurrentCelsius(string city)
    => city.Equals("Vienna", StringComparison.OrdinalIgnoreCase) ? 24 : 18;

builder.AddBinding(new BindingDescriptor(
    "host.weather.currentCelsius",
    SemVersion.One,
    [SandboxType.String],
    SandboxType.I32,
    SandboxEffect.Cpu | SandboxEffect.HostStateRead,
    "weather.current.read",
    BindingCostModel.Fixed(2),
    AuditLevel.PerResource,
    BindingSafety.ReadOnlyExternal,
    (context, args, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();

        var city = ((StringValue)args[0]).Value;
        var startedAt = DateTimeOffset.UtcNow;
        var value = ReadCurrentCelsius(city);

        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "BindingCall",
            startedAt,
            true,
            BindingId: "host.weather.currentCelsius",
            CapabilityId: "weather.current.read",
            Effect: SandboxEffect.HostStateRead,
            ResourceId: $"weather:{city}",
            Fields: context.BindingAuditFields("weather", startedAt)));

        return ValueTask.FromResult(SandboxValue.FromInt32(value));
    },
    CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
    GrantValidator: static (_, _) => { }));
```

Kernel C# can target that explicit id by putting `[HostBinding]` on the callable contract:

```csharp
[HostBinding(
    "host.weather.currentCelsius",
    "weather.current.read",
    SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
int CurrentCelsius(string city);
```

The explicit id, capability, effects, argument types, return type, and async flag must match the
registered descriptor. Validation fails closed when the kernel references a binding that is not
registered or when the policy does not grant its capability/effects.

## Security notes

- Keep bindings narrow. Prefer small capability-scoped methods over a generic "execute request" API.
- Do not return secrets to kernels and do not put secrets in capability ids, audit fields, or resource
  ids. Host implementations should read credentials internally from their normal configuration.
- Treat each binding as part of the security boundary: choose read/write effects accurately, set host
  call and allocation budgets, and write useful audit events for side-effecting or externally visible
  calls.
- The sandbox still never accepts arbitrary C#, IL, assemblies, reflection targets, or CLR member names
  from untrusted plugins.
