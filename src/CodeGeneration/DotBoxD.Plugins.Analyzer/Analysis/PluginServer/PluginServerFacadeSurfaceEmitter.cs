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
        builder.Append("    public ").Append(model.ServerInterfaceName).AppendLine(" Services => RequireFacade();");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Registry for server extension clients installed through setup, Extend, or EnsureAnonymousKernelAsync.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions => RequireFacade();");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote hook registration surface. Hooks plug plugin logic into server decisions and are awaited by the server when matching events are published.");
        builder.Append("    public ").Append(model.HookRegistryName).AppendLine(" Hooks => RequireStarted(_hooks);");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Remote fire-and-forget subscription registration surface. Subscriptions are notifications: the server calls matching handlers when an event is published but does not wait for them.");
        builder.Append("    public ").Append(model.SubscriptionRegistryName).AppendLine(" Subscriptions => RequireStarted(_subscriptions);");
        foreach (var control in model.Controls)
        {
            PluginServerXmlDocumentation.Append(builder, "    ", control.Documentation);
            PluginServerFlowAttributeSource.Append(builder, "    ", control.Attributes);
            builder.Append("    public ").Append(control.Type).Append(' ')
                .Append(PluginServerIdentifier.Escape(control.Name))
                .Append(" => RequireStarted(").Append(control.FieldName).AppendLine(");");
        }

        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Wire client used by generated server extension clients to invoke installed server-side extension kernels.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.IServerExtensionWireClient WireClient => RequireFacade();");
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
            "Installs the package produced by the factory once, evicts failed attempts, and returns the installed plugin id.");
        builder.AppendLine("    global::System.Threading.Tasks.Task<string> EnsureAnonymousKernelAsync(string pluginId, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory, global::System.Threading.CancellationToken cancellationToken = default);");
        builder.AppendLine("}");
    }

    public static void AppendInstallSurface(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs and invokes a one-off server-side probe. Calls must be intercepted by the DotBoxD plugin generator.");
        builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<TReturn> InvokeAsync<TReturn>(global::System.Func<" + model.WorldType + ", global::System.Threading.Tasks.ValueTask<TReturn>> lambda, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        => throw new global::System.InvalidOperationException(\"Plugin server InvokeAsync calls must be intercepted by the DotBoxD plugin generator.\");");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Installs and invokes a one-off server-side probe with an explicit capture bag. Calls must be intercepted by the DotBoxD plugin generator.");
        builder.AppendLine("    public global::System.Threading.Tasks.ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(TCaptures captures, global::DotBoxD.Abstractions.RemoteServerInvocation<" + model.WorldType + ", TCaptures, TReturn> lambda, global::System.Threading.CancellationToken cancellationToken = default) where TCaptures : class");
        builder.AppendLine("        => throw new global::System.InvalidOperationException(\"Plugin server InvokeAsync calls must be intercepted by the DotBoxD plugin generator.\");");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Creates a live-settings handle for an installed kernel so the plugin can batch strongly typed setting updates.");
        builder.AppendLine("    public global::DotBoxD.Abstractions.ILiveSettingsHandle<TKernel> Get<TKernel>() where TKernel : class, new()");
        builder.AppendLine("    {");
        builder.AppendLine("        ThrowIfDisposed();");
        builder.AppendLine("        return new LiveSettingsHandle<TKernel>(this, global::DotBoxD.Plugins.Kernel.KernelPackageRegistry.Resolve<TKernel>().Manifest.PluginId);");
        builder.AppendLine("    }");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Returns the installed plugin id for a server extension service type.");
        builder.AppendLine("    public string PluginId<TService>() where TService : class");
        builder.AppendLine("    {");
        builder.AppendLine("        ThrowIfDisposed();");
        builder.AppendLine("        return _serverExtensions.TryGetValue(typeof(TService), out var pluginId) ? pluginId : throw new global::System.InvalidOperationException($\"Server extension '{typeof(TService).FullName}' has not been registered.\");");
        builder.AppendLine("    }");
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
        builder.AppendLine("    public async global::System.Threading.Tasks.Task<string> EnsureAnonymousKernelAsync(string pluginId, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        while (true)");
        builder.AppendLine("        {");
        builder.AppendLine("            var created = false;");
        builder.AppendLine("            if (!_anonymousKernels.TryGetValue(pluginId, out var install))");
        builder.AppendLine("            {");
        builder.AppendLine("                install = CreateAnonymousKernelInstall(pluginId, factory);");
        builder.AppendLine("                if (!_anonymousKernels.TryAdd(pluginId, install))");
        builder.AppendLine("                {");
        builder.AppendLine("                    continue;");
        builder.AppendLine("                }");
        builder.AppendLine("                created = true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                return await AwaitAnonymousKernelAsync(pluginId, install, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch when (!created)");
        builder.AppendLine("            {");
        builder.AppendLine("                continue;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private global::System.Lazy<global::System.Threading.Tasks.Task<string>> CreateAnonymousKernelInstall(string pluginId, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.Lazy<global::System.Threading.Tasks.Task<string>>? install = null;");
        builder.AppendLine("        install = new global::System.Lazy<global::System.Threading.Tasks.Task<string>>(() => InstallAnonymousKernelAsync(pluginId, install!, factory));");
        builder.AppendLine("        return install;");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.Task<string> InstallAnonymousKernelAsync(string pluginId, global::System.Lazy<global::System.Threading.Tasks.Task<string>> install, global::System.Func<global::DotBoxD.Plugins.PluginPackage> factory)");
        builder.AppendLine("    {");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            var installedId = await InstallServerExtensionPackageAsync(factory(), default).ConfigureAwait(false);");
        builder.AppendLine("            if (!global::System.StringComparer.Ordinal.Equals(installedId, pluginId))");
        builder.AppendLine("            {");
        builder.AppendLine("                RemoveAnonymousKernel(pluginId, install);");
        builder.AppendLine("                throw new global::System.InvalidOperationException($\"Anonymous kernel package id '{installedId}' did not match requested id '{pluginId}'.\");");
        builder.AppendLine("            }");
        builder.AppendLine("            return installedId;");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            RemoveAnonymousKernel(pluginId, install);");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.Task<string> AwaitAnonymousKernelAsync(string pluginId, global::System.Lazy<global::System.Threading.Tasks.Task<string>> install, global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.Threading.Tasks.Task<string>? installTask = null;");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            installTask = install.Value;");
        builder.AppendLine("            var installedId = await installTask.WaitAsync(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            return installedId;");
        builder.AppendLine("        }");
        builder.AppendLine("        catch (global::System.OperationCanceledException) when (cancellationToken.IsCancellationRequested && installTask is not null && !installTask.IsCompleted)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            RemoveAnonymousKernel(pluginId, install);");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    private bool RemoveAnonymousKernel(string pluginId, global::System.Lazy<global::System.Threading.Tasks.Task<string>> install)");
        builder.AppendLine("        => ((global::System.Collections.Generic.ICollection<global::System.Collections.Generic.KeyValuePair<string, global::System.Lazy<global::System.Threading.Tasks.Task<string>>>>)_anonymousKernels).Remove(new global::System.Collections.Generic.KeyValuePair<string, global::System.Lazy<global::System.Threading.Tasks.Task<string>>>(pluginId, install));");
        builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<string> InstallPluginPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        var pluginId = await RequireControl().InstallPluginAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        RequireInstalledPackageId(package, pluginId);");
        builder.AppendLine("        _installedPluginIds.Add(package.Manifest.PluginId);");
        builder.AppendLine("        return pluginId;");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<string> InstallSubscriptionPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        var pluginId = await RequireControl().InstallSubscriptionAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        RequireInstalledPackageId(package, pluginId);");
        builder.AppendLine("        _installedPluginIds.Add(package.Manifest.PluginId);");
        builder.AppendLine("        return pluginId;");
        builder.AppendLine("    }");
        builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask<string> InstallServerExtensionPackageAsync(global::DotBoxD.Plugins.PluginPackage package, global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        var pluginId = await RequireControl().InstallServerExtensionAsync(global::DotBoxD.Plugins.Json.PluginPackageJsonSerializer.Export(package), cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        RequireInstalledPackageId(package, pluginId);");
        builder.AppendLine("        _installedPluginIds.Add(package.Manifest.PluginId);");
        builder.AppendLine("        return pluginId;");
        builder.AppendLine("    }");
        builder.AppendLine("    private static void RequireInstalledPackageId(global::DotBoxD.Plugins.PluginPackage package, string pluginId)");
        builder.AppendLine("    {");
        builder.AppendLine("        var manifestId = package.Manifest.PluginId;");
        builder.AppendLine("        if (global::System.StringComparer.Ordinal.Equals(pluginId, manifestId))");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        var callbackId = package.CallbackSubscriptionId;");
        builder.AppendLine("        if (callbackId is not null && global::System.StringComparer.Ordinal.Equals(pluginId, callbackId))");
        builder.AppendLine("        {");
        builder.AppendLine("            return;");
        builder.AppendLine("        }");
        builder.AppendLine("        throw new global::System.InvalidOperationException($\"Installed package id '{pluginId}' did not match manifest id '{manifestId}'.\");");
        builder.AppendLine("    }");
        builder.AppendLine("    private void RequireInstalledKernel<TKernel>(string pluginId)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (!_installedPluginIds.Contains(pluginId))");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.InvalidOperationException($\"Kernel '{typeof(TKernel).FullName}' has not been installed.\");");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }
}
