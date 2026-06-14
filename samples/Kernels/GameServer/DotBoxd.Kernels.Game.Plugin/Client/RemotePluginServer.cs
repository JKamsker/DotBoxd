namespace DotBoxd.Kernels.Game.Plugin.Client;

using System.Globalization;
using System.Reflection;

/// <summary>
/// Example-local facade that gives the plugin a server-shaped surface
/// (<c>server.Kernels.Register&lt;TService, TKernel&gt;()</c>,
/// <c>server.Kernels.Get&lt;TKernel&gt;().SetValuesAsync(..)</c>) while forwarding over the unchanged
/// <see cref="IGamePluginControlService"/> IPC contract. It resolves each kernel's analyzer-generated
/// package by type (via <see cref="KernelPackageRegistry"/>) and ships it as verified IR.
/// </summary>
internal sealed class RemotePluginServer
{
    private readonly IGamePluginControlService _control;

    public RemotePluginServer(IGamePluginControlService control)
    {
        _control = control;
        Kernels = new RemoteKernelControl(control);
    }

    public RemoteKernelControl Kernels { get; }

    /// <summary>Holds the connection open until the server completes its with-plugin phase.</summary>
    public ValueTask HoldUntilShutdownAsync() => _control.HoldUntilShutdownAsync();
}

internal sealed class RemoteKernelControl
{
    private readonly IGamePluginControlService _control;

    public RemoteKernelControl(IGamePluginControlService control) => _control = control;

    /// <summary>
    /// Registers a kernel as the implementation of a server service contract: resolves its generated
    /// verified-IR package and ships it. Returns the installed plugin id.
    /// </summary>
    public async ValueTask<string> Register<TService, TKernel>()
        where TService : class
        where TKernel : class, TService
    {
        var json = PluginPackageJsonSerializer.Export(KernelPackageRegistry.Resolve<TKernel>());
        return await _control.InstallPluginAsync(json).ConfigureAwait(false);
    }

    /// <summary>Strongly-typed settings handle for a kernel this plugin authored.</summary>
    public RemoteKernelHandle<TKernel> Get<TKernel>() where TKernel : class, new()
        => new(_control, PluginId(typeof(TKernel)));

    internal static string PluginId(Type kernelType)
        => kernelType.GetCustomAttribute<PluginAttribute>()?.Id
           ?? throw new InvalidOperationException($"Kernel '{kernelType.FullName}' has no [Plugin] id.");
}

internal sealed class RemoteKernelHandle<TKernel> where TKernel : class, new()
{
    private readonly IGamePluginControlService _control;
    private readonly string _pluginId;

    public RemoteKernelHandle(IGamePluginControlService control, string pluginId)
    {
        _control = control;
        _pluginId = pluginId;
    }

    /// <summary>
    /// Sets live setting values from a typed lambda. The lambda mutates a local draft; the resulting
    /// <c>[LiveSetting]</c> values are shipped over IPC. (For read-modify-write against live server
    /// state, do it server-side under the kernel's execution gate.)
    /// </summary>
    public ValueTask SetValuesAsync(Action<TKernel> set, bool atomic = false)
    {
        ArgumentNullException.ThrowIfNull(set);
        var draft = new TKernel();
        set(draft);

        var updates = typeof(TKernel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is not null && p.GetCustomAttribute<LiveSettingAttribute>() is not null)
            .Select(p => new LiveSettingUpdate(
                p.Name,
                Convert.ToString(p.GetValue(draft), CultureInfo.InvariantCulture) ?? string.Empty))
            .ToArray();

        return _control.UpdateSettingsAsync(_pluginId, updates, atomic);
    }
}
