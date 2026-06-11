namespace SafeIR.Plugins;

using System.Reflection;

internal static class LiveKernelValueFactory
{
    public static T Create<T>(InstalledKernel kernel) where T : class
    {
        if (typeof(T).IsInterface) {
            return kernel.Value.As<T>();
        }

        var state = Activator.CreateInstance<T>();
        var properties = LiveProperties(typeof(T));
        PullFromStore(kernel, state, properties);
        kernel.RegisterStateSynchronizer(() => PushToStore(kernel, state, properties));
        return state;
    }

    public static T CreateDraft<T>(T source) where T : class
    {
        var draft = Activator.CreateInstance<T>();
        CopyLiveProperties(source, draft);
        return draft;
    }

    public static IReadOnlyDictionary<string, object?> ExtractSettings<T>(
        T state,
        IReadOnlyList<LiveSettingDefinition> settings) where T : class
    {
        var settingNames = settings.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        return LiveProperties(typeof(T))
            .Where(p => settingNames.Contains(p.Name))
            .ToDictionary(p => p.Name, p => p.GetValue(state), StringComparer.Ordinal);
    }

    public static void CopyLiveProperties<T>(T source, T target) where T : class
    {
        foreach (var property in LiveProperties(typeof(T))) {
            property.SetValue(target, property.GetValue(source));
        }
    }

    public static void PullFromStore<T>(InstalledKernel kernel, T state) where T : class
        => PullFromStore(kernel, state, LiveProperties(typeof(T)));

    public static void PullFromStore(InstalledKernel kernel, object state)
        => PullFromStore(kernel, state, LiveProperties(state.GetType()));

    private static IReadOnlyList<PropertyInfo> LiveProperties(Type type)
    {
        var marked = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite && Attribute.IsDefined(p, typeof(LiveSettingAttribute)))
            .ToArray();
        if (marked.Length > 0) {
            return marked;
        }

        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();
    }

    private static void PullFromStore<T>(InstalledKernel kernel, T state, IReadOnlyList<PropertyInfo> properties)
    {
        foreach (var property in properties) {
            if (!kernel.Manifest.LiveSettings.Any(s => string.Equals(s.Name, property.Name, StringComparison.Ordinal))) {
                continue;
            }

            var value = LiveSettingTypeConverter.CoerceClr(property.PropertyType, kernel.Value.GetObject(property.Name));
            property.SetValue(state, value);
        }
    }

    private static void PushToStore<T>(InstalledKernel kernel, T state, IReadOnlyList<PropertyInfo> properties)
    {
        var settings = kernel.Manifest.LiveSettings;
        var settingNames = settings.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var values = properties
            .Where(p => settingNames.Contains(p.Name))
            .ToDictionary(p => p.Name, p => p.GetValue(state), StringComparer.Ordinal);
        kernel.Value.SetMany(values);
    }
}
