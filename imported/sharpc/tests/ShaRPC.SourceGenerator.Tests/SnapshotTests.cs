using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using VerifyXunit;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Snapshot tests for ShaRpcGenerator. Snapshots live in the Snapshots/ subfolder
/// next to this file and are accepted via Verify's standard flow.
/// </summary>
public class SnapshotTests
{
    private const string SingleMethodService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.One
        {
            [ShaRpcService]
            public interface ICalculator
            {
                Task<int> AddAsync(int a, int b);
            }
        }
        """;

    private const string MixedReturnsService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Mixed
        {
            [ShaRpcService]
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
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Renamed
        {
            [ShaRpcService(Name = "Greeter")]
            public interface IHello
            {
                [ShaRpcMethod(Name = "Greet")]
                Task<string> HelloAsync(string who);
            }
        }
        """;

    private const string TwoServices = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Two
        {
            [ShaRpcService]
            public interface IOne
            {
                Task<int> AAsync(int x);
            }

            [ShaRpcService]
            public interface ITwo
            {
                Task<string> BAsync();
            }
        }
        """;

    private const string ValueTaskService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Vt
        {
            [ShaRpcService]
            public interface IVtSnap
            {
                ValueTask<int> AddAsync(int a, int b);
                ValueTask PingAsync();
            }
        }
        """;

    private const string RefOutStubService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.RefOut
        {
            [ShaRpcService]
            public interface IRefOutSnap
            {
                void BadOut(out int x);
                Task<int> GoodAsync(int a);
            }
        }
        """;

    private const string InheritedMembersService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Inherit
        {
            public interface IBase
            {
                Task<int> BaseAsync(int x);
            }

            [ShaRpcService]
            public interface IDerived : IBase
            {
                Task<string> DerivedAsync();
            }
        }
        """;

    private const string KeywordEscapedParamsService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Kw
        {
            [ShaRpcService]
            public interface IKwSnap
            {
                Task<int> DoAsync(int @class, int @default);
            }
        }
        """;

    private const string NestedService = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Snap.Nested
        {
            [ShaRpcService]
            public interface ISubSnap
            {
                Task<int> CountAsync();
            }

            [ShaRpcService]
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
