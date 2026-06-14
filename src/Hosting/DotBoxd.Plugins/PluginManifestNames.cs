namespace DotBoxd.Plugins;

internal static class PluginManifestNames
{
    public static class LiveSettingTypes
    {
        public const string Bool = "bool";
        public const string Int = "int";
        public const string Long = "long";
        public const string Double = "double";
        public const string String = "string";

        public static bool IsNumeric(string type)
            => string.Equals(type, Int, StringComparison.Ordinal) ||
               string.Equals(type, Long, StringComparison.Ordinal) ||
               string.Equals(type, Double, StringComparison.Ordinal);
    }

    public static class ModuleMetadata
    {
        public const string PluginId = "pluginId";
        public const string Kernel = "kernel";
    }

    public static class Entrypoints
    {
        public const string ShouldHandle = "ShouldHandle";
        public const string Handle = "Handle";
    }

    public static class EventKernelContract
    {
        public const string Prefix = "IEventKernel<";
        public const string Empty = "IEventKernel<>";
        public const string Suffix = ">";
        public const int SuffixLength = 1;
    }

    public static class EventParameters
    {
        public const string Prefix = "e_";
    }
}
