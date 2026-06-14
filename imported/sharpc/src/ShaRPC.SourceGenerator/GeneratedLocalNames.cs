using System.Collections.Generic;
using System.Threading;

namespace ShaRPC.SourceGenerator;

internal sealed class GeneratedLocalNames
{
    private readonly HashSet<string> _used = new(System.StringComparer.Ordinal);

    public GeneratedLocalNames(EquatableArray<ParameterModel> parameters, CancellationToken ct)
    {
        foreach (var parameter in parameters.Array)
        {
            ct.ThrowIfCancellationRequested();
            _used.Add(parameter.Name);
        }
    }

    public string Reserve(string baseName, CancellationToken ct)
    {
        var candidate = baseName;
        var suffix = 1;
        while (!_used.Add(candidate))
        {
            ct.ThrowIfCancellationRequested();
            candidate = baseName + suffix;
            suffix++;
        }

        return candidate;
    }
}
