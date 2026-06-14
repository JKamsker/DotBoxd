namespace DotBoxd.Plugins;

using System.Collections.ObjectModel;

internal static class PluginModelCopy
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}
