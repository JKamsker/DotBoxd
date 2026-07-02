using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Server.PluginCatalog;

internal sealed class ClientOperationRegistry
{
    private readonly Dictionary<string, InstalledKernel> _operations = new(StringComparer.Ordinal);

    public void Register(string operation, InstalledKernel kernel) => _operations[operation] = kernel;

    public bool TryResolve(string operation, out InstalledKernel kernel)
        => _operations.TryGetValue(operation, out kernel!);

    public IReadOnlyList<string> Snapshot()
        => _operations.Keys.Order(StringComparer.Ordinal).ToArray();
}
