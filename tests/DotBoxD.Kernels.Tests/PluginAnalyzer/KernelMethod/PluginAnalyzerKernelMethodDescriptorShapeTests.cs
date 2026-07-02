using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    private const string CallExpression = "global::DotBoxD.Kernels.CallExpression";

    [Fact]
    public void Prebuilt_sdk_descriptor_accepts_list_return_intrinsic_metadata()
    {
        var source =
            $"Gt(new {CallExpression}(\"list.count\", " +
            $"[new {CallExpression}(\"host.Sdk.IGameWorld.Tags\", " +
            "[__dotboxd_kernel_method_arg_0__], null, Span)], null, Span), I32(1))";
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "HasTags",
                "bool HasTags(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: ["sample.read.tags"],
                effects: ["Alloc", "Cpu", "HostStateRead"],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                source,
                "[HostBinding(\"sample.read.tags\", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]\n" +
                    "        System.Collections.Generic.List<string> Tags(string id);",
                "public bool HasTags(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedListKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.HasTags(e.MonsterId)"), sdkReference);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains("stale", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_stale_list_count_receiver_metadata()
    {
        var source =
            $"Gt(new {CallExpression}(\"list.count\", [__dotboxd_kernel_method_arg_0__], null, Span), I32(1))";
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "HasTags",
                "bool HasTags(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: false,
                capabilities: [],
                effects: [],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                source,
                worldMembers: string.Empty,
                "public bool HasTags(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedBadListCountKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.HasTags(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale list.count metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_nonconstant_record_get_index()
    {
        var recordType = "global::DotBoxD.Kernels.Sandbox.SandboxType.Record(" +
            "new global::DotBoxD.Kernels.Sandbox.SandboxType[] { " +
            "global::DotBoxD.Kernels.Sandbox.SandboxType.String })";
        var source =
            $"StringEquals(new {CallExpression}(\"record.get\", " +
            $"[new {CallExpression}(\"record.new\", [__dotboxd_kernel_method_arg_0__], {recordType}, Span), " +
            "__dotboxd_kernel_method_arg_1__], null, Span), __dotboxd_kernel_method_arg_0__)";
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Matches",
                "bool Matches(string,int)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: [],
                effects: ["Alloc"],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String),
                    new("__dotboxd_kernel_method_arg_1__", DotBoxDGenerationNames.ManifestTypes.Int)
                ],
                source,
                worldMembers: string.Empty,
                "public bool Matches(string value, int index) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedBadRecordGetKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Matches(e.MonsterId, e.PlayerLevel)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale record.get metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_primitive_helper_argument_mismatch()
    {
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Always",
                "bool Always(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: false,
                capabilities: [],
                effects: [],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                "Bool(__dotboxd_kernel_method_arg_0__)",
                worldMembers: string.Empty,
                "public bool Always(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedBadLiteralHelperKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Always(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale literal metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_named_helper_arguments()
    {
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Always",
                "bool Always(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: false,
                capabilities: [],
                effects: [],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                "Gt(right: I32(0), left: I32(1))",
                worldMembers: string.Empty,
                "public bool Always(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedNamedHelperKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Always(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale helper argument metadata",
                StringComparison.Ordinal));
    }

    private static string ShapeDescriptorSdkSource(
        string methodName,
        string signature,
        string returnType,
        bool allocates,
        string[] capabilities,
        string[] effects,
        KernelMethodDescriptorParameter[] parameters,
        string source,
        string worldMembers,
        string methodDeclaration)
    {
        var payload = new KernelMethodDescriptorPayload(
            KernelMethodDescriptorPayload.CurrentVersion,
            "global::Sdk.GamePluginContext",
            methodName,
            signature,
            returnType,
            allocates,
            new EquatableArray<string>(capabilities),
            new EquatableArray<string>(effects),
            new EquatableArray<KernelMethodDescriptorParameter>(parameters),
            source);
        var json = payload.ToJson();
        var hash = KernelMethodDescriptorPayload.Hash(json);
        var interfaceWorldMembers = string.IsNullOrWhiteSpace(worldMembers)
            ? string.Empty
            : worldMembers.Replace(
                ";",
                " => throw new System.NotSupportedException(\"metadata-only world\");",
                StringComparison.Ordinal);
        return $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Services.Attributes;

            [assembly: global::DotBoxD.Abstractions.GeneratedKernelMethodDescriptorAttribute(
                {{KernelMethodDescriptorPayload.CurrentVersion}},
                typeof(global::Sdk.GamePluginContext),
                "{{methodName}}",
                "{{signature}}",
                {{KernelMethodDescriptorPayload.JsonString(hash)}},
                {{KernelMethodDescriptorPayload.JsonString(json)}})]

            namespace Sdk
            {
                [RpcService]
                public interface IGameWorld
                {
                    {{interfaceWorldMembers}}
                }

                [GeneratePluginServer(Context = typeof(GamePluginContext))]
                public abstract class GamePluginServer : IGameWorld
                {
                }

                public sealed class GamePluginContext
                {
                    private readonly HookContext _raw;
                    public GamePluginContext(HookContext raw) => _raw = raw;
                    public HookContext Raw => _raw;
                    public IPluginMessageSink Messages => _raw.Messages;
                    public IGameWorld World => _raw.Host<IGameWorld>();
                    public static GamePluginContext FromHookContext(HookContext raw) => new(raw);

                    [KernelMethod]
                    {{methodDeclaration}}
                }
            }
            """;
    }

    private static string ShapeConsumerSource(string predicate)
        => $$"""
            using DotBoxD.Plugins.Runtime;
            using Sdk;

            namespace Consumer;

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent, GamePluginContext>(GamePluginContext.FromHookContext)
                        .Where((e, ctx) => {{predicate}})
                        .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
            }
            """;
}
