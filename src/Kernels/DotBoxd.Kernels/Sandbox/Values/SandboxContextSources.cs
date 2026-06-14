namespace DotBoxd.Kernels;

// Deterministic and non-deterministic source primitives for the sandbox:
// the logical clock (UtcNow / AuditTimestamp) and the random number source
// (NextRandomInt32 backed by a seeded DeterministicRandom). Split into its own
// partial file to keep SandboxContext focused; behavior is identical.
public sealed partial class SandboxContext
{
    public DateTimeOffset UtcNow()
    {
        if (Policy.Deterministic)
        {
            return Policy.LogicalNow ?? throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.PolicyDenied,
                "deterministic time requires a logical clock"));
        }

        return DateTimeOffset.UtcNow;
    }

    public DateTimeOffset AuditTimestamp()
        => Policy.Deterministic
            ? Policy.LogicalNow ?? DateTimeOffset.UnixEpoch
            : DateTimeOffset.UtcNow;

    public int NextRandomInt32(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "random range is invalid"));
        }

        if (Policy.Deterministic)
        {
            if (Policy.RandomSeed is null)
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.PolicyDenied,
                    "deterministic random requires a seed"));
            }

            _deterministicRandom ??= new DeterministicRandom(Policy.RandomSeed.Value);
            return _deterministicRandom.Next(minInclusive, maxExclusive);
        }

        return Random.Shared.Next(minInclusive, maxExclusive);
    }

    private sealed class DeterministicRandom(ulong seed)
    {
        private ulong _state = seed;

        public int Next(int minInclusive, int maxExclusive)
        {
            var range = (ulong)((long)maxExclusive - minInclusive);
            var threshold = (1UL << 32) % range;
            while (true)
            {
                var value = NextUInt32();
                if (value >= threshold)
                {
                    return checked((int)(minInclusive + (long)(value % range)));
                }
            }
        }

        private uint NextUInt32()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return (uint)(value >> 32);
        }
    }
}
