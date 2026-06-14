namespace DotBoxd.Plugins;

using DotBoxd.Kernels;

public interface ILiveSetting
{
    string Name { get; }
    LiveSettingDefinition Definition { get; }
    object? CurrentValue { get; }
    SandboxValue ToSandboxValue();
    void SetObject(object? value);
}

// Splits validation from storage so a value is coerced and range-validated exactly once
// per update. The store coerces through <see cref="Coerce"/> when it needs the trusted typed
// value up front (atomic batch checks) and then stores it through <see cref="ApplyCoerced"/>,
// which never re-runs conversion or range checks. Single updates still flow through the
// public <see cref="ILiveSetting.SetObject(object?)"/>, the one coercion site for the slot.
internal interface ICoercibleLiveSetting : ILiveSetting
{
    object? Coerce(object? value);
    void ApplyCoerced(object? coercedValue);
}

public sealed class LiveValue<T> : ICoercibleLiveSetting
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
        => Value = (T)Coerce(value)!;

    object? ICoercibleLiveSetting.Coerce(object? value) => Coerce(value);

    void ICoercibleLiveSetting.ApplyCoerced(object? coercedValue) => Value = (T)coercedValue!;

    private object? Coerce(object? value)
    {
        var coerced = LiveSettingTypeConverter.CoerceClr(typeof(T), value);
        LiveSettingTypeConverter.ValidateRangeValue(Definition, coerced);
        return coerced;
    }
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
            // The slot is the single coercion/validation site; hand it the raw caller value
            // so conversion and range checks run exactly once instead of once here and again
            // inside the slot.
            Resolve(name).SetObject(value);
        }
    }

    public void SetMany(IReadOnlyDictionary<string, object?> values)
    {
        lock (_gate) {
            // Resolve every slot first so an unknown setting fails before any value is applied,
            // preserving the all-or-nothing batch contract.
            var resolved = new (ILiveSetting Setting, object? Value)[values.Count];
            var index = 0;
            foreach (var item in values) {
                resolved[index++] = (Resolve(item.Key), item.Value);
            }

            // Coerce and range-validate the whole batch up front for slots that support the
            // validate/store split, again preserving atomicity: a single invalid value aborts
            // the batch before anything is stored. Trusted typed values are then applied without
            // re-coercing. Foreign slots keep their own single coercion site via SetObject.
            for (var i = 0; i < resolved.Length; i++) {
                if (resolved[i].Setting is ICoercibleLiveSetting coercible) {
                    resolved[i] = (coercible, coercible.Coerce(resolved[i].Value));
                }
            }

            foreach (var (setting, value) in resolved) {
                if (setting is ICoercibleLiveSetting coercible) {
                    coercible.ApplyCoerced(value);
                } else {
                    setting.SetObject(value);
                }
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

    private ILiveSetting Resolve(string name)
        => _settings.TryGetValue(name, out var setting)
            ? setting
            : throw new KeyNotFoundException($"Live setting '{name}' is not registered.");

    private sealed class LiveSettingSlot(LiveSettingDefinition definition, object? value) : ICoercibleLiveSetting
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
            => ApplyCoerced(Coerce(value));

        public object? Coerce(object? value)
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
            return coerced;
        }

        public void ApplyCoerced(object? coercedValue)
        {
            lock (_gate) {
                _value = coercedValue;
            }
        }
    }
}
