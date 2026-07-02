namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDMetadataNames
{
    public const string PluginAttribute = DotBoxDGenerationNames.TypeNames.PluginAttribute;
    public const string EventKernelAttribute = DotBoxDGenerationNames.TypeNames.EventKernelAttribute;
    public const string LiveSettingAttribute = DotBoxDGenerationNames.TypeNames.LiveSettingAttribute;
    public const string EventKernelInterface = DotBoxDGenerationNames.TypeNames.EventKernelInterface;
    public const string RangeAttribute = DotBoxDGenerationNames.TypeNames.RangeAttribute;
    public const string HostBindingAttribute = DotBoxDGenerationNames.TypeNames.HostBindingAttribute;
    public const string CapabilityAttribute = DotBoxDGenerationNames.TypeNames.CapabilityAttribute;
    public const string KernelMethodAttribute = DotBoxDGenerationNames.TypeNames.KernelMethodAttribute;
    public const string NativeOnlyAttribute = DotBoxDGenerationNames.TypeNames.NativeOnlyAttribute;
    public const string ServerExtensionAttribute = DotBoxDGenerationNames.TypeNames.ServerExtensionAttribute;
    public const string ServerExtensionClientAttribute = DotBoxDGenerationNames.TypeNames.ServerExtensionClientAttribute;
    public const string ServerExtensionMethodAttribute = DotBoxDGenerationNames.TypeNames.ServerExtensionMethodAttribute;
    public const string GeneratePluginServerAttribute = DotBoxDGenerationNames.TypeNames.GeneratePluginServerAttribute;
    public const string GeneratedKernelMethodDescriptorAttribute =
        DotBoxDGenerationNames.TypeNames.GeneratedKernelMethodDescriptorAttribute;
    public const string HookAttribute = DotBoxDHookContractNames.HookAttribute;
    public const string HookResultAttribute = DotBoxDHookContractNames.HookResultAttribute;
    public const string PolymorphicHandleAttribute = DotBoxDGenerationNames.TypeNames.PolymorphicHandleAttribute;
    public const string HandleSubtypeAttribute = DotBoxDGenerationNames.TypeNames.HandleSubtypeAttribute;
    public const string RpcServiceAttribute = DotBoxDGenerationNames.TypeNames.RpcServiceAttribute;
    public const string DotBoxDServiceAttribute = DotBoxDGenerationNames.TypeNames.DotBoxDServiceAttribute;
    public const string HookContextType = DotBoxDGenerationNames.TypeNames.HookContext;
    public const string ServerInvocationDelegateType = DotBoxDGenerationNames.TypeNames.ServerInvocationDelegateType;
    public const string ServerInvocationDelegateOriginal = DotBoxDGenerationNames.TypeNames.ServerInvocationDelegateOriginal;
    public const string GameWorldAccessType = DotBoxDGenerationNames.TypeNames.GameWorldAccessType;
    public const string GameWorldMonsterSnapshotType = DotBoxDGenerationNames.TypeNames.GameWorldMonsterSnapshotType;

    public static bool IsRpcServiceAttribute(string? typeName) =>
        typeName is RpcServiceAttribute or DotBoxDServiceAttribute;
}
