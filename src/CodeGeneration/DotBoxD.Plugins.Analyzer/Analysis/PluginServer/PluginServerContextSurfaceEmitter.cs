using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerContextSurfaceEmitter
{
    public static void AppendContext(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            string.Empty,
            "Generated server hook context. Extend this partial type with helper members; [KernelMethod] instance members can be consumed by lowered hook chains.");
        builder.Append(model.ContextAccessibility).Append(" partial class ").Append(model.ContextName).AppendLine();
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
        builder.Append("    public ").Append(model.WorldType).Append(" World => _raw.Host<")
            .Append(model.WorldType).AppendLine(">();");
        builder.AppendLine("    public global::DotBoxD.Abstractions.IPluginMessageSink Messages => _raw.Messages;");
        builder.AppendLine("    public global::System.Threading.CancellationToken CancellationToken => _raw.CancellationToken;");
        builder.AppendLine("    public bool HasCancelableDispatch => _raw.CancellationToken.CanBeCanceled;");
        builder.AppendLine();
        builder.Append("    public static ").Append(model.ContextName)
            .Append(" FromHookContext(global::DotBoxD.Abstractions.HookContext raw) => ")
            .Append(model.ContextFactoryMethodName ?? "new").AppendLine("(raw);");
        builder.AppendLine();
        builder.AppendLine("    partial void OnCreated(global::DotBoxD.Abstractions.HookContext raw);");
        builder.AppendLine("}");
    }

    public static void AppendRegistries(StringBuilder builder, PluginServerFacadeModel model)
    {
        AppendHookRegistry(builder, model);
        builder.AppendLine();
        AppendSubscriptionRegistry(builder, model);
    }

    private static void AppendHookRegistry(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            string.Empty,
            "Generated hook registry whose parameterless On<TEvent>() uses the generated server context by default.");
        AppendRegistryAttribute(builder, model, "Hook");
        builder.Append(model.Accessibility).Append(" sealed class ").Append(model.HookRegistryName).AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::DotBoxD.Plugins.Runtime.RemoteHookRegistry _inner;");
        builder.AppendLine();
        AppendRegistryConstructor(builder, model.HookRegistryName, "RemoteHookRegistry");
        builder.AppendLine();
        builder.Append("    public global::DotBoxD.Plugins.Runtime.RemoteHookPipeline<TEvent, ")
            .Append(model.ContextFullName).AppendLine("> On<TEvent>()");
        builder.Append("        => _inner.On<TEvent, ").Append(model.ContextFullName)
            .Append(">(").Append(model.ContextFullName).AppendLine(".FromHookContext);");
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
            "Generated subscription registry whose parameterless On<TEvent>() uses the generated server context by default.");
        AppendRegistryAttribute(builder, model, "Subscription");
        builder.Append(model.Accessibility).Append(" sealed class ").Append(model.SubscriptionRegistryName).AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::DotBoxD.Plugins.Runtime.RemoteSubscriptionRegistry _inner;");
        builder.AppendLine();
        AppendRegistryConstructor(builder, model.SubscriptionRegistryName, "RemoteSubscriptionRegistry");
        builder.AppendLine();
        builder.Append("    public global::DotBoxD.Plugins.Runtime.RemoteSubscriptionPipeline<TEvent, ")
            .Append(model.ContextFullName).AppendLine("> On<TEvent>()");
        builder.Append("        => _inner.On<TEvent, ").Append(model.ContextFullName)
            .Append(">(").Append(model.ContextFullName).AppendLine(".FromHookContext);");
        builder.AppendLine();
        builder.AppendLine("    public global::DotBoxD.Plugins.Runtime.RemoteSubscriptionPipeline<TEvent, TContext> On<TEvent, TContext>(");
        builder.AppendLine("        global::System.Func<global::DotBoxD.Abstractions.HookContext, TContext> createContext)");
        builder.AppendLine("        => _inner.On<TEvent, TContext>(createContext);");
        builder.AppendLine("}");
    }

    private static void AppendRegistryAttribute(StringBuilder builder, PluginServerFacadeModel model, string kind)
    {
        builder.Append("[global::DotBoxD.Abstractions.GeneratedPluginServerRegistry(")
            .Append("global::DotBoxD.Abstractions.GeneratedPluginServerRegistryKind.")
            .Append(kind)
            .Append(", typeof(")
            .Append(TypeReference(model, model.ClassName))
            .Append("), typeof(")
            .Append(model.ContextFullName)
            .AppendLine("))]");
    }

    private static string TypeReference(PluginServerFacadeModel model, string typeName)
        => string.IsNullOrEmpty(model.Namespace)
            ? "global::" + typeName
            : "global::" + model.Namespace + "." + typeName;

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
