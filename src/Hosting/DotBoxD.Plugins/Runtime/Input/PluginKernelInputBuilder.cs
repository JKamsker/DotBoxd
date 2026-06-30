using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Plugins.Runtime.Input;

internal static class PluginKernelInputBuilder
{
    public static SandboxValue Build<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<Action> deferredUpdates,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        Action<Action> enqueueUpdate)
    {
        var input = adapter is IPluginEventValueWriter<TEvent> writer
            ? Build(writer, e, parameters, liveSettings, value)
            : Build(adapter, e, parameters, liveSettings, value);

        foreach (var update in deferredUpdates)
        {
            enqueueUpdate(update);
        }

        return input;
    }

    public static SandboxValue BuildWithReusableBuffer<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<Action> deferredUpdates,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        Action<Action> enqueueUpdate,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var input = adapter is IPluginEventValueWriter<TEvent> writer
            ? BuildWithReusableBuffer(writer, e, parameters, liveSettings, value, ref buffer, ref list)
            : BuildWithReusableBuffer(adapter, e, parameters, liveSettings, value, ref buffer, ref list);

        foreach (var update in deferredUpdates)
        {
            enqueueUpdate(update);
        }

        return input;
    }

    private static SandboxValue Build(
        IReadOnlyList<SandboxValue> eventValues,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var valueCount = eventValues.Count + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValues.Count == 1 ? eventValues[0] : value.ToSandboxValue(liveSettings[0]),
            _ => BuildList(eventValues, liveSettings, value)
        };
    }

    private static SandboxValue Build<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var eventValues = adapter.ToSandboxValues(e);
        PluginEventAdapterValueValidator.ValidateValues(parameters, eventValues);
        return Build(eventValues, liveSettings, value);
    }

    private static SandboxValue Build<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var eventValueCount = writer.EventValueCount;
        var valueCount = eventValueCount + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValueCount == 1 ? ReadWriterValue(writer, e, parameters, 0) : value.ToSandboxValue(liveSettings[0]),
            _ => BuildList(writer, e, parameters, liveSettings, value)
        };
    }

    private static SandboxValue BuildList(
        IReadOnlyList<SandboxValue> eventValues,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        if (liveSettings.Count == 0)
        {
            return SandboxValue.FromList(eventValues, eventValues[0].Type);
        }

        var values = new SandboxValue[eventValues.Count + liveSettings.Count];
        for (var i = 0; i < eventValues.Count; i++)
        {
            values[i] = eventValues[i];
        }

        value.CopySandboxValues(liveSettings, values, eventValues.Count);
        return SandboxValue.FromOwnedList(values, values[0].Type);
    }

    private static SandboxValue BuildList<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var eventValueCount = writer.EventValueCount;
        var values = new SandboxValue[eventValueCount + liveSettings.Count];
        writer.CopySandboxValues(e, values, 0);
        PluginEventAdapterValueValidator.ValidateCopiedValues(parameters, eventValueCount, values, 0);
        if (liveSettings.Count > 0)
        {
            value.CopySandboxValues(liveSettings, values, eventValueCount);
        }

        return SandboxValue.FromOwnedList(values, values[0].Type);
    }

    private static SandboxValue BuildWithReusableBuffer(
        IReadOnlyList<SandboxValue> eventValues,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var valueCount = eventValues.Count + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValues.Count == 1 ? eventValues[0] : value.ToSandboxValue(liveSettings[0]),
            _ => BuildListWithReusableBuffer(eventValues, liveSettings, value, ref buffer, ref list)
        };
    }

    private static SandboxValue BuildWithReusableBuffer<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var eventValues = adapter.ToSandboxValues(e);
        PluginEventAdapterValueValidator.ValidateValues(parameters, eventValues);
        return BuildWithReusableBuffer(eventValues, liveSettings, value, ref buffer, ref list);
    }

    private static SandboxValue BuildWithReusableBuffer<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var eventValueCount = writer.EventValueCount;
        var valueCount = eventValueCount + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValueCount == 1 ? ReadWriterValue(writer, e, parameters, 0) : value.ToSandboxValue(liveSettings[0]),
            _ => BuildListWithReusableBuffer(writer, e, parameters, liveSettings, value, ref buffer, ref list)
        };
    }

    private static SandboxValue BuildListWithReusableBuffer(
        IReadOnlyList<SandboxValue> eventValues,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var values = RentBuffer(eventValues.Count + liveSettings.Count, ref buffer);
        for (var i = 0; i < eventValues.Count; i++)
        {
            values[i] = eventValues[i];
        }

        value.CopySandboxValues(liveSettings, values, eventValues.Count);
        return ReusableList(values, values[0].Type, ref list);
    }

    private static SandboxValue BuildListWithReusableBuffer<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        ref SandboxValue[]? buffer,
        ref ListValue? list)
    {
        var eventValueCount = writer.EventValueCount;
        var values = RentBuffer(eventValueCount + liveSettings.Count, ref buffer);
        writer.CopySandboxValues(e, values, 0);
        PluginEventAdapterValueValidator.ValidateCopiedValues(parameters, eventValueCount, values, 0);
        if (liveSettings.Count > 0)
        {
            value.CopySandboxValues(liveSettings, values, eventValueCount);
        }

        return ReusableList(values, values[0].Type, ref list);
    }

    private static SandboxValue[] RentBuffer(int valueCount, ref SandboxValue[]? buffer)
        => buffer is { Length: var length } values && length == valueCount
            ? values
            : buffer = new SandboxValue[valueCount];

    private static SandboxValue ReadWriterValue<TEvent>(
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<Parameter> parameters,
        int index)
    {
        var eventValue = writer.ToSandboxValue(e, index);
        PluginEventAdapterValueValidator.ValidateValue(parameters, writer.EventValueCount, index, eventValue);
        return eventValue;
    }

    private static ListValue ReusableList(
        SandboxValue[] values,
        SandboxType itemType,
        ref ListValue? list)
    {
        if (list is null || list.Count != values.Length || !list.ItemType.Equals(itemType))
        {
            list = (ListValue)SandboxValue.FromOwnedList(values, itemType);
            return list;
        }

        list.ResetOwnedValues(values);
        return list;
    }
}
