namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal sealed record PluginServerFacadeModel(
    string Namespace,
    string Accessibility,
    string ClassName,
    string ServerInterfaceName,
    string SetupInterfaceName,
    string ContextNamespace,
    string ContextAccessibility,
    string ContextName,
    string ContextFullName,
    string? ContextFactoryMethodName,
    EquatableArray<GeneratedKernelMethodDescriptorModel> KernelMethodDescriptors,
    string HookRegistryName,
    string SubscriptionRegistryName,
    string WorldType,
    string WorldExtensionSuffix,
    string WorldDocumentation,
    string ControlServiceType,
    string LiveSettingUpdateType,
    EquatableArray<PluginServerForwardedMethod> WorldMethods,
    EquatableArray<PluginServerControlProperty> Controls,
    // Reverse server->plugin event-callback contract for remote RunLocal chains, discovered by the
    // {worldNs}.Ipc.IPluginEventCallback convention (null when the world declares none). When set, the facade
    // owns a RemoteLocalHandlerRegistry, threads it into the hook/subscription registries, and provides a
    // generated sink on the peer so the server can push filtered+projected values back to native RunLocal
    // delegates. EventCallbackProvideSuffix is the generated DotBoxDGeneratedExtensions.Provide{Suffix} name.
    string? EventCallbackType = null,
    string? EventCallbackProvideSuffix = null,
    string? EventCallbackReturnType = null,
    bool EventCallbackReturnHasValue = false);

internal sealed record PluginServerControlProperty(
    string Name,
    string Type,
    string Documentation,
    string WrapperName,
    string AccumulatorInterfaceName,
    EquatableArray<PluginServerForwardedMethod> Methods,
    EquatableArray<PluginServerServiceWrapper> ServiceWrappers);

internal sealed record PluginServerForwardedMethod(
    string Name,
    string ReturnType,
    string Documentation,
    string? ReturnWrapperName,
    PluginServerReturnWrapperKind ReturnWrapperKind,
    EquatableArray<PluginServerParameter> Parameters);

internal sealed record PluginServerForwardedProperty(string Name, string Type, string Documentation);

internal sealed record PluginServerServiceWrapper(
    string Type,
    string WrapperName,
    string Documentation,
    EquatableArray<PluginServerForwardedProperty> Properties,
    EquatableArray<PluginServerForwardedMethod> Methods);

internal sealed record PluginServerParameter(string Name, string Type);

internal sealed record GeneratedKernelMethodDescriptorModel(
    string ContextType,
    string MethodMetadataName,
    string NormalizedSignature,
    string DescriptorHash,
    string DescriptorPayload);

internal sealed record PluginServerFacadeResult(
    GeneratedPluginPackage? Source,
    PluginKernelDiagnostic? Diagnostic);

internal enum PluginServerReturnWrapperKind
{
    None,
    Sync,
    Task,
    ValueTask,
}
