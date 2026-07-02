using FluentAssertions;
using Microsoft.CodeAnalysis;
namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public partial class CodegenRegressionTests
{
    [Fact]
    public void EscapedKeywordNamespaceAndServiceName_CompileGeneratedOutput()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.@event
            {
                [RpcService]
                public interface @class
                {
                    Task<int> CountAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxyHint = GeneratorTestHelper.HintName(
            "Regress.@event", "class", GeneratorTestHelper.GeneratedKind.Proxy);
        proxyHint.Should().NotContain("@");

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == proxyHint)
            .SourceText.ToString();
        proxy.Should().Contain("namespace Regress.@event");
        proxy.Should().Contain("global::Regress.@event.@class");
    }

    [Fact]
    public void GlobalNamespaceService_CompilesAndDoesNotEmitEmptyNamespace()
    {
        // Regression for the global-namespace branch: emitting a stray `namespace { ... }`
        // would fail to parse; emitting no namespace must keep the proxy at global scope.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            [RpcService]
            public interface IGlobal
            {
                Task<int> GoAsync(int x);
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        // Sanity: the proxy file must NOT start a namespace block for the global scope.
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "IGlobal.DotBoxDRpcProxy.g.cs")
            .SourceText.ToString();
        proxy.Should().NotContain("namespace ");
    }

    [Fact]
    public void StringLiteralsInServiceNameAndMethodName_AreEscaped()
    {
        // Regression for M3: a Name containing a double quote would break the generated
        // string literal. Escape it.
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Escape
            {
                [RpcService(Name = "Foo\"Bar")]
                public interface IEsc
                {
                    [RpcMethod(Name = "do\"it")]
                    Task<int> DoAsync(int x);
                }
            }
            """;

        var (final, _) = Run(source);
        AssertCompiles(final);
    }

    [Fact]
    public void ServiceAndSubServiceNames_WithQuotesAndBraces_AreEscapedEverywhere()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SubServiceEscape
            {
                [RpcService(Name = "Sub\"{Svc")]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [RpcService(Name = "Root{Svc")]
                public interface IRoot
                {
                    Task<ISub> GetSubAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.SubServiceEscape", "IRoot", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("registry.Register(\"Sub\\\"{Svc\", __sub)");
        dispatcher.Should().Contain("ServiceName = \"Sub\\\"{Svc\"");
        dispatcher.Should().Contain("\"Method '\" + method + \"' not found on service 'Root{Svc'.\"");
    }

    [Fact]
    public void GlobalNamespaceSubServiceReturn_UsesGeneratedSubProxyTypeName()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            [RpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            [RpcService]
            public interface IRoot
            {
                Task<ISub> GetSubAsync();
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "IRoot.DotBoxDRpcProxy.g.cs")
            .SourceText.ToString();
        proxy.Should().Contain("return new global::SubProxy(this._invoker, __dotboxd_handle.InstanceId);");
        proxy.Should().NotContain("global::ISubProxy");
    }

    [Fact]
    public void SynchronousSubServiceReturn_RegistersNestedServiceHandle()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SyncSubService
            {
                [RpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    ISub GetSub();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.SyncSubService", "IRoot", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("return new global::Regress.SyncSubService.SubProxy(this._invoker, __dotboxd_handle.InstanceId);");

        var dispatcher = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.SyncSubService", "IRoot", GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("case \"GetSub\":");
        dispatcher.Should().Contain("__subId = registry.Register(\"ISub\", __sub);");
        dispatcher.Should().Contain("serializer.Serialize(output, new global::DotBoxD.Services.Protocol.ServiceHandle");

        generated.Select(g => g.SourceText.ToString())
            .Should().Contain(source => source.Contains("GeneratedReturnKind.SyncNestedService", StringComparison.Ordinal));
    }

    [Fact]
    public void GenericSubServiceReturn_ProducesDBXS002_AndDoesNotBuildInvalidProxyType()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GenericSubService
            {
                [RpcService]
                public interface IChild<T>
                {
                    Task<int> CountAsync();
                }

                [RpcService]
                public interface IRoot
                {
                    Task<IChild<int>> GetChildAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003",
            "the generic sub-service interface itself is rejected");
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("generic sub-service return types are not supported"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.GenericSubService", "IRoot", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().NotContain("Child<int>Proxy");
    }

}
