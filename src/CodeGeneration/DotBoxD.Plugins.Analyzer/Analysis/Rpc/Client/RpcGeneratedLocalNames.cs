using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed class RpcGeneratedLocalNames
{
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public RpcGeneratedLocalNames(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            _used.Add(parameter.Name);
        }
    }

    public string Next(string baseName)
    {
        var name = baseName;
        for (var suffix = 0; !_used.Add(name); suffix++)
        {
            name = baseName + suffix.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }

    public void Reserve(string name)
        => _used.Add(name);
}
