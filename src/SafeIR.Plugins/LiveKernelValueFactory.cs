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
        kernel.RegisterStateSynchronizer(typeof(T), () => PushToStore(kernel, state, properties));
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
        var values = new Dictionary<string, object?>(settings.Count, StringComparer.Ordinal);
        foreach (var property in LiveProperties(typeof(T))) {
            if (HasSetting(settings, property.Name)) {
                values[property.Name] = property.GetValue(state);
            }
        }

        return values;
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
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var marked = FilterLiveProperties(properties, requireAttribute: true);
        if (marked.Length > 0)
        {
            return marked;
        }

        return FilterLiveProperties(properties, requireAttribute: false);
    }

    private static PropertyInfo[] FilterLiveProperties(PropertyInfo[] properties, bool requireAttribute)
    {
        var count = 0;
        for (var i = 0; i < properties.Length; i++) {
            if (IsLiveProperty(properties[i], requireAttribute)) {
                count++;
            }
        }

        if (count == 0) {
            return [];
        }

        if (count == properties.Length) {
            return properties;
        }

        var filtered = new PropertyInfo[count];
        var index = 0;
        for (var i = 0; i < properties.Length; i++) {
            var property = properties[i];
            if (IsLiveProperty(property, requireAttribute)) {
                filtered[index] = property;
                index++;
            }
        }

        return filtered;
    }

    private static bool IsLiveProperty(PropertyInfo property, bool requireAttribute)
    {
        return property.CanRead &&
            property.CanWrite &&
            (!requireAttribute || Attribute.IsDefined(property, typeof(LiveSettingAttribute)));
    }

    private static void PullFromStore<T>(InstalledKernel kernel, T state, IReadOnlyList<PropertyInfo> properties)
    {
        foreach (var property in properties) {
            if (!HasSetting(kernel.Manifest.LiveSettings, property.Name)) {
                continue;
            }

            var value = LiveSettingTypeConverter.CoerceClr(property.PropertyType, kernel.Value.GetObject(property.Name));
            property.SetValue(state, value);
        }
    }

    private static void PushToStore<T>(InstalledKernel kernel, T state, IReadOnlyList<PropertyInfo> properties)
    {
        var settings = kernel.Manifest.LiveSettings;
        var values = new Dictionary<string, object?>(settings.Count, StringComparer.Ordinal);
        foreach (var property in properties) {
            if (HasSetting(settings, property.Name)) {
                values[property.Name] = property.GetValue(state);
            }
        }

        kernel.Value.SetMany(values);
    }

    private static bool HasSetting(IReadOnlyList<LiveSettingDefinition> settings, string name)
    {
        for (var i = 0; i < settings.Count; i++) {
            if (string.Equals(settings[i].Name, name, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
