using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerContextSurfaceEmitter
{
    public static void AppendContextAndRegistries(StringBuilder builder, PluginServerFacadeModel model)
    {
        AppendContext(builder, model);
        builder.AppendLine();
        AppendHookRegistry(builder, model);
        builder.AppendLine();
        AppendSubscriptionRegistry(builder, model);
    }

    private static void AppendContext(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            string.Empty,
            "Generated plugin-owned hook context. Extend this partial type with plugin-specific members; [KernelMethod] and [HostBinding] members can be consumed by lowered hook chains.");
        builder.Append(model.Accessibility).Append(" sealed partial class ").Append(model.ContextName).AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::DotBoxD.Abstractions.HookContext _raw;");
        builder.AppendLine();
        builder.Append("    public ").Append(model.ContextName).AppendLine("(global::DotBoxD.Abstractions.HookContext raw)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(raw);");
        builder.AppendLine("        _raw = raw;");
        builder.AppendLine("        OnCreated(raw);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public global::DotBoxD.Abstractions.HookContext Raw => _raw;");
        builder.AppendLine("    public global::DotBoxD.Abstractions.IPluginMessageSink Messages => _raw.Messages;");
        builder.AppendLine("    public global::System.Threading.CancellationToken CancellationToken => _raw.CancellationToken;");
        builder.AppendLine("    public bool HasCancelableDispatch => _raw.CancellationToken.CanBeCanceled;");
        builder.AppendLine();
        builder.Append("    public static ").Append(model.ContextName)
            .AppendLine(" FromHookContext(global::DotBoxD.Abstractions.HookContext raw) => new(raw);");
        builder.AppendLine();
        builder.AppendLine("    partial void OnCreated(global::DotBoxD.Abstractions.HookContext raw);");
        builder.AppendLine("}");
    }

    private static void AppendHookRegistry(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            string.Empty,
            "Generated hook registry whose parameterless On<TEvent>() uses the plugin-owned server context by default.");
        builder.Append(model.Accessibility).Append(" sealed class ").Append(model.HookRegistryName).AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::DotBoxD.Plugins.Runtime.RemoteHookRegistry _inner;");
        builder.AppendLine();
        AppendRegistryConstructor(builder, model.HookRegistryName, "RemoteHookRegistry");
        builder.AppendLine();
        builder.Append("    public global::DotBoxD.Plugins.Runtime.RemoteHookPipeline<TEvent, ")
            .Append(model.ContextName).AppendLine("> On<TEvent>()");
        builder.Append("        => _inner.On<TEvent, ").Append(model.ContextName)
            .Append(">(").Append(model.ContextName).AppendLine(".FromHookContext);");
        builder.AppendLine();
        builder.AppendLine("    public global::DotBoxD.Plugins.Runtime.RemoteHookPipeline<TEvent, TContext> On<TEvent, TContext>(");
        builder.AppendLine("        global::System.Func<global::DotBoxD.Abstractions.HookContext, TContext> createContext)");
        builder.AppendLine("        => _inner.On<TEvent, TContext>(createContext);");
        builder.AppendLine("}");
    }

    private static void AppendSubscriptionRegistry(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            string.Empty,
            "Generated subscription registry whose parameterless On<TEvent>() uses the plugin-owned server context by default.");
        builder.Append(model.Accessibility).Append(" sealed class ").Append(model.SubscriptionRegistryName).AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::DotBoxD.Plugins.Runtime.RemoteSubscriptionRegistry _inner;");
        builder.AppendLine();
        AppendRegistryConstructor(builder, model.SubscriptionRegistryName, "RemoteSubscriptionRegistry");
        builder.AppendLine();
        builder.Append("    public global::DotBoxD.Plugins.Runtime.RemoteSubscriptionPipeline<TEvent, ")
            .Append(model.ContextName).AppendLine("> On<TEvent>()");
        builder.Append("        => _inner.On<TEvent, ").Append(model.ContextName)
            .Append(">(").Append(model.ContextName).AppendLine(".FromHookContext);");
        builder.AppendLine();
        builder.AppendLine("    public global::DotBoxD.Plugins.Runtime.RemoteSubscriptionPipeline<TEvent, TContext> On<TEvent, TContext>(");
        builder.AppendLine("        global::System.Func<global::DotBoxD.Abstractions.HookContext, TContext> createContext)");
        builder.AppendLine("        => _inner.On<TEvent, TContext>(createContext);");
        builder.AppendLine("}");
    }

    private static void AppendRegistryConstructor(
        StringBuilder builder,
        string registryName,
        string runtimeRegistryName)
    {
        builder.Append("    public ").Append(registryName).AppendLine("(");
        builder.AppendLine("        global::System.Func<global::DotBoxD.Plugins.PluginPackage, global::System.Threading.Tasks.ValueTask<string>> install,");
        builder.AppendLine("        global::DotBoxD.Plugins.Runtime.Hooks.RemoteLocalHandlerRegistry? localHandlers = null)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(install);");
        builder.Append("        _inner = new global::DotBoxD.Plugins.Runtime.").Append(runtimeRegistryName)
            .AppendLine("(install, localHandlers);");
        builder.AppendLine("    }");
    }
}
