using SafeIR.Verifier;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0026: <see cref="GeneratedAssemblyVerifier.VerifyAsync"/>
/// copies the entire PE image into a fresh byte array (<c>assemblyBytes.ToArray()</c>)
/// before constructing the <c>PEReader</c>, even though the caller already supplied an
/// immutable <see cref="ReadOnlyMemory{Byte}"/> buffer that hashing consumes without a copy.
///
/// The copy is unconditional: it happens before any metadata is read, for any input,
/// regardless of whether the buffer is a valid PE. So a large non-PE buffer drives the
/// full-size copy as the dominant allocation while short-circuiting (no CLR metadata)
/// the metadata walk, isolating the wasteful copy.
///
/// These tests encode the correct post-fix behavior: per verification, the verifier must
/// not allocate a second full-size copy of the input buffer. They measure the bytes
/// allocated per <c>VerifyAsync</c> call and assert it stays well below the input size.
/// While the <c>ToArray()</c> copy is present, each call allocates at least the full
/// buffer size, so the assertion is false and the test is red until the copy is removed.
/// </summary>
public sealed class Fix_PAL_0026_Tests
{
    // A large enough input that one full-size copy dwarfs all incidental per-call
    // allocations (hash digest, diagnostics list, PEReader bookkeeping).
    private const int BufferSize = 2 * 1024 * 1024; // 2 MB

    [Fact]
    public async Task VerifyAsync_does_not_copy_the_full_input_buffer()
    {
        var buffer = BuildLargeNonPeBuffer(BufferSize);
        var manifest = BuildManifest(buffer);
        var policy = BuildPolicy(manifest);
        var verifier = new GeneratedAssemblyVerifier();
        ReadOnlyMemory<byte> input = buffer;

        // Warm up: JIT the verifier path and any one-time allocations.
        await verifier.VerifyAsync(input, manifest, policy, CancellationToken.None);

        const int iterations = 8;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            _ = await verifier.VerifyAsync(input, manifest, policy, CancellationToken.None);
        }

        var totalAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var perCall = totalAllocated / iterations;

        // A non-copying PE input path allocates only small bookkeeping per call. The
        // current ToArray() copy allocates the entire BufferSize image every call, so
        // perCall lands at or above BufferSize while the bug is present.
        Assert.True(
            perCall < BufferSize / 2,
            $"VerifyAsync allocated ~{perCall} bytes/call for a {BufferSize}-byte input, " +
            "indicating the full PE buffer is still being copied before metadata reads.");
    }

    [Fact]
    public async Task VerifyAsync_allocation_does_not_scale_with_input_size()
    {
        var verifier = new GeneratedAssemblyVerifier();

        var smallPerCall = await PerCallAllocationAsync(verifier, 256 * 1024);
        var largePerCall = await PerCallAllocationAsync(verifier, BufferSize);

        // Without a full-buffer copy, per-call allocation is dominated by fixed
        // bookkeeping and barely changes when the input grows ~8x. The ToArray()
        // copy makes per-call allocation scale linearly with the input, so the large
        // input allocates dramatically more than the small one.
        Assert.True(
            largePerCall < smallPerCall + (BufferSize / 2),
            $"Per-call allocation grew with input size ({smallPerCall} bytes for 256 KB " +
            $"vs {largePerCall} bytes for {BufferSize / 1024} KB), indicating a full-buffer copy.");
    }

    private static async Task<long> PerCallAllocationAsync(GeneratedAssemblyVerifier verifier, int size)
    {
        var buffer = BuildLargeNonPeBuffer(size);
        var manifest = BuildManifest(buffer);
        var policy = BuildPolicy(manifest);
        ReadOnlyMemory<byte> input = buffer;

        await verifier.VerifyAsync(input, manifest, policy, CancellationToken.None);

        const int iterations = 8;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            _ = await verifier.VerifyAsync(input, manifest, policy, CancellationToken.None);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    private static byte[] BuildLargeNonPeBuffer(int size)
    {
        // Deterministic, non-PE content. The verifier still hashes it and unconditionally
        // copies it via ToArray() before discovering it has no CLR metadata.
        var buffer = new byte[size];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = unchecked((byte)(i * 31 + 7));
        }

        return buffer;
    }

    private static ArtifactManifest BuildManifest(byte[] buffer)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(buffer)).ToLowerInvariant();
        return new ArtifactManifest(
            1,
            "test",
            "module",
            "plan",
            "policy",
            "bindings",
            "runtime",
            "compiler",
            "type-system",
            "effect-analysis",
            "verifier",
            "1.0.0",
            "net10.0",
            [],
            hash,
            DateTimeOffset.UtcNow);
    }

    private static VerificationPolicy BuildPolicy(ArtifactManifest manifest)
    {
        var policy = VerificationPolicy.BoxedValueDefaults();
        return policy.ExpectedManifestIdentity is null
            ? policy.WithExpectedManifest(VerificationManifestIdentity.FromManifest(manifest))
            : policy;
    }
}
