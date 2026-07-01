namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Names of the <c>DotBoxD.Plugins.KernelRpcValue</c> transport family that the InvokeAsync and RPC
/// payload emitters bake into generated code. The analyzer targets netstandard2.0 and cannot reference
/// <c>DotBoxD.Plugins</c>, so these are unbindable string literals. <c>DotBoxDRpcValueNameContractTests</c>
/// pins every entry to the real runtime symbol, turning a rename or namespace move red at build time
/// instead of letting the emitted code silently fail to compile in consumer projects.
/// </summary>
internal static class DotBoxDRpcValueNames
{
    public const string GlobalPrefix = "global::";

    public const string GlobalKernelRpcValue = GlobalPrefix + "DotBoxD.Plugins.KernelRpcValue";
    public const string GlobalKernelRpcValueKind = GlobalPrefix + "DotBoxD.Plugins.KernelRpcValueKind";
    public const string GlobalKernelRpcBinaryCodec = GlobalPrefix + "DotBoxD.Plugins.KernelRpcBinaryCodec";

    /// <summary>Members of <c>DotBoxD.Plugins.KernelRpcValue</c> (factory methods, readers, accessors).</summary>
    public static class KernelRpcValue
    {
        public const string Unit = "Unit";
        public const string Bool = "Bool";
        public const string Int32 = "Int32";
        public const string Int64 = "Int64";
        public const string Double = "Double";
        public const string String = "String";
        public const string Guid = "Guid";
        public const string List = "List";
        public const string Record = "Record";
        public const string Map = "Map";
        public const string GetItem = "GetItem";
        public const string RequireKind = "RequireKind";
        public const string Kind = "Kind";
        public const string ItemCount = "ItemCount";
        public const string BoolValue = "BoolValue";
        public const string Int32Value = "Int32Value";
        public const string Int64Value = "Int64Value";
        public const string DoubleValue = "DoubleValue";
        public const string TextValue = "TextValue";
        public const string GuidValue = "GuidValue";
    }

    /// <summary>Members of the <c>DotBoxD.Plugins.KernelRpcValueKind</c> enum the emitters reference.</summary>
    public static class KernelRpcValueKind
    {
        public const string Unit = "Unit";
        public const string List = "List";
        public const string Record = "Record";
        public const string Map = "Map";
    }

    /// <summary>Members of <c>DotBoxD.Plugins.KernelRpcBinaryCodec</c> the client proxies call.</summary>
    public static class KernelRpcBinaryCodec
    {
        public const string EncodeArguments = "EncodeArguments";
        public const string DecodeValue = "DecodeValue";
    }
}
