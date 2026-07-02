using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class PluginLiveSettingValidationTests
{
    [Fact]
    public void Live_setting_bindings_reject_null_names_at_public_boundary()
    {
        using var server = PluginServer.Create();

        var boundValue = Assert.Throws<ArgumentNullException>(
            () => server.BindValue<int>(null!, 1));
        var boundContext = Assert.Throws<ArgumentNullException>(
            () => server.BindContext<IValidationSettings>(null!));
        var liveValue = Assert.Throws<ArgumentNullException>(
            () => new LiveValue<int>((string)null!, 1));

        Assert.Equal("name", boundValue.ParamName);
        Assert.Equal("name", boundContext.ParamName);
        Assert.Equal("name", liveValue.ParamName);
    }

    [Fact]
    public void Live_setting_store_rejects_null_entries_and_names_at_public_boundary()
    {
        var nullEntry = Assert.Throws<ArgumentNullException>(
            () => new LiveSettingStore([null!]));
        var nullName = Assert.Throws<ArgumentNullException>(
            () => new LiveSettingStore([new NullNamedSetting()]));

        Assert.Equal("settings", nullEntry.ParamName);
        Assert.Equal("name", nullName.ParamName);
    }

    [Fact]
    public void Live_setting_store_rejects_duplicate_names_with_public_parameter()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new LiveSettingStore([
                new LiveValue<int>("Damage", 1),
                new LiveValue<int>("Damage", 2)
            ]));

        Assert.Equal("settings", ex.ParamName);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Live_setting_update_rejects_non_finite_double_without_changing_value(double value)
    {
        var store = new LiveSettingStore([new LiveValue<double>("Multiplier", 1D)]);

        var ex = Assert.Throws<SandboxValidationException>(
            () => store.Set("Multiplier", value));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK020");
        Assert.Equal(1D, store.Get<double>("Multiplier"));
    }

    [Fact]
    public void Live_setting_update_rejects_invalid_scalar_text_with_diagnostics()
    {
        var store = new LiveSettingStore([
            new LiveValue<int>("MinDamage", 100),
            new LiveValue<bool>("Enabled", true)
        ]);

        var intError = Assert.Throws<SandboxValidationException>(
            () => store.SetObject("MinDamage", "not-int"));
        var boolError = Assert.Throws<SandboxValidationException>(
            () => store.SetObject("Enabled", "not-bool"));

        Assert.Contains(intError.Diagnostics, d => d.Code == "DBXK020");
        Assert.Contains(boolError.Diagnostics, d => d.Code == "DBXK020");
        Assert.Equal(100, store.Get<int>("MinDamage"));
        Assert.True(store.Get<bool>("Enabled"));
    }

    private interface IValidationSettings
    {
        int Damage { get; set; }
    }

    private sealed class NullNamedSetting : ILiveSetting
    {
        public string Name => null!;
        public LiveSettingDefinition Definition { get; } = new(null!, "int", 1);
        public object? CurrentValue => 1;
        public SandboxValue ToSandboxValue() => SandboxValue.FromInt32(1);
        public void SetObject(object? value) { }
    }
}
