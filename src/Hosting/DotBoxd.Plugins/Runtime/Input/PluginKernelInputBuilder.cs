namespace DotBoxd.Plugins;

using DotBoxd.Kernels;

internal static class PluginKernelInputBuilder
{
    public static SandboxValue Build<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        TEvent e,
        IReadOnlyList<Action> deferredUpdates,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value,
        Action<Action> enqueueUpdate)
    {
        var input = adapter is IPluginEventValueWriter<TEvent> writer
            ? Build(writer, e, liveSettings, value)
            : Build(adapter.ToSandboxValues(e), liveSettings, value);

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
        IPluginEventValueWriter<TEvent> writer,
        TEvent e,
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var eventValueCount = writer.EventValueCount;
        var valueCount = eventValueCount + liveSettings.Count;
        return valueCount switch
        {
            0 => SandboxValue.Unit,
            1 => eventValueCount == 1 ? writer.ToSandboxValue(e, 0) : value.ToSandboxValue(liveSettings[0]),
            _ => BuildList(writer, e, liveSettings, value)
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
        IReadOnlyList<LiveSettingDefinition> liveSettings,
        LiveSettingStore value)
    {
        var eventValueCount = writer.EventValueCount;
        var values = new SandboxValue[eventValueCount + liveSettings.Count];
        writer.CopySandboxValues(e, values, 0);
        if (liveSettings.Count > 0)
        {
            value.CopySandboxValues(liveSettings, values, eventValueCount);
        }

        return SandboxValue.FromOwnedList(values, values[0].Type);
    }
}
