using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginLiveSettingValidationTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Live_setting_update_rejects_non_finite_double_without_changing_value(double value)
    {
        var store = new LiveSettingStore([new LiveValue<double>("Multiplier", 1D)]);

        var ex = Assert.Throws<SandboxValidationException>(
            () => store.Set("Multiplier", value));

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP020");
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

        Assert.Contains(intError.Diagnostics, d => d.Code == "SGP020");
        Assert.Contains(boolError.Diagnostics, d => d.Code == "SGP020");
        Assert.Equal(100, store.Get<int>("MinDamage"));
        Assert.True(store.Get<bool>("Enabled"));
    }
}
