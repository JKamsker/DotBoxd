namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

using System.Text;

internal static partial class PluginServerFacadeEmitter
{
    private static void AppendLifecycle(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Connects the generated plugin server, initializes runtime domain, hook, subscription, and extension APIs, and replays setup registrations once.");
        builder.AppendLine("    public async global::System.Threading.Tasks.ValueTask StartAsync(global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        ThrowIfDisposed();");
        builder.AppendLine("        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            ThrowIfDisposed();");
        builder.AppendLine("            if (!_started)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (_connectionFactory is null) { throw new global::System.InvalidOperationException(NotStartedMessage); }");
        if (model.EventCallbackType is not null)
        {
            // Provide the reverse event-callback sink during the connect before services start.
            builder.AppendLine("                _session = await _connectionFactory(peer => global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.Provide" + model.EventCallbackProvideSuffix + "(peer, new RemoteLocalEventSink(_localHandlers)), cancellationToken).ConfigureAwait(false);");
        }
        else
        {
            builder.AppendLine("                _session = await _connectionFactory(null, cancellationToken).ConfigureAwait(false);");
        }

        builder.AppendLine("                var control = _session.Get<" + model.ControlServiceType + ">();");
        builder.AppendLine("                var world = global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.Get" + model.WorldExtensionSuffix + "(_session.Peer);");
        builder.AppendLine("                Initialize(control, world);");
        builder.AppendLine("                _started = true;");
        builder.AppendLine("            }");
        builder.AppendLine("            await ReplaySetupAsync(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            if (!_configured)");
        builder.AppendLine("            {");
        builder.AppendLine("                OnConfigured();");
        builder.AppendLine("                _configured = true;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            _startGate.Release();");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Starts the generated plugin server and then waits until the remote host shuts down or the operation is cancelled.");
        builder.AppendLine("    public async global::System.Threading.Tasks.ValueTask RunAsync(global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        await StartAsync(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("        await HoldUntilShutdownAsync(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("    }");
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Waits for the remote host to signal shutdown after the generated plugin server has started.");
        builder.AppendLine("    public global::System.Threading.Tasks.ValueTask HoldUntilShutdownAsync(global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        => RequireControl().HoldUntilShutdownAsync(cancellationToken);");
        builder.AppendLine();
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Synchronously releases the generated plugin server session and any owned connection.");
        builder.AppendLine("    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "    ",
            "Asynchronously releases the generated plugin server session and any owned connection.");
        builder.AppendLine("    public async global::System.Threading.Tasks.ValueTask DisposeAsync()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (_disposed) { return; }");
        builder.AppendLine("        _disposed = true;");
        builder.AppendLine("        if (_session is not null) { await _session.DisposeAsync().ConfigureAwait(false); }");
        builder.AppendLine("        _started = false; _control = null; _world = null; _hooks = null; _subscriptions = null; _session = null;");
        if (model.EventCallbackType is not null)
        {
            builder.AppendLine("        _localHandlers.Clear();");
        }

        foreach (var control in model.Controls)
        {
            builder.Append("        ").Append(control.FieldName).AppendLine(" = null;");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void Initialize(" + model.ControlServiceType + " control, " + model.WorldType + "? world)");
        builder.AppendLine("    {");
        builder.AppendLine("        _control = control;");
        builder.AppendLine("        _world = world;");
        var localHandlersArg = model.EventCallbackType is not null ? ", _localHandlers" : string.Empty;
        builder.AppendLine("        _hooks = new " + model.HookRegistryName + "(package => InstallPluginPackageAsync(package)" + localHandlersArg + ");");
        builder.AppendLine("        _subscriptions = new " + model.SubscriptionRegistryName + "(package => InstallSubscriptionPackageAsync(package)" + localHandlersArg + ");");
        foreach (var control in model.Controls)
        {
            builder.Append("        ").Append(control.FieldName).Append(" = world is null ? null : new ")
                .Append(control.WrapperName).Append("(this, world.")
                .Append(PluginServerIdentifier.Escape(control.Name)).AppendLine(");");
        }

        builder.AppendLine("    }");
        builder.AppendLine("    private " + PluginServerIdentifier.Escape(model.ClassName) + " RequireFacade() { ThrowIfDisposed(); return this; }");
        builder.AppendLine("    private T RequireStarted<T>(T? value) where T : class { ThrowIfDisposed(); return _started && value is not null ? value : throw new global::System.InvalidOperationException(NotStartedMessage); }");
        builder.AppendLine("    private " + model.ControlServiceType + " RequireControl() => RequireStarted(_control);");
        builder.AppendLine("    private " + model.WorldType + " RequireWorld() => RequireStarted(_world);");
        builder.AppendLine("    private void ThrowIfDisposed() => global::System.ObjectDisposedException.ThrowIf(_disposed, this);");
    }
}
