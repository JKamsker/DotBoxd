extern alias GameServerAbstractions;
extern alias GameServerPlugin;

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;
using DotBoxD.Services.Attributes;
using GameServerAbstractions::DotBoxD.Kernels.Game.Server.Abstractions;
using HookContractNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDHookContractNames;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class PluginAnalyzerTypeNameContractTests
{
    [Fact]
    public void Type_name_constants_match_referenced_contracts()
    {
        var expected = ExpectedTypeNames();
        var actual = typeof(TypeNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

        Assert.Equal(expected.Keys.OrderBy(static key => key), actual.Keys.OrderBy(static key => key));
        foreach (var (name, expectedValue) in expected)
        {
            Assert.Equal(expectedValue, actual[name]);
        }
    }

    [Fact]
    public void Hook_contract_name_constants_match_referenced_contracts()
    {
        Assert.Equal(HookContractNames.HookAttribute, TypeName(typeof(HookAttribute)));
        Assert.Equal(HookContractNames.HookResultAttribute, TypeName(typeof(HookResultAttribute)));
    }

    private static Dictionary<string, string> ExpectedTypeNames()
        => new(StringComparer.Ordinal)
        {
            [nameof(TypeNames.GlobalPrefix)] = "global::",
            [nameof(TypeNames.GeneratedInterceptorsNamespace)] = "DotBoxD.Plugins.Generated",

            [nameof(TypeNames.PluginAttribute)] = TypeName(typeof(PluginAttribute)),
            [nameof(TypeNames.EventKernelAttribute)] = TypeName(typeof(EventKernelAttribute)),
            [nameof(TypeNames.LiveSettingAttribute)] = TypeName(typeof(LiveSettingAttribute)),
            [nameof(TypeNames.EventKernelInterface)] = OriginalTypeName(typeof(IEventKernel<>), "TEvent"),
            [nameof(TypeNames.RangeAttribute)] = TypeName(typeof(RangeAttribute)),
            [nameof(TypeNames.HostBindingAttribute)] = TypeName(typeof(HostBindingAttribute)),
            [nameof(TypeNames.CapabilityAttribute)] = TypeName(typeof(CapabilityAttribute)),
            [nameof(TypeNames.KernelMethodAttribute)] = TypeName(typeof(KernelMethodAttribute)),
            [nameof(TypeNames.LowerToIrAttribute)] = TypeName(typeof(LowerToIrAttribute)),
            [nameof(TypeNames.PipelineStepAttribute)] = TypeName(typeof(PipelineStepAttribute)),
            [nameof(TypeNames.PipelineSurfaceAttribute)] = TypeName(typeof(PipelineSurfaceAttribute)),
            [nameof(TypeNames.LocalAttribute)] = TypeName(typeof(LocalAttribute)),
            [nameof(TypeNames.ServerExtensionAttribute)] = TypeName(typeof(ServerExtensionAttribute)),
            [nameof(TypeNames.ServerExtensionClientAttribute)] = TypeName(typeof(ServerExtensionClientAttribute)),
            [nameof(TypeNames.ServerExtensionMethodAttribute)] = TypeName(typeof(ServerExtensionMethodAttribute)),
            [nameof(TypeNames.GeneratePluginServerAttribute)] = TypeName(typeof(GeneratePluginServerAttribute)),
            [nameof(TypeNames.GeneratedKernelMethodDescriptorAttribute)] =
                TypeName(typeof(GeneratedKernelMethodDescriptorAttribute)),
            [nameof(TypeNames.PolymorphicHandleAttribute)] = TypeName(typeof(PolymorphicHandleAttribute)),
            [nameof(TypeNames.HandleSubtypeAttribute)] = TypeName(typeof(HandleSubtypeAttribute)),
            [nameof(TypeNames.DotBoxDServiceAttribute)] = TypeName(typeof(DotBoxDServiceAttribute)),
            [nameof(TypeNames.HookContext)] = TypeName(typeof(HookContext)),
            [nameof(TypeNames.ServerInvocationDelegateType)] = RemoteServerInvocationTypeName(),
            [nameof(TypeNames.ServerInvocationDelegateOriginal)] = RemoteServerInvocationOriginalName(),
            [nameof(TypeNames.GameWorldAccessType)] = TypeName(typeof(IGameWorldAccess)),
            [nameof(TypeNames.GameWorldMonsterSnapshotType)] = TypeName(typeof(MonsterSnapshot)),
            [nameof(TypeNames.HookPipelineWithContextOriginal)] = OriginalTypeName(typeof(HookPipeline<,>), "TEvent", "TContext"),
            [nameof(TypeNames.HookStageWithContextOriginal)] = OriginalTypeName(typeof(HookStage<,,>), "TEvent", "TCurrent", "TContext"),
            [nameof(TypeNames.RemoteHookPipelineOriginal)] = OriginalTypeName(typeof(RemoteHookPipeline<>), "TEvent"),
            [nameof(TypeNames.RemoteHookPipelineWithContextOriginal)] = OriginalTypeName(typeof(RemoteHookPipeline<,>), "TEvent", "TContext"),
            [nameof(TypeNames.RemoteHookStageOriginal)] = OriginalTypeName(typeof(RemoteHookStage<,>), "TEvent", "TCurrent"),
            [nameof(TypeNames.RemoteHookStageWithContextOriginal)] = OriginalTypeName(typeof(RemoteHookStage<,,>), "TEvent", "TCurrent", "TContext"),
            [nameof(TypeNames.SubscriptionPipelineWithContextOriginal)] = OriginalTypeName(typeof(SubscriptionPipeline<,>), "TEvent", "TContext"),
            [nameof(TypeNames.SubscriptionStageWithContextOriginal)] = OriginalTypeName(typeof(SubscriptionStage<,,>), "TEvent", "TCurrent", "TContext"),
            [nameof(TypeNames.RemoteSubscriptionPipelineOriginal)] = OriginalTypeName(typeof(RemoteSubscriptionPipeline<>), "TEvent"),
            [nameof(TypeNames.RemoteSubscriptionPipelineWithContextOriginal)] = OriginalTypeName(typeof(RemoteSubscriptionPipeline<,>), "TEvent", "TContext"),
            [nameof(TypeNames.RemoteSubscriptionStageOriginal)] = OriginalTypeName(typeof(RemoteSubscriptionStage<,>), "TEvent", "TCurrent"),
            [nameof(TypeNames.RemoteSubscriptionStageWithContextOriginal)] = OriginalTypeName(typeof(RemoteSubscriptionStage<,,>), "TEvent", "TCurrent", "TContext"),

            [nameof(TypeNames.ListOriginal)] = OriginalTypeName(typeof(List<>), "T"),
            [nameof(TypeNames.ReadOnlyListOriginal)] = OriginalTypeName(typeof(IReadOnlyList<>), "T"),
            [nameof(TypeNames.ListInterfaceOriginal)] = OriginalTypeName(typeof(IList<>), "T"),
            [nameof(TypeNames.EnumerableOriginal)] = OriginalTypeName(typeof(IEnumerable<>), "T"),
            [nameof(TypeNames.ReadOnlyCollectionOriginal)] = OriginalTypeName(typeof(IReadOnlyCollection<>), "T"),
            [nameof(TypeNames.DictionaryOriginal)] = OriginalTypeName(typeof(Dictionary<,>), "TKey", "TValue"),
            [nameof(TypeNames.ReadOnlyDictionaryOriginal)] = OriginalTypeName(typeof(IReadOnlyDictionary<,>), "TKey", "TValue"),
            [nameof(TypeNames.DictionaryInterfaceOriginal)] = OriginalTypeName(typeof(IDictionary<,>), "TKey", "TValue"),
            [nameof(TypeNames.SystemActionPrefix)] = TypeName(typeof(Action)),
            [nameof(TypeNames.SystemActivator)] = TypeName(typeof(Activator)),
            [nameof(TypeNames.SystemEnvironment)] = TypeName(typeof(Environment)),
            [nameof(TypeNames.SystemGc)] = TypeName(typeof(GC)),
            [nameof(TypeNames.SystemDelegate)] = TypeName(typeof(Delegate)),
            [nameof(TypeNames.SystemServiceProvider)] = TypeName(typeof(IServiceProvider)),
            [nameof(TypeNames.SystemType)] = TypeName(typeof(Type)),

            [nameof(TypeNames.GlobalArray)] = GlobalTypeName(typeof(Array)),
            [nameof(TypeNames.GlobalAttribute)] = GlobalTypeName(typeof(Attribute)),
            [nameof(TypeNames.GlobalAttributeTargets)] = GlobalTypeName(typeof(AttributeTargets)),
            [nameof(TypeNames.GlobalAttributeUsage)] = GlobalAttributeName(typeof(AttributeUsageAttribute)),
            [nameof(TypeNames.GlobalAction)] = GlobalTypeName(typeof(Action<,>)),
            [nameof(TypeNames.GlobalDictionary)] = GlobalTypeName(typeof(Dictionary<,>)),
            [nameof(TypeNames.GlobalEnumerable)] = GlobalTypeName(typeof(Enumerable)),
            [nameof(TypeNames.GlobalFunc)] = GlobalTypeName(typeof(Func<,,>)),
            [nameof(TypeNames.GlobalInvalidOperationException)] = GlobalTypeName(typeof(InvalidOperationException)),
            [nameof(TypeNames.GlobalReadOnlyList)] = GlobalTypeName(typeof(IReadOnlyList<>)),
            [nameof(TypeNames.GlobalCancellationToken)] = GlobalTypeName(typeof(CancellationToken)),
            [nameof(TypeNames.GlobalValueTask)] = GlobalTypeName(typeof(ValueTask)),

            [nameof(TypeNames.GlobalHookContext)] = GlobalTypeName(typeof(HookContext)),
            [nameof(TypeNames.GlobalPluginPackage)] = GlobalTypeName(typeof(PluginPackage)),
            [nameof(TypeNames.GlobalPluginManifest)] = GlobalTypeName(typeof(PluginManifest)),
            [nameof(TypeNames.GlobalHookSubscriptionManifest)] = GlobalTypeName(typeof(HookSubscriptionManifest)),
            [nameof(TypeNames.GlobalIndexedPredicate)] = GlobalTypeName(typeof(IndexedPredicate)),
            [nameof(TypeNames.GlobalIndexPredicateOperator)] = GlobalTypeName(typeof(IndexPredicateOperator)),
            [nameof(TypeNames.GlobalLiveSettingDefinition)] = GlobalTypeName(typeof(LiveSettingDefinition)),
            [nameof(TypeNames.GlobalPluginPackageJsonSerializer)] = GlobalTypeName(typeof(PluginPackageJsonSerializer)),
            [nameof(TypeNames.GlobalPluginMessageBindings)] = GlobalTypeName(typeof(PluginMessageBindings)),
            [nameof(TypeNames.GlobalHookPipeline)] = GlobalTypeName(typeof(HookPipeline<,>)),

            [nameof(TypeNames.GlobalSandboxModule)] = GlobalTypeName(typeof(SandboxModule)),
            [nameof(TypeNames.GlobalSandboxFunction)] = GlobalTypeName(typeof(SandboxFunction)),
            [nameof(TypeNames.GlobalExecutionMode)] = GlobalTypeName(typeof(ExecutionMode)),
            [nameof(TypeNames.GlobalCapabilityRequest)] = GlobalTypeName(typeof(CapabilityRequest)),
            [nameof(TypeNames.GlobalParameter)] = GlobalTypeName(typeof(Parameter)),
            [nameof(TypeNames.GlobalExpression)] = GlobalTypeName(typeof(Expression)),
            [nameof(TypeNames.GlobalStatement)] = GlobalTypeName(typeof(Statement)),
            [nameof(TypeNames.GlobalIfStatement)] = GlobalTypeName(typeof(IfStatement)),
            [nameof(TypeNames.GlobalReturnStatement)] = GlobalTypeName(typeof(ReturnStatement)),
            [nameof(TypeNames.GlobalAssignmentStatement)] = GlobalTypeName(typeof(AssignmentStatement)),
            [nameof(TypeNames.GlobalVariableExpression)] = GlobalTypeName(typeof(VariableExpression)),
            [nameof(TypeNames.GlobalLiteralExpression)] = GlobalTypeName(typeof(LiteralExpression)),
            [nameof(TypeNames.GlobalCallExpression)] = GlobalTypeName(typeof(CallExpression)),
            [nameof(TypeNames.GlobalUnaryExpression)] = GlobalTypeName(typeof(UnaryExpression)),
            [nameof(TypeNames.GlobalBinaryExpression)] = GlobalTypeName(typeof(BinaryExpression)),
            [nameof(TypeNames.GlobalSourceSpan)] = GlobalTypeName(typeof(SourceSpan)),
            [nameof(TypeNames.GlobalSemVersion)] = GlobalTypeName(typeof(SemVersion)),
            [nameof(TypeNames.GlobalSandboxType)] = GlobalTypeName(typeof(SandboxType)),
            [nameof(TypeNames.GlobalSandboxValue)] = GlobalTypeName(typeof(SandboxValue)),
        };

    private static string RemoteServerInvocationTypeName()
        => TypeName(typeof(RemoteServerInvocation<,,>));

    private static string RemoteServerInvocationOriginalName()
        => RemoteServerInvocationTypeName() + "<TWorld, TCaptures, TReturn>";

    private static string GlobalTypeName(Type type) => TypeNames.GlobalPrefix + TypeName(type);

    private static string GlobalAttributeName(Type type) => TypeNames.GlobalPrefix + AttributeName(type);

    private static string AttributeName(Type type)
    {
        var typeName = TypeName(type);
        return typeName.EndsWith(nameof(Attribute), StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - nameof(Attribute).Length)
            : typeName;
    }

    private static string OriginalTypeName(Type openGenericType, params string[] typeParameters)
        => TypeName(openGenericType) + "<" + string.Join(", ", typeParameters) + ">";

    private static string TypeName(Type type)
    {
        var name = type.Name;
        var genericMarker = name.IndexOf('`');
        if (genericMarker >= 0)
        {
            name = name.Substring(0, genericMarker);
        }

        return string.IsNullOrEmpty(type.Namespace) ? name : type.Namespace + "." + name;
    }
}
