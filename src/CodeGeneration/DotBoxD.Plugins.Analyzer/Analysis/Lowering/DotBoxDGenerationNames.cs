namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDGenerationNames
{
    public const int GeneratedSpanLine = 1;
    public const int GeneratedSpanColumn = 1;
    public const string DefaultEventParameterName = "e";
    public const string DefaultContextParameterName = "ctx";
    public const string KernelSuffix = "Kernel";
    public const string PluginPackageSuffix = "PluginPackage";

    public static class KernelMethodParameters
    {
        public const int EventIndex = 0;
        public const int ContextIndex = 1;
    }

    public static class TypeNames
    {
        public const string GlobalPrefix = "global::";
        public const string GeneratedInterceptorsNamespace = "DotBoxD.Plugins.Generated";

        public const string PluginAttribute = "DotBoxD.Abstractions.PluginAttribute";
        public const string EventKernelAttribute = "DotBoxD.Abstractions.EventKernelAttribute";
        public const string LiveSettingAttribute = "DotBoxD.Abstractions.LiveSettingAttribute";
        public const string EventKernelInterface = "DotBoxD.Abstractions.IEventKernel<TEvent>";
        public const string RangeAttribute = "System.ComponentModel.DataAnnotations.RangeAttribute";
        public const string HostBindingAttribute = "DotBoxD.Abstractions.HostBindingAttribute";
        public const string CapabilityAttribute = "DotBoxD.Abstractions.CapabilityAttribute";
        public const string KernelMethodAttribute = "DotBoxD.Abstractions.KernelMethodAttribute";
        public const string NativeOnlyAttribute = "DotBoxD.Abstractions.NativeOnlyAttribute";
        public const string ServerExtensionAttribute = "DotBoxD.Abstractions.ServerExtensionAttribute";
        public const string ServerExtensionClientAttribute = "DotBoxD.Abstractions.ServerExtensionClientAttribute";
        public const string ServerExtensionMethodAttribute = "DotBoxD.Abstractions.ServerExtensionMethodAttribute";
        public const string GeneratePluginServerAttribute = "DotBoxD.Abstractions.GeneratePluginServerAttribute";
        public const string GeneratedKernelMethodDescriptorAttribute =
            "DotBoxD.Abstractions.GeneratedKernelMethodDescriptorAttribute";
        public const string PolymorphicHandleAttribute = "DotBoxD.Abstractions.PolymorphicHandleAttribute";
        public const string HandleSubtypeAttribute = "DotBoxD.Abstractions.HandleSubtypeAttribute";
        public const string RpcServiceAttribute = "DotBoxD.Services.Attributes.RpcServiceAttribute";
        public const string DotBoxDServiceAttribute = "DotBoxD.Services.Attributes.DotBoxDServiceAttribute";
        public const string HookContext = "DotBoxD.Abstractions.HookContext";
        public const string ServerInvocationDelegateType = "DotBoxD.Abstractions.RemoteServerInvocation";
        public const string ServerInvocationDelegateOriginal = ServerInvocationDelegateType + "<TWorld, TCaptures, TReturn>";
        public const string GameWorldAccessType = "DotBoxD.Kernels.Game.Server.Abstractions.IGameWorldAccess";
        public const string GameWorldMonsterSnapshotType = "DotBoxD.Kernels.Game.Server.Abstractions.MonsterSnapshot";
        public const string HookPipelineWithContextOriginal = "DotBoxD.Plugins.Runtime.HookPipeline<TEvent, TContext>";
        public const string HookStageWithContextOriginal = "DotBoxD.Plugins.Runtime.Hooks.HookStage<TEvent, TCurrent, TContext>";
        public const string RemoteHookPipelineOriginal = "DotBoxD.Plugins.Runtime.RemoteHookPipeline<TEvent>";
        public const string RemoteHookPipelineWithContextOriginal = "DotBoxD.Plugins.Runtime.RemoteHookPipeline<TEvent, TContext>";
        public const string RemoteHookStageOriginal = "DotBoxD.Plugins.Runtime.Hooks.RemoteHookStage<TEvent, TCurrent>";
        public const string RemoteHookStageWithContextOriginal = "DotBoxD.Plugins.Runtime.Hooks.RemoteHookStage<TEvent, TCurrent, TContext>";
        public const string SubscriptionPipelineWithContextOriginal = "DotBoxD.Plugins.Runtime.SubscriptionPipeline<TEvent, TContext>";
        public const string SubscriptionStageWithContextOriginal = "DotBoxD.Plugins.Runtime.Subscriptions.SubscriptionStage<TEvent, TCurrent, TContext>";
        public const string RemoteSubscriptionPipelineOriginal = "DotBoxD.Plugins.Runtime.RemoteSubscriptionPipeline<TEvent>";
        public const string RemoteSubscriptionPipelineWithContextOriginal = "DotBoxD.Plugins.Runtime.RemoteSubscriptionPipeline<TEvent, TContext>";
        public const string RemoteSubscriptionStageOriginal = "DotBoxD.Plugins.Runtime.Subscriptions.RemoteSubscriptionStage<TEvent, TCurrent>";
        public const string RemoteSubscriptionStageWithContextOriginal = "DotBoxD.Plugins.Runtime.Subscriptions.RemoteSubscriptionStage<TEvent, TCurrent, TContext>";

        public const string ListOriginal = "System.Collections.Generic.List<T>";
        public const string ReadOnlyListOriginal = "System.Collections.Generic.IReadOnlyList<T>";
        public const string ListInterfaceOriginal = "System.Collections.Generic.IList<T>";
        public const string EnumerableOriginal = "System.Collections.Generic.IEnumerable<T>";
        public const string ReadOnlyCollectionOriginal = "System.Collections.Generic.IReadOnlyCollection<T>";
        public const string DictionaryOriginal = "System.Collections.Generic.Dictionary<TKey, TValue>";
        public const string ReadOnlyDictionaryOriginal = "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
        public const string DictionaryInterfaceOriginal = "System.Collections.Generic.IDictionary<TKey, TValue>";
        public const string SystemActionPrefix = "System.Action";
        public const string SystemActivator = "System.Activator";
        public const string SystemEnvironment = "System.Environment";
        public const string SystemGc = "System.GC";
        public const string SystemDelegate = "System.Delegate";
        public const string SystemServiceProvider = "System.IServiceProvider";
        public const string SystemType = "System.Type";

        public const string GlobalArray = GlobalPrefix + "System.Array";
        public const string GlobalAttribute = GlobalPrefix + "System.Attribute";
        public const string GlobalAttributeTargets = GlobalPrefix + "System.AttributeTargets";
        public const string GlobalAttributeUsage = GlobalPrefix + "System.AttributeUsage";
        public const string GlobalAction = GlobalPrefix + "System.Action";
        public const string GlobalDictionary = GlobalPrefix + "System.Collections.Generic.Dictionary";
        public const string GlobalEnumerable = GlobalPrefix + "System.Linq.Enumerable";
        public const string GlobalFunc = GlobalPrefix + "System.Func";
        public const string GlobalInvalidOperationException = GlobalPrefix + "System.InvalidOperationException";
        public const string GlobalReadOnlyList = GlobalPrefix + "System.Collections.Generic.IReadOnlyList";
        public const string GlobalCancellationToken = GlobalPrefix + "System.Threading.CancellationToken";
        public const string GlobalValueTask = GlobalPrefix + "System.Threading.Tasks.ValueTask";

        public const string GlobalHookContext = GlobalPrefix + HookContext;
        public const string GlobalPluginPackage = GlobalPrefix + "DotBoxD.Plugins.PluginPackage";
        public const string GlobalPluginManifest = GlobalPrefix + "DotBoxD.Plugins.PluginManifest";
        public const string GlobalHookSubscriptionManifest = GlobalPrefix + "DotBoxD.Plugins.HookSubscriptionManifest";
        public const string GlobalIndexedPredicate = GlobalPrefix + "DotBoxD.Plugins.IndexedPredicate";
        public const string GlobalIndexPredicateOperator = GlobalPrefix + "DotBoxD.Plugins.IndexPredicateOperator";
        public const string GlobalLiveSettingDefinition = GlobalPrefix + "DotBoxD.Plugins.LiveSettingDefinition";
        public const string GlobalPluginPackageJsonSerializer = GlobalPrefix + "DotBoxD.Plugins.Json.PluginPackageJsonSerializer";
        public const string GlobalPluginMessageBindings = GlobalPrefix + "DotBoxD.Plugins.Runtime.PluginMessageBindings";
        public const string GlobalHookPipeline = GlobalPrefix + "DotBoxD.Plugins.Runtime.HookPipeline";

        public const string GlobalSandboxModule = GlobalPrefix + "DotBoxD.Kernels.SandboxModule";
        public const string GlobalSandboxFunction = GlobalPrefix + "DotBoxD.Kernels.SandboxFunction";
        public const string GlobalExecutionMode = GlobalPrefix + "DotBoxD.Kernels.ExecutionMode";
        public const string GlobalCapabilityRequest = GlobalPrefix + "DotBoxD.Kernels.CapabilityRequest";
        public const string GlobalParameter = GlobalPrefix + "DotBoxD.Kernels.Parameter";
        public const string GlobalExpression = GlobalPrefix + "DotBoxD.Kernels.Expression";
        public const string GlobalStatement = GlobalPrefix + "DotBoxD.Kernels.Statement";
        public const string GlobalIfStatement = GlobalPrefix + "DotBoxD.Kernels.IfStatement";
        public const string GlobalReturnStatement = GlobalPrefix + "DotBoxD.Kernels.ReturnStatement";
        public const string GlobalAssignmentStatement = GlobalPrefix + "DotBoxD.Kernels.AssignmentStatement";
        public const string GlobalVariableExpression = GlobalPrefix + "DotBoxD.Kernels.VariableExpression";
        public const string GlobalLiteralExpression = GlobalPrefix + "DotBoxD.Kernels.LiteralExpression";
        public const string GlobalCallExpression = GlobalPrefix + "DotBoxD.Kernels.CallExpression";
        public const string GlobalUnaryExpression = GlobalPrefix + "DotBoxD.Kernels.UnaryExpression";
        public const string GlobalBinaryExpression = GlobalPrefix + "DotBoxD.Kernels.BinaryExpression";
        public const string GlobalSourceSpan = GlobalPrefix + "DotBoxD.Kernels.Model.SourceSpan";
        public const string GlobalSemVersion = GlobalPrefix + "DotBoxD.Kernels.Model.SemVersion";
        public const string GlobalSandboxType = GlobalPrefix + "DotBoxD.Kernels.Sandbox.SandboxType";
        public const string GlobalSandboxValue = GlobalPrefix + "DotBoxD.Kernels.Sandbox.SandboxValue";
    }

    public static class Contracts
    {
        public const string EventKernelPrefix = "IEventKernel<";
        public const string EventKernelSuffix = ">";

        public static string EventKernel(string eventName) => EventKernelPrefix + eventName + EventKernelSuffix;
    }

    public static class CSharpTypes
    {
        public const string Bool = ManifestTypes.Bool;
        public const string Int = ManifestTypes.Int;
        public const string Long = ManifestTypes.Long;
        public const string Double = ManifestTypes.Double;
        public const string String = ManifestTypes.String;
    }

    public static class CSharpLiterals
    {
        public const string Null = "null";
        public const string True = "true";
        public const string False = "false";
        public const string Int32Default = "0";
        public const string Int64Default = "0L";
        public const string DoubleDefault = "0D";
        public const string StringDefault = "\"\"";
        public const string Int64Suffix = "L";
        public const string DoubleSuffix = "D";
        public const string DoubleRoundTripFormat = "R";
    }

    public static class CSharpIdentifiers
    {
        public const string EscapePrefix = "@";
    }

    public static class ManifestTypes
    {
        public const string Unit = "unit";
        public const string Bool = "bool";
        public const string Int = "int";
        public const string Long = "long";
        public const string Double = "double";
        public const string String = "string";

        // Non-scalar marshaller-eligible event-property / projection tags. These values are carried, never
        // operated on, by the lowered IR; the kernel parameter and projection return SandboxType is emitted in
        // full from the CLR type (see SandboxTypeSourceEmitter), so these tags only mark a value as a non-scalar
        // of a given shape for the expression lowerer and the chain gate. An enum is carried as its underlying
        // integer, so it reuses Int/Long rather than a distinct tag.
        public const string Guid = "guid";
        public const string List = "list";
        public const string Record = "record";
        public const string Map = "map";

        public const string Unsupported = "unsupported";

        public static bool IsNumeric(string type) => type is Int or Long or Double;
    }

    public static class RangeAttributeArguments
    {
        public const int NumericOverloadCount = 2;
        public const int TypeAndStringOverloadCount = 3;
        public const int NumericMinimumIndex = 0;
        public const int NumericMaximumIndex = 1;
        public const int ConversionTypeIndex = 0;
        public const int ConvertedMinimumIndex = 1;
        public const int ConvertedMaximumIndex = 2;
    }

    public static class Effects
    {
        public const string Cpu = "Cpu";
        public const string Alloc = "Alloc";
        public const string HostStateRead = "HostStateRead";
        public const string HostStateWrite = "HostStateWrite";
        public const string Concurrency = "Concurrency";
        public const string Audit = "Audit";
    }

    public static class Capabilities
    {
        public const string MessageWriteReason = "send host messages";
        public const string RuntimeAsync = "dotboxd.runtime.async";

        /// <summary>The capability a <c>ctx.Messages.Send</c> requires — kept in step with
        /// <c>DotBoxD.Plugins.Runtime.PluginMessageBindings.CapabilityId</c>.</summary>
        public const string MessageWrite = "host.message.write";
    }

    public static class Entrypoints
    {
        public const string ShouldHandle = "ShouldHandle";
        public const string Handle = "Handle";
    }

    public static class HookContext
    {
        public const string MessagesProperty = "Messages";
        public const string SendMethod = "Send";
        public const string SendTargetArgument = "targetId";
        public const string SendMessageArgument = "message";
        public const int SendTargetIndex = 0;
        public const int SendMessageIndex = 1;
    }

    public static class Helpers
    {
        public const string Var = "Var";
        public const string Str = "Str";
        public const string Int32ToStr = "Int32ToStr";
        public const string ConcatString = "ConcatString";
        public const string StringLength = "StringLength";
        public const string StringSubstring = "StringSubstring";
        public const string StringEquals = "StringEquals";
        public const string I32 = "I32";
        public const string I64 = "I64";
        public const string F64 = "F64";
        public const string Bool = "Bool";
        public const string Not = "Not";
        public const string Neg = "Neg";
        public const string Eq = "Eq";
        public const string Ne = "Ne";
        public const string Ge = "Ge";
        public const string Gt = "Gt";
        public const string Le = "Le";
        public const string Lt = "Lt";
        public const string And = "And";
        public const string Or = "Or";
        public const string Add = "Add";
        public const string Sub = "Sub";
        public const string Mul = "Mul";
        public const string Div = "Div";
        public const string Mod = "Mod";
    }

    public static class IrTypes
    {
        public const string IfStatement = TypeNames.GlobalIfStatement, ReturnStatement = TypeNames.GlobalReturnStatement;
    }

    public static class BindingIds
    {
        public const string Int32ToStringInvariant = "int32.toStringInvariant";
        public const string StringLength = "string.length";
        public const string StringSubstringBudgeted = "string.substringBudgeted";
        public const string StringConcatBudgeted = "string.concatBudgeted";
        public const string StringEquals = "string.equals";
    }

    public static class GeneratedVariables
    {
        public const string EventPrefix = "e_";
    }

    public static class ModuleMetadata
    {
        public const string PluginId = "pluginId", Kernel = "kernel";
        public const string RequiredCapabilities = "requiredCapabilities";
    }

    public static class Operators
    {
        public const string LogicalNot = "!";
        public const string Minus = "-";
        public const string EqualTo = "==";
        public const string NotEqualTo = "!=";
        public const string GreaterThanOrEqual = ">=";
        public const string GreaterThan = ">";
        public const string LessThanOrEqual = "<=";
        public const string LessThan = "<";
        public const string LogicalAnd = "&&";
        public const string LogicalOr = "||";
        public const string Add = "+";
        public const string Multiply = "*";
        public const string Divide = "/";
        public const string Modulo = "%";
    }
}
