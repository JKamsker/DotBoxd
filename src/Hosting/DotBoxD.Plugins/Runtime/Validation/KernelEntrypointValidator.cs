using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Input;

namespace DotBoxD.Plugins.Runtime;

internal static class KernelEntrypointValidator
{
    public static void Validate<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        IPluginEventAdapter<TEvent> adapter)
    {
        var eventName = adapter.EventName;
        var parameters = adapter.Parameters;
        PluginEventAdapterShapeValidator.Validate(adapter, eventName, parameters);
        Validate<TEvent>(manifest, plan, entrypoints, new PluginEventShape(eventName, parameters));
    }

    public static void Validate<TEvent>(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        PluginEventShape adapterShape)
        => Validate(manifest, plan, entrypoints, adapterShape, EventAliases<TEvent>(adapterShape.EventName));

    public static void Validate(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        PluginEventShape adapterShape)
        => Validate(manifest, plan, entrypoints, adapterShape, [adapterShape.EventName]);

    private static void Validate(
        PluginManifest manifest,
        ExecutionPlan plan,
        KernelEntrypoints entrypoints,
        PluginEventShape adapterShape,
        IReadOnlyList<string> eventAliases)
    {
        if (!manifest.Subscriptions.Any(s => MatchesAnyAlias(s.Event, eventAliases)))
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK031", $"Plugin '{manifest.PluginId}' is not subscribed to event '{adapterShape.EventName}'.")
            ]);
        }

        var expected = PluginParameterShape.BuildExpected(adapterShape.Parameters, manifest.LiveSettings);
        ValidateFunction(plan, entrypoints.ShouldHandle, SandboxType.Bool, requireNonUnit: false, expected);

        // Handle return contract by chain shape:
        //  - ordinary chains and result RegisterLocal return Unit.
        //  - RunLocal and sandbox result Register return a non-Unit value.
        //    Projection exact type is enforced by the push decoder; result exact type is enforced when FireAsync
        //    decodes to the hook's declared TResult.
        var handleReturnsValue = ReturnsValue(manifest);
        ValidateFunction(
            plan,
            entrypoints.Handle,
            handleReturnsValue ? null : SandboxType.Unit,
            requireNonUnit: handleReturnsValue,
            expected);
    }

    private static string[] EventAliases<TEvent>(string adapterEventName)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal) { adapterEventName };
        var typeName = typeof(TEvent).Name;
        var fullName = typeof(TEvent).FullName;
        var hook = Attribute.GetCustomAttribute(typeof(TEvent), typeof(HookAttribute), inherit: false) as HookAttribute;
        var isConventionName =
            EventNameMatch.Matches(adapterEventName, typeName) ||
            (fullName is not null && EventNameMatch.Matches(adapterEventName, fullName)) ||
            (hook is not null && string.Equals(adapterEventName, hook.Name, StringComparison.Ordinal));

        if (!isConventionName)
        {
            return [.. aliases];
        }

        aliases.Add(typeName);
        if (fullName is not null)
        {
            aliases.Add(fullName);
        }

        if (hook is not null)
        {
            aliases.Add(hook.Name);
        }

        return [.. aliases];
    }

    private static bool MatchesAnyAlias(string? actual, IReadOnlyList<string> eventAliases)
    {
        foreach (var alias in eventAliases)
        {
            if (EventNameMatch.Matches(actual, alias))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReturnsValue(PluginManifest manifest)
    {
        foreach (var subscription in manifest.Subscriptions)
        {
            if ((subscription.LocalTerminal && subscription.ProjectedType is not null) ||
                (subscription.ResultType is not null && !subscription.ResultLocalTerminal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateFunction(
        ExecutionPlan plan,
        string functionId,
        SandboxType? exactReturnType,
        bool requireNonUnit,
        IReadOnlyList<Parameter> expected)
    {
        var function = plan.Module.Functions.FirstOrDefault(f => string.Equals(f.Id, functionId, StringComparison.Ordinal));
        if (function is null || !function.IsEntrypoint)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic("DBXK032", $"Kernel entrypoint '{functionId}' is missing or not public.")
            ]);
        }

        var returnTypeOk = (exactReturnType is null || function.ReturnType == exactReturnType)
            && (!requireNonUnit || function.ReturnType != SandboxType.Unit);
        if (!returnTypeOk || !PluginParameterShape.Matches(function.Parameters, expected))
        {
            throw SignatureError(functionId);
        }
    }

    private static SandboxValidationException SignatureError(string functionId)
        => new([new SandboxDiagnostic("DBXK033", $"Kernel entrypoint '{functionId}' does not match the hook event and live settings.")]);
}
