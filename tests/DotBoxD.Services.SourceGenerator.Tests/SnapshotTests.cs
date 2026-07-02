namespace DotBoxD.Services.SourceGenerator.Tests;

/// <summary>
/// Snapshot tests for DotBoxDRpcGenerator. Snapshots live in the Snapshots/ subfolder
/// next to this file and are accepted via Verify's standard flow.
/// </summary>
public class SnapshotTests
{
    private const string SingleMethodService = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.One
        {
            [RpcService]
            public interface ICalculator
            {
                Task<int> AddAsync(int a, int b);
            }
        }
        """;

    private const string MixedReturnsService = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Mixed
        {
            [RpcService]
            public interface IMix
            {
                Task<string> GetNameAsync();
                Task SaveAsync(string value);
                int SyncAdd(int a, int b);
                void SyncPing();
            }
        }
        """;

    private const string CustomNameService = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Renamed
        {
            [RpcService(Name = "Greeter")]
            public interface IHello
            {
                [RpcMethod(Name = "Greet")]
                Task<string> HelloAsync(string who);
            }
        }
        """;

    private const string TwoServices = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Two
        {
            [RpcService]
            public interface IOne
            {
                Task<int> AAsync(int x);
            }

            [RpcService]
            public interface ITwo
            {
                Task<string> BAsync();
            }
        }
        """;

    private const string ValueTaskService = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Vt
        {
            [RpcService]
            public interface IVtSnap
            {
                ValueTask<int> AddAsync(int a, int b);
                ValueTask PingAsync();
            }
        }
        """;

    private const string RefOutStubService = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.RefOut
        {
            [RpcService]
            public interface IRefOutSnap
            {
                void BadOut(out int x);
                Task<int> GoodAsync(int a);
            }
        }
        """;

    private const string InheritedMembersService = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Inherit
        {
            public interface IBase
            {
                Task<int> BaseAsync(int x);
            }

            [RpcService]
            public interface IDerived : IBase
            {
                Task<string> DerivedAsync();
            }
        }
        """;

    private const string KeywordEscapedParamsService = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Kw
        {
            [RpcService]
            public interface IKwSnap
            {
                Task<int> DoAsync(int @class, int @default);
            }
        }
        """;

    private const string NestedService = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Nested
        {
            [RpcService]
            public interface ISubSnap
            {
                Task<int> CountAsync();
            }

            [RpcService]
            public interface IRootSnap
            {
                Task<ISubSnap> GetSubAsync(string label);
            }
        }
        """;

    [Fact]
    public Task SingleMethod() => RunVerify(SingleMethodService);

    [Fact]
    public Task MixedReturns() => RunVerify(MixedReturnsService);

    [Fact]
    public Task CustomNames() => RunVerify(CustomNameService);

    [Fact]
    public Task TwoServicesInOneCompilation() => RunVerify(TwoServices);

    [Fact]
    public Task ValueTaskReturns() => RunVerify(ValueTaskService);

    [Fact]
    public Task RefOutStub() => RunVerify(RefOutStubService);

    [Fact]
    public Task InheritedMembers() => RunVerify(InheritedMembersService);

    [Fact]
    public Task KeywordEscapedParameters() => RunVerify(KeywordEscapedParamsService);

    [Fact]
    public Task NestedServiceReturn() => RunVerify(NestedService);

    private static Task RunVerify(string source)
    {
        var (driver, _) = GeneratorTestHelper.RunGenerator(source);
        return Verifier.Verify(driver).UseDirectory("Snapshots");
    }
}
