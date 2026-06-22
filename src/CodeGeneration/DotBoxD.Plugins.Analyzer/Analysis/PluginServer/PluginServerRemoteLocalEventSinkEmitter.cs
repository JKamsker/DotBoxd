using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerRemoteLocalEventSinkEmitter
{
    public static void Append(StringBuilder builder, PluginServerFacadeModel model)
    {
        if (model.EventCallbackType is null)
        {
            return;
        }

        builder.Append("    private sealed class RemoteLocalEventSink : ").AppendLine(model.EventCallbackType);
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::DotBoxD.Plugins.Runtime.Hooks.RemoteLocalHandlerRegistry _localHandlers;");
        builder.AppendLine("        public RemoteLocalEventSink(global::DotBoxD.Plugins.Runtime.Hooks.RemoteLocalHandlerRegistry localHandlers) => _localHandlers = localHandlers;");
        AppendOnEventAsync(builder, model);
        AppendOnResultAsync(builder);
        AppendDispatchAsync(builder);
        builder.AppendLine("    }");
    }

    // Generated bridge implementing the discovered reverse event-callback contract. Each server push decodes
    // through the local-handler registry into the native RunLocal delegate. A throwaway message sink is
    // supplied because a RunLocal terminal performs no host send.
    private static void AppendOnEventAsync(StringBuilder builder, PluginServerFacadeModel model)
    {
        if (!model.EventCallbackReturnHasValue)
        {
            builder.Append("        public ").Append(model.EventCallbackReturnType).AppendLine(" OnEventAsync(string subscriptionId, global::System.ReadOnlyMemory<byte> projectedValue, global::System.Threading.CancellationToken ct = default)");
            builder.AppendLine("            => DispatchAsync(subscriptionId, projectedValue, ct);");
            return;
        }

        builder.Append("        public async ").Append(model.EventCallbackReturnType).AppendLine(" OnEventAsync(string subscriptionId, global::System.ReadOnlyMemory<byte> projectedValue, global::System.Threading.CancellationToken ct = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            await DispatchAsync(subscriptionId, projectedValue, ct).ConfigureAwait(false);");
        builder.AppendLine("            return default!;");
        builder.AppendLine("        }");
    }

    private static void AppendOnResultAsync(StringBuilder builder)
    {
        builder.AppendLine("        public global::System.Threading.Tasks.ValueTask<byte[]> OnResultAsync(string subscriptionId, global::System.ReadOnlyMemory<byte> contextValue, global::System.Threading.CancellationToken ct = default)");
        builder.AppendLine("            => _localHandlers.DispatchResultAsync(subscriptionId, contextValue, new global::DotBoxD.Abstractions.HookContext(new global::DotBoxD.Abstractions.InMemoryPluginMessageSink(), ct), ct);");
    }

    private static void AppendDispatchAsync(StringBuilder builder)
    {
        builder.AppendLine("        private global::System.Threading.Tasks.ValueTask DispatchAsync(string subscriptionId, global::System.ReadOnlyMemory<byte> projectedValue, global::System.Threading.CancellationToken ct)");
        builder.AppendLine("            => _localHandlers.DispatchAsync(subscriptionId, projectedValue, new global::DotBoxD.Abstractions.HookContext(new global::DotBoxD.Abstractions.InMemoryPluginMessageSink(), ct), ct);");
    }
}
