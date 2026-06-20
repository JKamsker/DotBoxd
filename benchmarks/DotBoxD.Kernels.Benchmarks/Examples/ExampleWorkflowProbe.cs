using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Benchmarks.Examples;

using System.Diagnostics;
using DotBoxD.Kernels;

internal static class ExampleWorkflowProbe
{
    public static async Task RunAsync()
    {
        const int iterations = 50_000;
        const int warmup = 1_000;
        var fire = new DamageEvent("fire", 120, "player-1");
        var ice = new DamageEvent("ice", 300, "player-2");

        var mixed = await MeasureCaseAsync("mixed fire/ice", fire, ice, warmup, iterations);
        var miss = await MeasureCaseAsync("predicate miss", ice, null, warmup, iterations);
        var hit = await MeasureCaseAsync("predicate hit", fire, null, warmup, iterations);

        Console.WriteLine("case                         handwritten   native hook      x   compiled      x   interpreted      x");
        WriteCase(mixed);
        WriteCase(miss);
        WriteCase(hit);
        Console.WriteLine($"compiled recent modes: {mixed.Compiled.ObservationSummary}");
        Console.WriteLine($"interpreted recent modes: {mixed.Interpreted.ObservationSummary}");
    }

    private static async Task<WorkflowCaseSummary> MeasureCaseAsync(
        string name,
        DamageEvent primary,
        DamageEvent? secondary,
        int warmup,
        int iterations)
    {
        _ = RunHandwritten(primary, secondary, warmup);
        var handwrittenMs = Time(() => RunHandwritten(primary, secondary, iterations));
        var nativeHookMs = await TimeNativeHookDispatchAsync(primary, secondary, warmup, iterations);
        var compiledRun = await TimeKernelDispatchAsync(primary, secondary, warmup, iterations, ExecutionMode.Compiled);
        var interpretedRun = await TimeKernelDispatchAsync(primary, secondary, warmup, iterations, ExecutionMode.Interpreted);
        return new WorkflowCaseSummary(name, handwrittenMs, nativeHookMs, compiledRun, interpretedRun);
    }

    private static void WriteCase(WorkflowCaseSummary summary)
    {
        var handwrittenMs = summary.HandwrittenMilliseconds;
        var nativeHookMs = summary.NativeHookMilliseconds;
        var compiledMs = summary.Compiled.Milliseconds;
        var interpretedMs = summary.Interpreted.Milliseconds;
        Console.WriteLine(
            $"{summary.Name,-20} {handwrittenMs,9:N1} ms {nativeHookMs,11:N1} ms {nativeHookMs / handwrittenMs,6:N1} {compiledMs,9:N1} ms {compiledMs / handwrittenMs,5:N1} {interpretedMs,12:N1} ms {interpretedMs / handwrittenMs,6:N1}");
    }

    private static int RunHandwritten(DamageEvent primary, DamageEvent? secondary, int iterations)
    {
        var messages = new List<PluginMessage>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var e = EventAt(primary, secondary, i);
            if (e.DamageType == "fire" && e.Amount >= 100)
            {
                messages.Add(new PluginMessage(e.TargetId, "Ouch, fire."));
            }
        }

