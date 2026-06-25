using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFacadeSurfaceEmitter
{
    public static void AppendProperties(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Returns this generated server as its complete service facade.");
        builder.Append("    public ").Append(model.ServerInterfaceName).AppendLine(" Services => this;");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Registry for server extension clients installed through setup, Extend, or EnsureAnonymousKernelAsync.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions => this;");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote hook registration surface. Hooks plug plugin logic into server decisions and are awaited by the server when matching events are published.");
        builder.Append("    public ").Append(model.HookRegistryName).AppendLine(" Hooks => _started && _hooks is not null ? _hooks : throw new global::System.InvalidOperationException(NotStartedMessage);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote fire-and-forget subscription registration surface. Subscriptions are notifications: the server calls matching handlers when an event is published but does not wait for them.");
        builder.Append("    public ").Append(model.SubscriptionRegistryName).AppendLine(" Subscriptions => _started && _subscriptions is not null ? _subscriptions : throw new global::System.InvalidOperationException(NotStartedMessage);");
        foreach (var control in model.Controls)
        {
            PluginServerXmlDocumentation.Append(builder, "    ", control.Documentation);
            builder.Append("    public ").Append(control.Type).Append(' ').Append(control.Name)
                .Append(" => _started && _").Append(FieldName(control.Name))
                .AppendLine(" is not null ? _" + FieldName(control.Name) + " : throw new global::System.InvalidOperationException(NotStartedMessage);");
        }

        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Wire client used by generated server extension clients to invoke installed server-side extension kernels.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.IServerExtensionWireClient WireClient => this;");
    }

    public static void AppendServerInterface(StringBuilder builder, PluginServerFacadeModel model)
    {
        PluginServerXmlDocumentation.Append(builder, string.Empty, model.WorldDocumentation);
        builder.Append(model.Accessibility).Append(" interface ").Append(model.ServerInterfaceName)
            .Append(" : ").Append(model.WorldType)
            .Append(", global::DotBoxD.Abstractions.IPluginServer<").Append(model.WorldType)
            .Append(">, global::DotBoxD.Abstractions.IServerExtensionClientRegistry, ")
            .AppendLine("global::System.IDisposable, global::System.IAsyncDisposable");
        builder.AppendLine("{");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Returns this generated server as its complete service facade.");
        builder.Append("    ").Append(model.ServerInterfaceName).AppendLine(" Services { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Registry for server extension clients installed through setup, Extend, or EnsureAnonymousKernelAsync.");
        builder.AppendLine("    global::DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote hook registration surface. Hooks plug plugin logic into server decisions and are awaited by the server when matching events are published.");
        builder.Append("    ").Append(model.HookRegistryName).AppendLine(" Hooks { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote fire-and-forget subscription registration surface. Subscriptions are notifications: the server calls matching handlers when an event is published but does not wait for them.");
        builder.Append("    ").Append(model.SubscriptionRegistryName).AppendLine(" Subscriptions { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Wire client used by generated server extension clients to invoke installed server-side extension kernels.");
        builder.AppendLine("    global::DotBoxD.Abstractions.IServerExtensionWireClient WireClient { get; }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a live-settings handle for an installed kernel so the plugin can batch strongly typed setting updates.");
        builder.AppendLine("    global::DotBoxD.Abstractions.ILiveSettingsHandle<TKernel> Get<TKernel>() where TKernel : class, new();");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs the package produced by the factory at most once and returns the installed plugin id.");
        builder.AppendLine("    global::System.Threading.Tasks.Task<string> EnsureAnonymousKernelAsync(string pluginId, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory);");
        builder.AppendLine("}");
    }

    public static void AppendInstallSurface(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs and invokes a one-off server-side probe. Calls must be intercepted by the DotBoxD plugin generator.");
        builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<TReturn> InvokeAsync<TReturn>(global::System.Func<" + model.WorldType + ", global::System.Threading.Tasks.ValueTask<TReturn>> lambda)");
        builder.AppendLine("        => throw new global::System.InvalidOperationException(\"Plugin server InvokeAsync calls must be intercepted by the DotBoxD plugin generator.\");");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs and invokes a one-off server-side probe with an explicit capture bag. Calls must be intercepted by the DotBoxD plugin generator.");
        builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(TCaptures captures, global::DotBoxD.Abstractions.RemoteServerInvocation<" + model.WorldType + ", TCaptures, TReturn> lambda) where TCaptures : class");
        builder.AppendLine("        => throw new global::System.InvalidOperationException(\"Plugin server InvokeAsync calls must be intercepted by the DotBoxD plugin generator.\");");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a live-settings handle for an installed kernel so the plugin can batch strongly typed setting updates.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.ILiveSettingsHandle<TKernel> Get<TKernel>() where TKernel : class, new()");
        builder.AppendLine("        => new LiveSettingsHandle<TKernel>(this, global::DotBoxD.Plugins.Kernel.KernelPackageRegistry.Resolve<TKernel>().Manifest.PluginId);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Returns the installed plugin id for a server extension service type.");
        builder.AppendLine("    public string PluginId<TService>() where TService : class");
        builder.AppendLine("        => _serverExtensions.TryGetValue(typeof(TService), out var pluginId) ? pluginId : throw new global::System.InvalidOperationException($\"Server extension '{typeof(TService).FullName}' has not been registered.\");");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Invokes an installed server extension kernel through the generated control-plane wire client.");
        builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        => RequireControl().InvokeServerExtensionAsync(pluginId, arguments, cancellationToken);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs the package produced by the factory at most once and returns the installed plugin id.");
        builder.AppendLine("    public global::System.Threading.Tasks.Task<string> EnsureAnonymousKernelAsync(string pluginId, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory)");
        builder.AppendLine("    {");
        builder.AppendLine("        var install = _anonymousKernels.GetOrAdd(pluginId, id => new global::System.Lazy<global::System.Threading.Tasks.Task<string>>(() => InstallServerExtensionPackageAsync(factory()).AsTask()));");
        builder.AppendLine("        return AwaitAnonymousKernelAsync(pluginId, install);");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.Task<string> AwaitAnonymousKernelAsync(string pluginId, global::System.Lazy<global::System.Threading.Tasks.Task<string>> install)");
        builder.AppendLine("    {");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            return await install.Value.ConfigureAwait(false);");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            ((global::System.Collections.Generic.ICollection<global::System.Collections.Generic.KeyValuePair<string, global::System.Lazy<global::System.Threading.Tasks.Task<string>>>>)_anonymousKernels).Remove(new global::System.Collections.Generic.KeyValuePair<string, global::System.Lazy<global::System.Threading.Tasks.Task<string>>>(pluginId, install));");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<string> InstallPluginPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        => RequireControl().InstallPluginAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken);");
        builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<string> InstallSubscriptionPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        => RequireControl().InstallSubscriptionAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken);");
        builder.AppendLine("    private global::System.Threading.Tasks.ValueTask<string> InstallServerExtensionPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        => RequireControl().InstallServerExtensionAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken);");
    }

    private static string FieldName(string propertyName)
        => char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
}
