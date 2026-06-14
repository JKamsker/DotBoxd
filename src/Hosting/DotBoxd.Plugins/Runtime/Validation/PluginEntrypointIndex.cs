namespace DotBoxd.Plugins;

using DotBoxd.Kernels;

/// <summary>
/// Indexes a module's public entrypoint functions by id once so package validation can resolve the
/// fixed set of kernel entrypoints (ShouldHandle, Handle) without rescanning the full function list
/// for every lookup. Preserves the prior <c>FirstOrDefault</c>/<c>Any</c> semantics: only functions
/// with <see cref="SandboxFunction.IsEntrypoint"/> set are indexed, and the first occurrence of a
/// given id wins.
/// </summary>
internal readonly struct PluginEntrypointIndex
{
    private readonly Dictionary<string, SandboxFunction> _entrypoints;

    private PluginEntrypointIndex(Dictionary<string, SandboxFunction> entrypoints)
        => _entrypoints = entrypoints;

    public static PluginEntrypointIndex Build(PluginPackage package)
    {
        var functions = package.Module.Functions;
        var entrypoints = new Dictionary<string, SandboxFunction>(StringComparer.Ordinal);
        for (var i = 0; i < functions.Count; i++)
        {
            var function = functions[i];
            if (function.IsEntrypoint)
            {
                _ = entrypoints.TryAdd(function.Id, function);
            }
        }

        return new PluginEntrypointIndex(entrypoints);
    }

    public bool Contains(string functionId) => _entrypoints.ContainsKey(functionId);

    public bool TryGet(string functionId, out SandboxFunction function)
        => _entrypoints.TryGetValue(functionId, out function!);
}
