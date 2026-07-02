using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientReturnAttributeSurpriseTests
{
    [Fact]
    public void Service_backed_generated_client_preserves_return_flow_attributes()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ServiceBackedSource);
        var client = assembly.GetType("Sample.EchoKernelServerExtensionClient", throwOnError: true)!;
        var method = client.GetMethod(
            "EchoAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(string), typeof(System.Threading.CancellationToken)])!;

        AssertNotNullIfNotNull(method, "value");
    }

    [Fact]
    public void Service_backed_receiver_method_extension_preserves_return_flow_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(ServiceBackedSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelServerExtensionClientExtensions",
            "[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute(\"value\")]");
    }

    [Fact]
    public void Direct_receiver_method_extension_preserves_return_flow_attributes()
    {
        var generatedSources = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(DirectExtensionSource);

        AssertGeneratedSourceContains(
            generatedSources,
            "EchoKernelDirectServerExtensionClientExtensions",
            "[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute(\"value\")]");
    }

    private static void AssertNotNullIfNotNull(MethodInfo method, string parameterName)
    {
        var attribute = Assert.Single(method.ReturnParameter.GetCustomAttributes<NotNullIfNotNullAttribute>());

        Assert.Equal(parameterName, attribute.ParameterName);
    }

    private static void AssertGeneratedSourceContains(
        IReadOnlyList<string> generatedSources,
        string generatedTypeName,
        string expectedSource)
        => Assert.Contains(
            generatedSources,
            source => source.Contains(generatedTypeName, StringComparison.Ordinal) &&
                      source.Contains(expectedSource, StringComparison.Ordinal));

    private const string ServiceBackedSource = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public interface IEchoService
        {
            [return: NotNullIfNotNull(nameof(value))]
            ValueTask<string> EchoAsync(string value, CancellationToken cancellationToken = default);
        }

        [ServerExtensionClient(typeof(IRemoteControl), "EchoClient")]
        [ServerExtension("echo", typeof(IEchoService))]
        public sealed partial class EchoKernel
        {
            [ServerExtensionMethod(typeof(IRemoteControl), "EchoValue")]
            public string Echo(string value, HookContext ctx) => value;
        }
        """;

    private const string DirectExtensionSource = """
        #nullable enable
        using System.Diagnostics.CodeAnalysis;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteControl;

        public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
        {
            public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteControl), "direct-echo")]
        public sealed partial class EchoKernel
        {
            [return: NotNullIfNotNull(nameof(value))]
            [ServerExtensionMethod(typeof(IRemoteControl), "Echo")]
            public string Echo(string value, HookContext ctx) => value;
        }
        """;
}
