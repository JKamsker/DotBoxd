using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerSetupEmitter
{
    public static void AppendSetupMembers(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine("    private enum RecordedInstallKind");
        builder.AppendLine("    {");
        builder.AppendLine("        Plugin,");
        builder.AppendLine("        Subscription,");
        builder.AppendLine("        ServerExtension");
        builder.AppendLine("    }");
        builder.AppendLine();
        AppendRecordedInstall(builder);
        AppendRecordSetup(builder, model);
        AppendReplaySetup(builder);
        AppendSetupRecorder(builder, model);
        foreach (var control in model.Controls)
        {
            AppendControlAccumulator(builder, control);
        }
    }

    public static void AppendBuilder(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.Append("public sealed class ").Append(model.ClassName).AppendLine("Builder");
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<global::DotBoxD.Services.Peer.RpcPeerSession>>? _connectionFactory;");
        builder.Append("    private readonly ").Append(model.ControlServiceType).AppendLine("? _control;");
        builder.Append("    private readonly ").Append(model.WorldType).AppendLine("? _world;");
        builder.Append("    private global::System.Action<").Append(model.SetupInterfaceName).AppendLine(">? _setup;");
        builder.AppendLine("    private " + model.ClassName + "Builder(global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<global::DotBoxD.Services.Peer.RpcPeerSession>> connectionFactory) => _connectionFactory = connectionFactory;");
        builder.AppendLine("    private " + model.ClassName + "Builder(" + model.ControlServiceType + " control, " + model.WorldType + "? world) { _control = control; _world = world; }");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromPipeName(string pipeName)");
        builder.AppendLine("        => new(ct => new global::System.Threading.Tasks.ValueTask<global::DotBoxD.Services.Peer.RpcPeerSession>(global::DotBoxD.Pushdown.Services.RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName, cancellationToken: ct)));");
        builder.AppendLine("    public static " + model.ClassName + "Builder FromConnection(" + model.ControlServiceType + " control, " + model.WorldType + "? world = null)");
        builder.AppendLine("        => new(control, world);");
        builder.AppendLine("    public " + model.ClassName + "Builder Setup(global::System.Action<" + model.SetupInterfaceName + "> configure)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(configure);");
        builder.AppendLine("        _setup += configure;");
        builder.AppendLine("        return this;");
        builder.AppendLine("    }");
        builder.AppendLine("    public " + model.ServerInterfaceName + " Build()");
        builder.AppendLine("        => _connectionFactory is not null ? new " + model.ClassName + "(_connectionFactory, _setup) : new " + model.ClassName + "(_control!, _world, _setup);");
        builder.AppendLine("}");
    }

    public static void AppendSetupInterfaces(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine();
        builder.Append(model.Accessibility).Append(" interface ").Append(model.SetupInterfaceName).AppendLine();
        builder.AppendLine("{");
        builder.Append("    ").Append(model.SetupInterfaceName).AppendLine(" Replace<TService, TKernel>() where TService : class where TKernel : class, TService;");
        builder.AppendLine("    global::DotBoxD.Plugins.Runtime.RemoteHookRegistry Hooks { get; }");
        builder.AppendLine("    global::DotBoxD.Plugins.Runtime.RemoteSubscriptionRegistry Subscriptions { get; }");
        foreach (var control in model.Controls)
        {
            builder.Append("    ").Append(control.AccumulatorInterfaceName).Append(' ')
                .Append(control.Name).AppendLine(" { get; }");
        }

        builder.AppendLine("}");
        foreach (var control in model.Controls)
        {
            builder.AppendLine();
            builder.Append(model.Accessibility).Append(" interface ").Append(control.AccumulatorInterfaceName).AppendLine();
            builder.AppendLine("{");
            builder.Append("    ").Append(control.AccumulatorInterfaceName).AppendLine(" Extend<TKernel>() where TKernel : class;");
            builder.Append("    ").Append(control.AccumulatorInterfaceName).AppendLine(" Extend<TService, TKernel>() where TService : class where TKernel : class;");
            builder.AppendLine("}");
        }
    }

    private static void AppendRecordedInstall(StringBuilder builder)
    {
        builder.AppendLine("    private readonly struct RecordedInstall");
        builder.AppendLine("    {");
        builder.AppendLine("        private RecordedInstall(RecordedInstallKind kind, global::DotBoxD.Plugins.PluginPackage package, global::System.Type? registryKey)");
        builder.AppendLine("        {");
        builder.AppendLine("            Kind = kind;");
        builder.AppendLine("            Package = package;");
        builder.AppendLine("            RegistryKey = registryKey;");
        builder.AppendLine("        }");
        builder.AppendLine("        public RecordedInstallKind Kind { get; }");
        builder.AppendLine("        public global::DotBoxD.Plugins.PluginPackage Package { get; }");
        builder.AppendLine("        public global::System.Type? RegistryKey { get; }");
        builder.AppendLine("        public static RecordedInstall Plugin(global::DotBoxD.Plugins.PluginPackage package) => new(RecordedInstallKind.Plugin, package, null);");
        builder.AppendLine("        public static RecordedInstall Subscription(global::DotBoxD.Plugins.PluginPackage package) => new(RecordedInstallKind.Subscription, package, null);");
        builder.AppendLine("        public static RecordedInstall ServerExtension(global::DotBoxD.Plugins.PluginPackage package, global::System.Type registryKey) => new(RecordedInstallKind.ServerExtension, package, registryKey);");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendRecordSetup(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.Append("    private static global::System.Collections.Generic.List<RecordedInstall> RecordSetup(global::System.Action<")
            .Append(model.SetupInterfaceName).AppendLine(">? setup)");
        builder.AppendLine("    {");
        builder.AppendLine("        var installs = new global::System.Collections.Generic.List<RecordedInstall>();");
        builder.AppendLine("        if (setup is not null)");
        builder.AppendLine("        {");
        builder.AppendLine("            setup(new SetupRecorder(installs));");
        builder.AppendLine("        }");
        builder.AppendLine("        return installs;");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendReplaySetup(StringBuilder builder)
    {
        builder.AppendLine("    private async global::System.Threading.Tasks.ValueTask ReplaySetupAsync(global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (_setupReplayed) { return; }");
        builder.AppendLine("        foreach (var install in _setupInstalls)");
        builder.AppendLine("        {");
        builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        builder.AppendLine("            if (install.Kind == RecordedInstallKind.Plugin)");
        builder.AppendLine("            {");
        builder.AppendLine("                _ = await InstallPluginPackageAsync(install.Package, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("                continue;");
        builder.AppendLine("            }");
        builder.AppendLine("            if (install.Kind == RecordedInstallKind.Subscription)");
        builder.AppendLine("            {");
        builder.AppendLine("                _ = await InstallSubscriptionPackageAsync(install.Package, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("                continue;");
        builder.AppendLine("            }");
        builder.AppendLine("            var pluginId = await InstallServerExtensionPackageAsync(install.Package, cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            if (install.RegistryKey is not null) { _serverExtensions[install.RegistryKey] = pluginId; }");
        builder.AppendLine("        }");
        builder.AppendLine("        _setupReplayed = true;");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendSetupRecorder(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.Append("    private sealed class SetupRecorder : ").Append(model.SetupInterfaceName).AppendLine();
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::System.Collections.Generic.List<RecordedInstall> _installs;");
        builder.AppendLine("        private readonly global::DotBoxD.Plugins.Runtime.RemoteHookRegistry _hooks;");
        builder.AppendLine("        private readonly global::DotBoxD.Plugins.Runtime.RemoteSubscriptionRegistry _subscriptions;");
        foreach (var control in model.Controls)
        {
            builder.Append("        private readonly ").Append(control.AccumulatorInterfaceName).Append(' ')
                .Append(FieldName(control.Name)).AppendLine(";");
        }

        builder.AppendLine("        public SetupRecorder(global::System.Collections.Generic.List<RecordedInstall> installs)");
        builder.AppendLine("        {");
        builder.AppendLine("            _installs = installs;");
        builder.AppendLine("            _hooks = new global::DotBoxD.Plugins.Runtime.RemoteHookRegistry(package =>");
        builder.AppendLine("            {");
        builder.AppendLine("                _installs.Add(RecordedInstall.Plugin(package));");
        builder.AppendLine("                return global::System.Threading.Tasks.ValueTask.FromResult(package.Manifest.PluginId);");
        builder.AppendLine("            });");
        builder.AppendLine("            _subscriptions = new global::DotBoxD.Plugins.Runtime.RemoteSubscriptionRegistry(package =>");
        builder.AppendLine("            {");
        builder.AppendLine("                _installs.Add(RecordedInstall.Subscription(package));");
        builder.AppendLine("                return global::System.Threading.Tasks.ValueTask.FromResult(package.Manifest.PluginId);");
        builder.AppendLine("            });");
        foreach (var control in model.Controls)
        {
            builder.Append("            ").Append(FieldName(control.Name)).Append(" = new ")
                .Append(control.Name).AppendLine("SetupAccumulator(installs);");
        }

        builder.AppendLine("        }");
        builder.Append("        public ").Append(model.SetupInterfaceName).AppendLine(" Replace<TService, TKernel>() where TService : class where TKernel : class, TService");
        builder.AppendLine("        {");
        builder.AppendLine("            _installs.Add(RecordedInstall.Plugin(global::DotBoxD.Plugins.Kernel.KernelPackageRegistry.Resolve<TKernel>()));");
        builder.AppendLine("            return this;");
        builder.AppendLine("        }");
        foreach (var control in model.Controls)
        {
            builder.Append("        public ").Append(control.AccumulatorInterfaceName).Append(' ')
                .Append(control.Name).Append(" => ").Append(FieldName(control.Name)).AppendLine(";");
        }
        builder.AppendLine("        public global::DotBoxD.Plugins.Runtime.RemoteHookRegistry Hooks => _hooks;");
        builder.AppendLine("        public global::DotBoxD.Plugins.Runtime.RemoteSubscriptionRegistry Subscriptions => _subscriptions;");

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendControlAccumulator(StringBuilder builder, PluginServerControlProperty control)
    {
        builder.Append("    private sealed class ").Append(control.Name).Append("SetupAccumulator : ")
            .Append(control.AccumulatorInterfaceName).AppendLine();
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::System.Collections.Generic.List<RecordedInstall> _installs;");
        builder.Append("        public ").Append(control.Name).AppendLine("SetupAccumulator(global::System.Collections.Generic.List<RecordedInstall> installs) => _installs = installs;");
        builder.Append("        public ").Append(control.AccumulatorInterfaceName).AppendLine(" Extend<TKernel>() where TKernel : class");
        builder.AppendLine("        {");
        builder.AppendLine("            Add<TKernel>();");
        builder.AppendLine("            return this;");
        builder.AppendLine("        }");
        builder.Append("        public ").Append(control.AccumulatorInterfaceName).AppendLine(" Extend<TService, TKernel>() where TService : class where TKernel : class");
        builder.AppendLine("        {");
        builder.AppendLine("            Add<TKernel>();");
        builder.AppendLine("            return this;");
        builder.AppendLine("        }");
        builder.AppendLine("        private void Add<TKernel>() where TKernel : class");
        builder.AppendLine("            => _installs.Add(RecordedInstall.ServerExtension(global::DotBoxD.Plugins.Kernel.KernelPackageRegistry.Resolve<TKernel>(), typeof(TKernel)));");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static string FieldName(string propertyName)
        => "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
}
