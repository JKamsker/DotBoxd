namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Test collection for allocation- and wall-time-sensitive measurement tests.
///
/// <para>
/// These tests quantify steady-state per-call / per-iteration allocation (and, for some, a
/// wall-time budget) by sampling runtime counters around a tight measured window. When such a
/// window runs concurrently with the rest of the suite, the allocations and CPU pressure of other
/// test threads contaminate the sample: process-wide counters (<see cref="System.GC"/>.
/// <c>GetTotalAllocatedBytes</c>) literally sum every thread's allocations, and even thread-local
/// measurements suffer wall-time inflation from contended GC. That makes the measurements flaky
/// under parallel execution while remaining correct in isolation.
/// </para>
///
/// <para>
/// Declaring <c>DisableParallelization = true</c> on this collection makes xUnit run these tests
/// serially with respect to every other collection, so each measured window observes only its own
/// work. This is a scheduling guarantee only -- it does not weaken any assertion, threshold, or
/// measured value.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurementCollection
{
    public const string Name = "Allocation measurement (serial)";
    public const string TraitName = "Category";
    public const string TraitValue = "AllocationMeasurement";
}
