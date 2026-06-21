using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerStringConcatTests
{
    [Fact]
    public async Task Generated_package_lowers_string_concat_in_handle()
    {
        var package = CreatePackage();
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallAsync(package);

        await kernel.HandleAsync(new StringConcatEventAdapter(), new StringConcatEvent("player-1", "matched"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("prefix-matched", message.Message);
    }

    [Fact]
    public async Task Generated_should_handle_executes_string_concat_in_compiled_mode()
    {
        var package = CreatePackage();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(new InMemoryPluginMessageSink());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithWallTime(TimeSpan.FromSeconds(30))
            .WithMaxHostCalls(1_000)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);

        var matching = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            Input("matched"),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });
        var rejected = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            Input("miss"),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(matching.Succeeded, ExecutionFailure(matching));
        Assert.True(rejected.Succeeded, ExecutionFailure(rejected));
        Assert.Equal(ExecutionMode.Compiled, matching.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, rejected.ActualMode);
        Assert.True(((BoolValue)matching.Value!).Value);
        Assert.False(((BoolValue)rejected.Value!).Value);
    }

    private static PluginPackage CreatePackage()
        => PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record StringConcatEvent(string TargetId, string Message);

            [Plugin("generated-string-concat")]
            public sealed partial class StringConcatKernel : IEventKernel<StringConcatEvent>
            {
                public bool ShouldHandle(StringConcatEvent e, HookContext ctx)
                    => "prefix-" + e.Message == "prefix-matched";

                public void Handle(StringConcatEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "prefix-" + e.Message);
            }
            """, "Sample.StringConcatPluginPackage");

    private static SandboxValue Input(string message)
        => SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromString(message)
        ]);

    private static string ExecutionFailure(SandboxExecutionResult result)
        => result.Error?.SafeMessage + Environment.NewLine +
           string.Join(Environment.NewLine, result.AuditEvents.Select(e => $"{e.Kind}: {e.ErrorCode} {e.Message}"));

    private sealed record StringConcatEvent(string TargetId, string Message);

    private sealed class StringConcatEventAdapter : IPluginEventAdapter<StringConcatEvent>
    {
        public string EventName => nameof(StringConcatEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_TargetId", SandboxType.String),
            new("e_Message", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(StringConcatEvent e)
            => [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.Message)
            ];
    }
}
