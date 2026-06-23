using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Plugins.Runtime;

public sealed class LiveSettingStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ILiveSetting> _settings;

    public LiveSettingStore(IEnumerable<ILiveSetting> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = new Dictionary<string, ILiveSetting>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            _settings.Add(setting.Name, setting);
        }
    }

    private LiveSettingStore(Dictionary<string, ILiveSetting> settings)
        => _settings = settings;

    public IReadOnlyList<LiveSettingDefinition> Definitions
    {
        get
        {
            var definitions = new LiveSettingDefinition[_settings.Count];
            var index = 0;
            foreach (var setting in _settings.Values)
            {
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
        lock (_gate)
        {
            return _settings.TryGetValue(name, out var setting)
                ? (T)LiveSettingTypeConverter.CoerceClr(typeof(T), setting.CurrentValue)!
                : throw new KeyNotFoundException($"Live setting '{name}' is not registered.");
        }
    }

    public object? GetObject(string name)
    {
        lock (_gate)
        {
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
        lock (_gate)
        {
            // The slot is the single coercion/validation site; hand it the raw caller value
            // so conversion and range checks run exactly once instead of once here and again
            // inside the slot.
            Resolve(name).SetObject(value);
        }
    }

    public void SetMany(IReadOnlyDictionary<string, object?> values)
    {
        lock (_gate)
        {
            // Resolve every slot first so an unknown setting fails before any value is applied,
            // preserving the all-or-nothing batch contract.
            var resolved = new (ILiveSetting Setting, object? Value)[values.Count];
            var index = 0;
            foreach (var item in values)
            {
                resolved[index++] = (Resolve(item.Key), item.Value);
            }

            // Coerce and range-validate built-in slots before anything is applied. Foreign slots
            // keep their own single coercion site via SetObject, so they are applied before the
            // built-in commit phase and rolled back best-effort if a later foreign slot fails.
            for (var i = 0; i < resolved.Length; i++)
            {
                if (resolved[i].Setting is ICoercibleLiveSetting coercible)
                {
                    resolved[i] = (coercible, coercible.Coerce(resolved[i].Value));
                }
            }

            ApplyForeignSettings(resolved);
            foreach (var (setting, value) in resolved)
            {
                if (setting is ICoercibleLiveSetting coercible)
                {
                    coercible.ApplyCoerced(value);
                }
            }
        }
    }

    private static void ApplyForeignSettings((ILiveSetting Setting, object? Value)[] resolved)
    {
        List<(ILiveSetting Setting, object? Previous)>? applied = null;
        try
        {
            foreach (var (setting, value) in resolved)
            {
                if (setting is ICoercibleLiveSetting)
                {
                    continue;
                }

                applied ??= [];
                applied.Add((setting, setting.CurrentValue));
                setting.SetObject(value);
            }
        }
        catch
        {
            if (applied is not null)
            {
                for (var i = applied.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        applied[i].Setting.SetObject(applied[i].Previous);
                    }
                    catch
                    {
                        // A foreign slot can reject rollback too; preserve the original failure.
                    }
                }
            }

            throw;
        }
    }

    public T As<T>() where T : class => LiveContextProxy<T>.Create(this);

    internal SandboxValue ToSandboxValue(LiveSettingDefinition setting)
    {
        lock (_gate)
        {
            return _settings[setting.Name].ToSandboxValue();
        }
    }

    internal void CopySandboxValues(
        IReadOnlyList<LiveSettingDefinition> orderedSettings,
        SandboxValue[] destination,
        int destinationIndex)
    {
        lock (_gate)
        {
            for (var i = 0; i < orderedSettings.Count; i++)
            {
                destination[destinationIndex + i] = _settings[orderedSettings[i].Name].ToSandboxValue();
            }
        }
    }

    internal IReadOnlyDictionary<string, object?> ToObjectValues(IReadOnlyList<LiveSettingDefinition> orderedSettings)
    {
        lock (_gate)
        {
            var values = new Dictionary<string, object?>(orderedSettings.Count, StringComparer.Ordinal);
            for (var i = 0; i < orderedSettings.Count; i++)
            {
                var setting = orderedSettings[i];
                values.Add(setting.Name, _settings[setting.Name].CurrentValue);
            }

            return values;
        }
    }

    internal LiveSettingStore Copy(IReadOnlyList<LiveSettingDefinition> orderedSettings)
    {
        lock (_gate)
        {
            var definitions = new LiveSettingDefinition[orderedSettings.Count];
            for (var i = 0; i < orderedSettings.Count; i++)
            {
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
        foreach (var definition in definitions)
        {
            var value = definition.Type switch
            {
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
        private SandboxValue? _sandboxValue;

        public string Name => definition.Name;
        public LiveSettingDefinition Definition => definition;

        public object? CurrentValue
        {
            get
            {
                lock (_gate)
                {
                    return _value;
                }
            }
        }

        public SandboxValue ToSandboxValue()
        {
            lock (_gate)
            {
                return _sandboxValue ??= LiveSettingTypeConverter.ToSandboxValue(definition.Type, _value);
            }
        }

        public void SetObject(object? value)
            => ApplyCoerced(Coerce(value));

        public object? Coerce(object? value)
        {
            var coerced = definition.Type switch
            {
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
            lock (_gate)
            {
                _value = coercedValue;
                _sandboxValue = null;
            }
        }
    }
}
