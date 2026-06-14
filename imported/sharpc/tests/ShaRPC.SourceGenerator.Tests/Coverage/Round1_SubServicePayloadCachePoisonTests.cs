using System.Linq;
using Microsoft.CodeAnalysis;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// RED regression test (bug-hunt ROUND 1, defect #3) for a false-negative poisoned into the
/// per-compilation sub-service payload cache.
///
/// <para>
/// <see cref="ShaRPC.SourceGenerator"/>'s SubServicePayloadInspector caches the result of
/// "does this DTO transitively reach a [ShaRpcService] interface?". In a cyclic DTO graph
/// (A holds B, B holds A) where A *also* holds a service-interface field, visiting B while
/// already inside A's traversal trips the cycle-break guard and returns <c>false</c> for B —
/// a context-dependent answer that is only correct because A is mid-traversal. The inspector
/// then caches that <c>false</c> for B unconditionally. A later, independent query for B
/// (the parameter of a different method) reads the poisoned cache entry and reports B as
/// payload-clean, so the SHARPC002 "contains a sub-service type" diagnostic is suppressed for
/// the method that takes B even though B does transitively reach the service interface via A.
/// </para>
///
/// <para>
/// Both methods live on one service interface and the cache is shared per-compilation
/// (<c>SharedRpcTypeValidationCache</c>), so processing <c>UseAAsync(A)</c> first deterministically
/// poisons the B entry before <c>UseBAsync(B)</c> is validated. <see cref="A"/> declares its
/// <c>Child</c> (type B) field BEFORE its <c>Svc</c> (the service interface) field, which is the
/// member ordering that drives the cycle-break-then-find-service sequence.
/// </para>
///
/// <para>
/// The correct behaviour (and what this test asserts) is that BOTH <c>UseAAsync</c> and
/// <c>UseBAsync</c> are flagged with SHARPC002, because A and B each transitively contain the
/// sub-service interface. The control assertion for <c>UseAAsync</c> passes today; the
/// <c>UseBAsync</c> assertion is RED on the current (unfixed) code due to the poisoned cache.
/// </para>
/// </summary>
public sealed class Round1_SubServicePayloadCachePoisonTests
{
    // Order matters: in A, `Child` (type B) is declared BEFORE `Svc` (the service interface).
    // Both UseAAsync(A) and UseBAsync(B) live on one interface so they share the per-compilation
    // validation cache, and UseAAsync is declared first so A's traversal runs (and poisons B)
    // before B is validated on its own.
    private const string Source = @"
using System.Threading.Tasks;
using ShaRPC.Core.Attributes;

namespace Bug.Reg.CyclePoison
{
    public sealed class A
    {
        public B Child;
        public IMyRpc Svc;
    }

    public sealed class B
    {
        public A Parent;
    }

    [ShaRpcService]
    public interface IMyRpc
    {
        Task<int> PingAsync();
    }

    [ShaRpcService]
    public interface IRoot
    {
        Task UseAAsync(A a);
        Task UseBAsync(B b);
    }
}";

    [Fact]
    public void CyclicDtoReachingServiceInterface_FlagsBothMethods_NotJustTheFirst()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        var subServiceDiagnostics = runResult.Diagnostics
            .Where(d => d.Id == "SHARPC002" && d.GetMessage().Contains("contains a sub-service type"))
            .Select(d => d.GetMessage())
            .ToList();

        // Control: A directly holds the service interface, so the method taking A is always flagged.
        // This holds on both the unfixed and fixed code and confirms the mechanism fires at all.
        Assert.Contains(subServiceDiagnostics, m => m.Contains("UseAAsync"));

        // The defect: B transitively reaches IMyRpc through its A back-reference, so the method
        // taking B must also be flagged. On the current code the cache holds a poisoned false for
        // B (computed under the cycle-break while inside A's traversal), so this diagnostic is
        // missing and the assertion is RED. The suggested fix (cache only positive results) makes
        // it GREEN.
        Assert.Contains(subServiceDiagnostics, m => m.Contains("UseBAsync"));
    }
}