        return messages.Count;
    }

    private static async Task<double> TimeNativeHookDispatchAsync(
        DamageEvent primary,
        DamageEvent? secondary,
        int warmup,
        int iterations)
    {
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages);
        server.Hooks.On(DamageEventAdapter.Instance)
            .Where((e, _) => e.DamageType == "fire")
            .Where((e, _) => e.Amount >= 100)
            .InvokeHostHandler((e, ctx) => ctx.Messages.Send(e.TargetId, "Ouch, fire."));

        await PublishLoopAsync(server, primary, secondary, warmup);
        return await TimeAsync(() => PublishLoopAsync(server, primary, secondary, iterations));
    }

    private static async Task<KernelRunSummary> TimeKernelDispatchAsync(
        DamageEvent primary,
        DamageEvent? secondary,
        int warmup,
        int iterations,
        ExecutionMode mode)
    {
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            messages,
            defaultPolicy: MessageWritePolicy(),
            executionMode: mode);
        var kernel = await server.InstallJsonAsync(FireDamagePackageJson());
        server.Hooks.On(DamageEventAdapter.Instance).Use(kernel);

        await PublishLoopAsync(server, primary, secondary, warmup);
        var milliseconds = await TimeAsync(() => PublishLoopAsync(server, primary, secondary, iterations));
        return new KernelRunSummary(messages.Messages.Count, milliseconds, SummarizeObservations(kernel.ExecutionObservations));
    }

    private static async Task PublishLoopAsync(
        DotBoxD.Plugins.PluginServer server,
        DamageEvent primary,
        DamageEvent? secondary,
        int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            await server.Hooks.PublishAsync(EventAt(primary, secondary, i));
        }
    }

    private static DamageEvent EventAt(DamageEvent primary, DamageEvent? secondary, int index)
        => secondary is null || (index & 1) == 0 ? primary : secondary;

    private static string SummarizeObservations(IReadOnlyList<PluginExecutionObservation> observations)
        => string.Join(
            ", ",
            observations
                .GroupBy(item => (
                    item.Entrypoint,
                    item.ActualMode,
                    item.FallbackReason,
                    item.CacheStatus,
                    item.MaterializationStatus))
                .Select(group =>
                    $"{group.Key.Entrypoint}:{group.Key.ActualMode}/fallback={group.Key.FallbackReason?.ToString() ?? "none"}/cache={group.Key.CacheStatus}/mat={group.Key.MaterializationStatus ?? "none"} x{group.Count()}"));

    private static SandboxPolicy MessageWritePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private static double Time(Func<object> action)
    {
        var sw = Stopwatch.StartNew();
        GC.KeepAlive(action());
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static async Task<double> TimeAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    internal static string FireDamagePackageJson()
        => """
        {
          "manifest": {
            "pluginId": "benchmark-fire-damage",
            "contract": "IEventKernel<DamageEvent>",
            "mode": "Auto",
            "effects": ["Cpu", "Alloc", "HostStateWrite", "Audit"],
            "liveSettings": [
              { "name": "DamageType", "type": "string", "defaultValue": "fire" },
              { "name": "MinDamage", "type": "int", "defaultValue": 100, "min": 0, "max": 10000 }
            ],
            "subscriptions": [
              { "event": "DamageEvent", "kernel": "FireDamageKernel" }
            ]
          },
          "module": {
            "id": "benchmark-fire-damage",
            "version": "1.0.0",
            "targetSandboxVersion": "1.0.0",
            "capabilityRequests": [
              { "id": "host.message.write", "reason": "send host messages" }
            ],
            "metadata": { "pluginId": "benchmark-fire-damage", "kernel": "FireDamageKernel" },
            "functions": [
              {
                "id": "ShouldHandle",
                "visibility": "entrypoint",
                "parameters": [
                  { "name": "e_DamageType", "type": "String" },
                  { "name": "e_Amount", "type": "I32" },
                  { "name": "e_TargetId", "type": "String" },
                  { "name": "DamageType", "type": "String" },
                  { "name": "MinDamage", "type": "I32" }
                ],
                "returnType": "Bool",
                "body": [
                  { "op": "return", "value": {
                    "op": "and",
                    "left": { "op": "eq", "left": { "var": "e_DamageType" }, "right": { "var": "DamageType" } },
                    "right": { "op": "gte", "left": { "var": "e_Amount" }, "right": { "var": "MinDamage" } }
                  } }
                ]
              },
              {
                "id": "Handle",
                "visibility": "entrypoint",
                "parameters": [
                  { "name": "e_DamageType", "type": "String" },
                  { "name": "e_Amount", "type": "I32" },
                  { "name": "e_TargetId", "type": "String" },
                  { "name": "DamageType", "type": "String" },
                  { "name": "MinDamage", "type": "I32" }
                ],
                "returnType": "Unit",
                "body": [
                  { "op": "return", "value": {
                    "call": "host.message.send",
                    "args": [
                      { "var": "e_TargetId" },
                      { "string": "Ouch, fire." }
                    ]
                  } }
                ]
              }
            ]
          }
        }
        """;

    internal sealed record DamageEvent(string DamageType, int Amount, string TargetId);

    private readonly record struct KernelRunSummary(int Messages, double Milliseconds, string ObservationSummary)
    {
        public static KernelRunSummary Empty { get; } = new(0, 0, "");
    }

    private readonly record struct WorkflowCaseSummary(
        string Name,
        double HandwrittenMilliseconds,
        double NativeHookMilliseconds,
        KernelRunSummary Compiled,
        KernelRunSummary Interpreted);

    internal sealed class DamageEventAdapter : IPluginEventValueWriter<DamageEvent>
    {
        public static DamageEventAdapter Instance { get; } = new();

        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public int EventValueCount => 3;

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];

        public SandboxValue ToSandboxValue(DamageEvent e, int index)
            => index switch
            {
                0 => SandboxValue.FromString(e.DamageType),
                1 => SandboxValue.FromInt32(e.Amount),
                2 => SandboxValue.FromString(e.TargetId),
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

        public void CopySandboxValues(DamageEvent e, SandboxValue[] destination, int destinationIndex)
        {
            destination[destinationIndex] = SandboxValue.FromString(e.DamageType);
            destination[destinationIndex + 1] = SandboxValue.FromInt32(e.Amount);
            destination[destinationIndex + 2] = SandboxValue.FromString(e.TargetId);
        }
    }
}
