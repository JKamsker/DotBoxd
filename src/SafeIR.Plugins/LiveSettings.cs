namespace SafeIR.Plugins;

using SafeIR;

public interface ILiveSetting
{
    string Name { get; }
    LiveSettingDefinition Definition { get; }
    object? CurrentValue { get; }
    SandboxValue ToSandboxValue();
    void SetObject(object? value);
}

public sealed class LiveValue<T> : ILiveSetting
{
    private readonly object _gate = new();
    private T _value;

    public LiveValue(string name, T value)
        : this(new LiveSettingDefinition(name, LiveSettingTypeConverter.FromClrType(typeof(T)), value), value)
    {
    }

    internal LiveValue(LiveSettingDefinition definition, T value)
    {
        Definition = definition;
        _value = value;
    }

    public string Name => Definition.Name;
    public LiveSettingDefinition Definition { get; }

    public T Value
    {
        get {
            lock (_gate) {
                return _value;
            }
        }
        set {
            lock (_gate) {
                _value = value;
            }
        }
    }

    public object? CurrentValue => Value;

    public SandboxValue ToSandboxValue()
        => LiveSettingTypeConverter.ToSandboxValue(Definition.Type, Value);

    public void SetObject(object? value)
        => Value = (T)LiveSettingTypeConverter.CoerceClr(typeof(T), value)!;
}

