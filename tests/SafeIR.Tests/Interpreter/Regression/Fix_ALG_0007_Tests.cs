using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for ALG-0007: plugin package validation must resolve the two fixed kernel
/// entrypoints (ShouldHandle, Handle) by id without rescanning the full module function list once
/// per entrypoint per validation pass. The fix indexes entrypoint functions once
/// (<c>PluginEntrypointIndex</c>) and resolves both the basic and prepared validators from that
/// index. These tests pin the observable behavior the optimization must preserve: entrypoint
/// resolution depends only on the (id, IsEntrypoint) function identity, not on how many unrelated
/// helper functions the module carries.
/// </summary>
public sealed class Fix_ALG_0007_Tests
{
    [Fact]
    public async Task Install_succeeds_with_many_non_entrypoint_helper_functions()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var crowded = package with
        {
            Module = package.Module with
            {
                Functions = WithHelperFunctions(package.Module.Functions, helperCount: 256)
            }
        };

        // The two real entrypoints must still resolve out of a large function list, so install
        // succeeds exactly as it does for the two-function package.
        var kernel = await server.InstallAsync(crowded);

        Assert.NotNull(kernel);
    }

    [Fact]
    public async Task Install_rejects_entrypoint_id_that_is_only_a_non_entrypoint_function()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();

        // Demote ShouldHandle to a non-entrypoint (non-public) function while keeping its id. The
        // entrypoint lookup must still treat it as missing because only IsEntrypoint functions are
        // indexed, so install is rejected with the missing-entrypoint diagnostic.
        var functions = package.Module.Functions
            .Select(f => f.Id == package.Entrypoints.ShouldHandle
                ? f with { IsEntrypoint = false }
                : f)
            .ToArray();
        var invalid = package with { Module = package.Module with { Functions = functions } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP032");
    }

    private static SandboxFunction[] WithHelperFunctions(
        IReadOnlyList<SandboxFunction> functions,
        int helperCount)
    {
        // Clone an already-valid entrypoint body into many non-entrypoint helper functions with
        // distinct ids so the module's function count dwarfs the two entrypoints under test.
        var template = functions.Single(f => f.IsEntrypoint && f.ReturnType == SandboxType.Unit);
        var result = new List<SandboxFunction>(functions);
        for (var i = 0; i < helperCount; i++)
        {
            result.Add(template with { Id = $"Helper_{i}", IsEntrypoint = false });
        }

        return result.ToArray();
    }
}
