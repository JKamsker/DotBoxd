namespace DotBoxD.Plugins;

using System.Globalization;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Runtime;

public static class PluginMessageBindings
{
    public const string SendBindingId = "host.message.send";
    public const string CapabilityId = "host.message.write";

    private static readonly string[] AllowedGrantKeys =
        ["allowedTargets", "targetPrefixes", "maxMessageLength"];

    private static readonly ConditionalWeakTable<CapabilityGrant, MessageGrantOptions> OptionsCache = new();

    public static SandboxHostBuilder AddPluginMessageBindings(
        this SandboxHostBuilder builder,
        IPluginMessageSink sink)
    {
        builder.AddBinding(CreateSend(sink));
        return builder;
    }

    public static BindingDescriptor CreateSend(IPluginMessageSink sink)
    {
        var invoker = new PluginMessageSendInvoker(sink);
        return new BindingDescriptor(
            SendBindingId,
            SemVersion.One,
            [SandboxType.String, SandboxType.String],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            CapabilityId,
            new BindingCostModel(5, MaxCallsPerRun: 100),
            AuditLevel.PerResource,
            BindingSafety.SideEffectingExternal,
            invoker.Invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            ValidateGrant);
    }

    private static void ValidateGrant(CapabilityGrant grant, ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            if (Array.IndexOf(AllowedGrantKeys, key) < 0)
            {
                Add(diagnostics, grant, $"parameter '{key}' is not supported");
            }
        }

        ValidateTargetList(grant, diagnostics, "allowedTargets");
        ValidateTargetList(grant, diagnostics, "targetPrefixes");
        ValidateMaxMessageLength(grant, diagnostics);
    }

    private static void ValidateTargetList(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return;
        }

        var values = value.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Any(string.IsNullOrEmpty))
        {
            Add(diagnostics, grant, $"parameter '{key}' must not contain empty values");
            return;
        }

        if (values.Any(item => !SandboxLiteralConstraints.IsOpaqueId(item)))
        {
            Add(diagnostics, grant, $"parameter '{key}' must contain only opaque target IDs");
        }
    }

    private static void ValidateMaxMessageLength(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics)
    {
        if (grant.Parameters.TryGetValue("maxMessageLength", out var value) &&
            (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0))
        {
            Add(diagnostics, grant, "parameter 'maxMessageLength' must be a non-negative integer");
        }
    }

    private static void Add(ICollection<SandboxDiagnostic> diagnostics, CapabilityGrant grant, string message)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-POLICY-GRANT-PARAM",
            $"grant '{grant.Id}' {message}"));

    private static MessageGrantOptions ReadGrantOptions(CapabilityGrant grant)
        => OptionsCache.GetValue(grant, CreateGrantOptions);

    private static MessageGrantOptions CreateGrantOptions(CapabilityGrant grant)
        => new(
            ReadTargetSet(grant, "allowedTargets"),
            ReadTargetList(grant, "targetPrefixes"),
            ReadMaxMessageLength(grant));

    private static IReadOnlySet<string>? ReadTargetSet(CapabilityGrant grant, string key)
        => grant.Parameters.TryGetValue(key, out var value)
            ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal)
            : null;

    private static IReadOnlyList<string>? ReadTargetList(CapabilityGrant grant, string key)
        => grant.Parameters.TryGetValue(key, out var value)
            ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : null;

    private static int? ReadMaxMessageLength(CapabilityGrant grant)
    {
        if (!grant.Parameters.TryGetValue("maxMessageLength", out var value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.PermissionDenied,
                "host.message.send denied: maxMessageLength grant is invalid"));
        }

        return parsed;
    }

    private static string Sanitize(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                return SanitizeWithControlCharacters(value);
            }
        }

        return value;
    }

    private static string SanitizeWithControlCharacters(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    private static string SanitizeResourceTargetId(string targetId)
    {
        var sanitized = AuditTextSanitizer.SanitizeAndRedact(targetId);
        return string.Equals(sanitized, targetId, StringComparison.Ordinal)
            ? targetId
            : "[redacted]";
    }

    private static void WriteAudit(SandboxContext context, string targetId, string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var fields = BindingAuditFields.CreateMutable(
            "plugin-message",
            timestamp,
            context.ModuleHash,
            context.PolicyHash,
            context.Policy.Deterministic,
            extraCapacity: 1);
        fields["messageLength"] = message.Length.ToString(CultureInfo.InvariantCulture);
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "PluginMessage",
            timestamp,
            true,
            BindingId: SendBindingId,
            CapabilityId: CapabilityId,
            Effect: SandboxEffect.HostStateWrite,
            ResourceId: $"target:{SanitizeResourceTargetId(targetId)}",
            Message: AuditTextSanitizer.SanitizeAndRedact(message),
            Fields: fields));
    }

    private sealed record MessageGrantOptions(
        IReadOnlySet<string>? AllowedTargets,
        IReadOnlyList<string>? TargetPrefixes,
        int? MaxMessageLength)
    {
        public bool AllowsTarget(string targetId)
        {
            // No recipient scoping configured: the grant is intentionally unrestricted.
            if (AllowedTargets is null && TargetPrefixes is null)
            {
                return true;
            }

            if (AllowedTargets is not null && AllowedTargets.Contains(targetId))
            {
                return true;
            }

            if (TargetPrefixes is not null)
            {
                for (var i = 0; i < TargetPrefixes.Count; i++)
                {
                    if (targetId.StartsWith(TargetPrefixes[i], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    private sealed class PluginMessageSendInvoker(IPluginMessageSink sink) : ITwoArgumentBindingInvoker
    {
        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
            => Invoke(context, args[0], args[1], cancellationToken);

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            CancellationToken cancellationToken)
        {
            var targetId = ((StringValue)arg0).Value;
            if (!SandboxLiteralConstraints.IsOpaqueId(targetId))
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.InvalidInput,
                    "host.message.send denied: target ID is invalid"));
            }

            var options = ReadGrantOptions(context.GetCapability(CapabilityId));
            if (!options.AllowsTarget(targetId))
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.PermissionDenied,
                    "host.message.send denied: target is not in the granted recipient set"));
            }

            var message = Sanitize(((StringValue)arg1).Value);
            if (options.MaxMessageLength is { } maxMessageLength && message.Length > maxMessageLength)
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.QuotaExceeded,
                    "host.message.send denied: message exceeds the granted length limit"));
            }

            var send = sink.SendAsync(targetId, message, cancellationToken);
            if (!send.IsCompletedSuccessfully)
            {
                return AwaitSendAsync(send, context, targetId, message);
            }

            send.GetAwaiter().GetResult();
            WriteAudit(context, targetId, message);
            return ValueTask.FromResult(SandboxValue.Unit);
        }

        private static async ValueTask<SandboxValue> AwaitSendAsync(
            ValueTask send,
            SandboxContext context,
            string targetId,
            string message)
        {
            await send.ConfigureAwait(false);
            WriteAudit(context, targetId, message);
            return SandboxValue.Unit;
        }
    }
}