public sealed class LiveSettingStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ILiveSetting> _settings;

    public LiveSettingStore(IEnumerable<ILiveSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = new Dictionary<string, ILiveSetting>(StringComparer.Ordinal);
        foreach (var setting in settings) {
            _settings.Add(setting.Name, setting);
        }
    }

    private LiveSettingStore(Dictionary<string, ILiveSetting> settings)
        => _settings = settings;

    public IReadOnlyList<LiveSettingDefinition> Definitions
    {
        get {
            var definitions = new LiveSettingDefinition[_settings.Count];
            var index = 0;
            foreach (var setting in _settings.Values) {
                definitions[index] = setting.Definition;
                index++;
            }

            Array.Sort(definitions, static (left, right) =>
                string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return definitions;
        }
    }

    public T Get<T>(string name)
    {
        lock (_gate) {
            return _settings.TryGetValue(name, out var setting)
                ? (T)LiveSettingTypeConverter.CoerceClr(typeof(T), setting.CurrentValue)!
                : throw new KeyNotFoundException($"Live setting '{name}' is not registered.");
        }
    }

    public object? GetObject(string name)
    {
        lock (_gate) {
            return _settings.TryGetValue(name, out var setting)
                ? setting.CurrentValue
                : throw new KeyNotFoundException($"Live setting '{name}' is not registered.");
        }
    }

    public void Set<T>(string name, T value)
    {
        SetObject(name, value);
    }

    public void SetObject(string name, object? value)
    {
        lock (_gate) {
            var coerced = CoerceAndValidate(name, value);
            coerced.Setting.SetObject(coerced.Value);
        }
    }

    public void SetMany(IReadOnlyDictionary<string, object?> values)
    {
        lock (_gate) {
            var coerced = CoerceAndValidate(values);
            foreach (var item in coerced) {
                _settings[item.Key].SetObject(item.Value);
            }
        }
    }

    public T As<T>() where T : class => LiveContextProxy<T>.Create(this);

    internal SandboxValue ToSandboxValue(LiveSettingDefinition setting)
    {
        lock (_gate) {
            return _settings[setting.Name].ToSandboxValue();
        }
    }

    internal void CopySandboxValues(
        IReadOnlyList<LiveSettingDefinition> orderedSettings,
        SandboxValue[] destination,
        int destinationIndex)
    {
        lock (_gate) {
            for (var i = 0; i < orderedSettings.Count; i++) {
                destination[destinationIndex + i] = _settings[orderedSettings[i].Name].ToSandboxValue();
            }
        }
    }

    internal IReadOnlyDictionary<string, object?> ToObjectValues(IReadOnlyList<LiveSettingDefinition> orderedSettings)
    {
        lock (_gate) {
            var values = new Dictionary<string, object?>(orderedSettings.Count, StringComparer.Ordinal);
            for (var i = 0; i < orderedSettings.Count; i++) {
                var setting = orderedSettings[i];
                values.Add(setting.Name, _settings[setting.Name].CurrentValue);
            }

            return values;
        }
    }

    internal LiveSettingStore Copy(IReadOnlyList<LiveSettingDefinition> orderedSettings)
    {
        lock (_gate) {
            var definitions = new LiveSettingDefinition[orderedSettings.Count];
            for (var i = 0; i < orderedSettings.Count; i++) {
                var setting = orderedSettings[i];
                definitions[i] = setting with { DefaultValue = _settings[setting.Name].CurrentValue };
            }

            return FromDefinitions(definitions);
        }
    }

    internal static LiveSettingStore FromDefinitions(IEnumerable<LiveSettingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var settings = new Dictionary<string, ILiveSetting>(StringComparer.Ordinal);
        foreach (var definition in definitions) {
            var value = definition.Type switch {
                PluginManifestNames.LiveSettingTypes.Bool => LiveSettingTypeConverter.CoerceClr(typeof(bool), definition.DefaultValue),
                PluginManifestNames.LiveSettingTypes.Int => LiveSettingTypeConverter.CoerceClr(typeof(int), definition.DefaultValue),
                PluginManifestNames.LiveSettingTypes.Long => LiveSettingTypeConverter.CoerceClr(typeof(long), definition.DefaultValue),
                PluginManifestNames.LiveSettingTypes.Double => LiveSettingTypeConverter.CoerceClr(typeof(double), definition.DefaultValue),
                PluginManifestNames.LiveSettingTypes.String => LiveSettingTypeConverter.CoerceClr(typeof(string), definition.DefaultValue),
                _ => throw LiveSettingTypeConverter.Diagnostic($"Live setting type '{definition.Type}' is not supported.")
            };
            LiveSettingTypeConverter.ValidateRangeValue(definition, value);
            settings.Add(definition.Name, new LiveSettingSlot(definition, value));
        }

        return new LiveSettingStore(settings);
    }

    private Dictionary<string, object?> CoerceAndValidate(IReadOnlyDictionary<string, object?> values)
    {
        var coerced = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);
        foreach (var item in values) {
            coerced[item.Key] = CoerceAndValidate(item.Key, item.Value).Value;
        }

        return coerced;
    }

    private (ILiveSetting Setting, object? Value) CoerceAndValidate(string name, object? value)
    {
        if (!_settings.TryGetValue(name, out var setting)) {
            throw new KeyNotFoundException($"Live setting '{name}' is not registered.");
        }

        var coerced = LiveSettingTypeConverter.CoerceClr(setting.Definition.Type, value);
        LiveSettingTypeConverter.ValidateRangeValue(setting.Definition, coerced);
        return (setting, coerced);
    }

    private sealed class LiveSettingSlot(LiveSettingDefinition definition, object? value) : ILiveSetting
    {
        private readonly object _gate = new();
        private object? _value = value;

        public string Name => definition.Name;
        public LiveSettingDefinition Definition => definition;

        public object? CurrentValue
        {
            get {
                lock (_gate) {
                    return _value;
                }
            }
        }

        public SandboxValue ToSandboxValue()
            => LiveSettingTypeConverter.ToSandboxValue(definition.Type, CurrentValue);

        public void SetObject(object? value)
        {
            var coerced = definition.Type switch {
                PluginManifestNames.LiveSettingTypes.Bool => LiveSettingTypeConverter.CoerceClr(typeof(bool), value),
                PluginManifestNames.LiveSettingTypes.Int => LiveSettingTypeConverter.CoerceClr(typeof(int), value),
                PluginManifestNames.LiveSettingTypes.Long => LiveSettingTypeConverter.CoerceClr(typeof(long), value),
                PluginManifestNames.LiveSettingTypes.Double => LiveSettingTypeConverter.CoerceClr(typeof(double), value),
                PluginManifestNames.LiveSettingTypes.String => LiveSettingTypeConverter.CoerceClr(typeof(string), value),
                _ => throw LiveSettingTypeConverter.Diagnostic($"Live setting type '{definition.Type}' is not supported.")
            };
            LiveSettingTypeConverter.ValidateRangeValue(definition, coerced);
            lock (_gate) {
                _value = coerced;
            }
        }
    }
}
