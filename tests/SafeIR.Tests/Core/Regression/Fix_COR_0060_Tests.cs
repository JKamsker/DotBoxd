using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for COR-0060: live-setting numeric validation must reject fractional
/// input for int/long settings instead of silently rounding it, and must compare long ranges
/// with exact integer arithmetic instead of collapsing 64-bit boundaries through <see cref="double"/>.
/// </summary>
public sealed class Fix_COR_0060_Tests
{
    // 2^53 and 2^53 + 1 are the smallest pair that double cannot distinguish.
    private const long PowerOfTwo53 = 9_007_199_254_740_992L;
    private const long PowerOfTwo53Plus1 = 9_007_199_254_740_993L;

    [Fact]
    public void Setting_int_rejects_fractional_double_without_rounding_or_mutating_value()
    {
        var store = new LiveSettingStore([new LiveValue<int>("MinDamage", 100)]);

        var ex = Assert.Throws<SandboxValidationException>(
            () => store.SetObject("MinDamage", 1.5));

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP020");
        Assert.Equal(100, store.Get<int>("MinDamage"));
    }

    [Fact]
    public void Setting_long_rejects_fractional_double_without_rounding_or_mutating_value()
    {
        var store = new LiveSettingStore([new LiveValue<long>("Threshold", 10L)]);

        var ex = Assert.Throws<SandboxValidationException>(
            () => store.SetObject("Threshold", 2.5));

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP020");
        Assert.Equal(10L, store.Get<long>("Threshold"));
    }

    [Fact]
    public void Setting_int_rejects_whole_double_outside_int_range()
    {
        var store = new LiveSettingStore([new LiveValue<int>("Count", 0)]);

        var ex = Assert.Throws<SandboxValidationException>(
            () => store.SetObject("Count", (double)int.MaxValue + 1));

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP020");
        Assert.Equal(0, store.Get<int>("Count"));
    }

    [Fact]
    public void Setting_long_round_trips_large_exact_value_without_precision_loss()
    {
        var store = new LiveSettingStore([new LiveValue<long>("Big", 0L)]);

        store.SetObject("Big", PowerOfTwo53Plus1);

        Assert.Equal(PowerOfTwo53Plus1, store.Get<long>("Big"));
    }

    [Fact]
    public void Import_detects_inverted_long_range_across_double_indistinguishable_boundary()
    {
        // min = 2^53 + 1, max = 2^53. These are distinct as Int64 but equal once narrowed to
        // double, so the old double-based comparison failed to flag min > max.
        var json = LongRangePackage(min: PowerOfTwo53Plus1, max: PowerOfTwo53);

        var ex = Assert.Throws<SandboxValidationException>(
            () => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP024");
    }

    [Fact]
    public void Import_accepts_valid_long_range_across_double_indistinguishable_boundary()
    {
        var json = LongRangePackage(min: PowerOfTwo53, max: PowerOfTwo53Plus1);

        var package = PluginPackageJsonSerializer.Import(json);

        var setting = Assert.Single(package.Manifest.LiveSettings);
        Assert.IsType<long>(setting.Min);
        Assert.IsType<long>(setting.Max);
        Assert.Equal(PowerOfTwo53, setting.Min);
        Assert.Equal(PowerOfTwo53Plus1, setting.Max);
    }

    private static string LongRangePackage(long min, long max)
        => $$"""
        {
          "manifest": {
            "pluginId": "json-fire-damage",
            "contract": "IEventKernel<DamageEvent>",
            "mode": "Interpreted",
            "effects": ["Cpu", "Alloc", "HostStateWrite", "Audit"],
            "liveSettings": [
              { "name": "Threshold", "type": "long", "defaultValue": {{min}}, "min": {{min}}, "max": {{max}} }
            ],
            "subscriptions": [
              { "event": "DamageEvent", "kernel": "JsonDamageKernel" }
            ]
          },
          "module": {
            "id": "json-fire-damage",
            "version": "1.0.0",
            "targetSandboxVersion": "1.0.0",
            "capabilityRequests": [
              { "id": "host.message.write", "reason": "send host messages" }
            ],
            "metadata": { "pluginId": "json-fire-damage", "kernel": "JsonDamageKernel" },
            "functions": [
              {
                "id": "ShouldHandle",
                "visibility": "entrypoint",
                "parameters": [
                  { "name": "e_DamageType", "type": "String" },
                  { "name": "e_Amount", "type": "I32" },
                  { "name": "e_TargetId", "type": "String" }
                ],
                "returnType": "Bool",
                "body": [
                  { "op": "return", "value": { "bool": true } }
                ]
              },
              {
                "id": "Handle",
                "visibility": "entrypoint",
                "parameters": [
                  { "name": "e_DamageType", "type": "String" },
                  { "name": "e_Amount", "type": "I32" },
                  { "name": "e_TargetId", "type": "String" }
                ],
                "returnType": "Unit",
                "body": [
                  {
                    "op": "return",
                    "value": {
                      "call": "host.message.send",
                      "args": [
                        { "var": "e_TargetId" },
                        { "string": "json package handled damage" }
                      ]
                    }
                  }
                ]
              }
            ]
          }
        }
        """;
}
