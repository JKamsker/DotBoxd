using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginParameterShape
{
    public static Parameter[] BuildExpected(
        IReadOnlyList<Parameter> eventParameters,
        IReadOnlyList<LiveSettingDefinition> liveSettings)
    {
        var expected = new Parameter[eventParameters.Count + liveSettings.Count];
        for (var i = 0; i < eventParameters.Count; i++)
        {
            expected[i] = eventParameters[i];
        }

        for (var i = 0; i < liveSettings.Count; i++)
        {
            var setting = liveSettings[i];
            expected[eventParameters.Count + i] = new Parameter(
                setting.Name,
                LiveSettingTypeConverter.ToSandboxType(setting.Type));
        }

        return expected;
    }

    public static bool Matches(IReadOnlyList<Parameter> first, IReadOnlyList<Parameter> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var i = 0; i < first.Count; i++)
        {
            if (!string.Equals(first[i].Name, second[i].Name, StringComparison.Ordinal) ||
                first[i].Type != second[i].Type)
            {
                return false;
            }
        }

        return true;
    }
}
