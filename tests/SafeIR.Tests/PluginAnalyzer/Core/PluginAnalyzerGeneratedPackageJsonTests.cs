using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAnalyzerGeneratedPackageJsonTests
{
    [Fact]
    public async Task Generated_package_exports_to_json_and_installs_from_json_boundary()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using SafeIR.Plugins;
            using SafeIR.Server.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, string DamageType, int Amount);

            [Plugin("generated-json-export")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public string DamageType { get; set; } = "fire";

                [LiveSetting]
                public int MinDamage { get; set; } = 100;

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.DamageType == DamageType && e.Amount >= MinDamage;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        var json = PluginPackageJsonSerializer.Export(package, indented: true);
        var imported = PluginPackageJsonSerializer.Import(json);

        Assert.Equal("generated-json-export", imported.Manifest.PluginId);
        Assert.DoesNotContain("assemblyPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawDllBase64", json, StringComparison.OrdinalIgnoreCase);

        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var kernel = await server.InstallJsonAsync(json);
        var adapter = new DamageEventAdapter();
        var matching = new DamageEvent("player-1", "matched", "fire", 150);
        var rejected = matching with { Amount = 99 };

        Assert.False(await kernel.ShouldHandleAsync(adapter, rejected));
        Assert.True(await kernel.ShouldHandleAsync(adapter, matching));

        await kernel.HandleAsync(adapter, matching);

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("matched", message.Message);
    }

    private sealed record DamageEvent(string TargetId, string Message, string DamageType, int Amount);

    private sealed class DamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_TargetId", SandboxType.String),
            new("e_Message", SandboxType.String),
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.Message),
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount)
            ];
    }
}
