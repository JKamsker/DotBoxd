namespace SafeIR;

using System.Collections.ObjectModel;

internal static class ModelCopy
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
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
