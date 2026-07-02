using System.Collections.ObjectModel;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Model;

internal static class ModelCopy
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        // The empty case is hot: every scalar SandboxType and every empty-argument model copy lands here.
        // Hand back a single shared, immutable empty view instead of allocating a fresh wrapper each time.
        if (values is IReadOnlyCollection<T> { Count: 0 } or ICollection<T> { Count: 0 })
        {
            return EmptyReadOnlyList<T>.Instance;
        }

        return new ReadOnlyCollection<T>(values.ToArray());
    }

    private static class EmptyReadOnlyList<T>
    {
        public static readonly ReadOnlyCollection<T> Instance = new(Array.Empty<T>());
    }

    /// <summary>
    /// Wraps an array the caller has just allocated, fully populated, and will never
    /// expose for mutation in a read-only view without an extra defensive copy.
    /// Only use this for arrays owned by the immediate caller; never for arrays that
    /// originate outside the current call.
    /// </summary>
    public static IReadOnlyList<T> WrapOwned<T>(T[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values);
    }

    public static IReadOnlyDictionary<string, string> StringDictionary(
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values, StringComparer.Ordinal));
    }

    public static IReadOnlyDictionary<string, TValue> Dictionary<TValue>(
        IReadOnlyDictionary<string, TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<string, TValue>(
            new Dictionary<string, TValue>(values, StringComparer.Ordinal));
    }

    public static IReadOnlyDictionary<SandboxValue, SandboxValue> ValueDictionary(
        IReadOnlyDictionary<SandboxValue, SandboxValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyDictionary<SandboxValue, SandboxValue>(
            new Dictionary<SandboxValue, SandboxValue>(values));
    }
}
