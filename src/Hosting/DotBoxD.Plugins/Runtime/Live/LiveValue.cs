using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Plugins.Runtime;

public interface ILiveSetting
{
    string Name { get; }
    LiveSettingDefinition Definition { get; }
    object? CurrentValue { get; }
    SandboxValue ToSandboxValue();
    void SetObject(object? value);
}

// Splits validation from storage so a value is coerced and range-validated exactly once
// per update.
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
        get
        {
            lock (_gate)
            {
                return _value;
            }
        }
        set
        {
            lock (_gate)
            {
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
