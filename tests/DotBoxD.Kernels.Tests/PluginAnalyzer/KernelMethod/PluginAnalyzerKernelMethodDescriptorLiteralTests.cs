using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_non_int_i32_literal()
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
                "Eq(I32(1L), I32(1))",
                worldMembers: string.Empty,
                "public bool Always(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedLongI32LiteralKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Always(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale literal metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_unsigned_i64_literal()
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
                "Eq(I64(1UL), I64(1L))",
                worldMembers: string.Empty,
                "public bool Always(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedUnsignedI64LiteralKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Always(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale literal metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_invalid_record_generic_field()
    {
        var recordType = "global::DotBoxD.Kernels.Sandbox.SandboxType.Record(" +
            "new global::DotBoxD.Kernels.Sandbox.SandboxType[] { " +
            "global::DotBoxD.Kernels.Sandbox.SandboxType.String, null })";
        var source =
            $"StringEquals(new {CallExpression}(\"record.get\", " +
            $"[new {CallExpression}(\"record.new\", [__dotboxd_kernel_method_arg_0__], {recordType}, Span), " +
            "I32(0)], null, Span), __dotboxd_kernel_method_arg_0__)";
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Matches",
                "bool Matches(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: [],
                effects: ["Alloc"],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                source,
                worldMembers: string.Empty,
                "public bool Matches(string value) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedInvalidRecordGenericKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Matches(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale record.new metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_empty_record_generic()
    {
        var recordType = "global::DotBoxD.Kernels.Sandbox.SandboxType.Record(" +
            "new global::DotBoxD.Kernels.Sandbox.SandboxType[] { })";
        var source =
            $"StringEquals(new {CallExpression}(\"record.get\", " +
            $"[new {CallExpression}(\"record.new\", [], {recordType}, Span), I32(0)], null, Span), " +
            "__dotboxd_kernel_method_arg_0__)";
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Matches",
                "bool Matches(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: [],
                effects: ["Alloc"],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                source,
                worldMembers: string.Empty,
                "public bool Matches(string value) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedEmptyRecordGenericKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Matches(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale record.new metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_named_sandbox_type_arguments()
    {
        var mapType = "global::DotBoxD.Kernels.Sandbox.SandboxType.Map(" +
            "value: global::DotBoxD.Kernels.Sandbox.SandboxType.String, " +
            "key: global::DotBoxD.Kernels.Sandbox.SandboxType.I32)";
        var recordType = "global::DotBoxD.Kernels.Sandbox.SandboxType.Record(" +
            $"new global::DotBoxD.Kernels.Sandbox.SandboxType[] {{ {mapType} }})";
        var source =
            $"And(Bool(true), new {CallExpression}(\"record.new\", " +
            $"[new {CallExpression}(\"host.Sdk.IGameWorld.Lookup\", " +
            $"[__dotboxd_kernel_method_arg_0__], null, Span)], {recordType}, Span))";
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Matches",
                "bool Matches(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: ["sample.lookup"],
                effects: ["Alloc", "Cpu", "HostStateRead"],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                source,
                "[HostBinding(\"sample.lookup\", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]\n" +
                    "        System.Collections.Generic.Dictionary<string, int> Lookup(string id);",
                "public bool Matches(string value) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedNamedSandboxTypeArgumentKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Matches(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale record.new metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_accepts_int32_to_string_allocation_metadata()
    {
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Always",
                "bool Always(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: [],
                effects: [],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                "StringEquals(Int32ToStr(I32(1)), Str(\"1\"))",
                worldMembers: string.Empty,
                "public bool Always(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedInt32ToStringKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Always(e.MonsterId)"), sdkReference);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_string_equality_helper()
    {
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Always",
                "bool Always(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: [],
                effects: [],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                "Eq(Str(\"a\"), Str(\"a\"))",
                worldMembers: string.Empty,
                "public bool Always(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedStringEqualityKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Always(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale equality metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_non_null_generic_on_list_count()
    {
        var source =
            $"Gt(new {CallExpression}(\"list.count\", " +
            $"[new {CallExpression}(\"host.Sdk.IGameWorld.Tags\", " +
            "[__dotboxd_kernel_method_arg_0__], null, Span)], " +
            "global::DotBoxD.Kernels.Sandbox.SandboxType.I32, Span), I32(1))";
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
            "ForgedGenericListCountKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.HasTags(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale list.count metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_spread_call_arguments()
    {
        var source =
            $"Gt(new {CallExpression}(\"list.count\", " +
            $"[new {CallExpression}(\"host.Sdk.IGameWorld.Tags\", " +
            "[__dotboxd_kernel_method_arg_0__], null, Span), " +
            "..(global::DotBoxD.Kernels.Expression[])[Str(\"x\")]], null, Span), I32(1))";
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
            "ForgedSpreadCallArgumentsKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.HasTags(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "unsupported expression source",
                StringComparison.Ordinal));
    }
}
