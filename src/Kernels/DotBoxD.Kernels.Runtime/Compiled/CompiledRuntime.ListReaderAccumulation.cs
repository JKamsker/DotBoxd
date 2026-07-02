using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

public static partial class CompiledRuntime
{
    public static int ListI32ReaderAddRemainderCycleFromZeroRaw(
        SandboxContext context,
        object reader,
        int current,
        int iterations,
        int divisor,
        int loopFuelPerIteration,
        long readFuel)
    {
        if (iterations <= 0)
        {
            return current;
        }

        if (divisor <= 0)
        {
            throw InvalidInput("integer division by zero");
        }

        var items = (int[])reader;
        var firstCycleIterations = Math.Min(iterations, Math.Min(divisor, items.Length));
        var afterFirstCycle = current;
        for (var i = 0; i < firstCycleIterations; i++)
        {
            if (!TryAddI32(afterFirstCycle, items[i], out afterFirstCycle))
            {
                ChargeThrough(context, i + 1, loopFuelPerIteration, readFuel);
                throw InvalidInput("integer overflow");
            }
        }

        if (iterations <= firstCycleIterations)
        {
            ChargeThrough(context, iterations, loopFuelPerIteration, readFuel);
            return afterFirstCycle;
        }

        if (divisor > items.Length)
        {
            ChargeThrough(context, items.Length + 1, loopFuelPerIteration, readFuel);
            throw InvalidInput("list index is out of range");
        }

        var cycleDelta = (long)afterFirstCycle - current;
        var overflowAt = FirstOverflowIteration(current, items, divisor, iterations, cycleDelta);
        if (overflowAt > 0)
        {
            ChargeThrough(context, overflowAt, loopFuelPerIteration, readFuel);
            throw InvalidInput("integer overflow");
        }

        ChargeThrough(context, iterations, loopFuelPerIteration, readFuel);
        return FinalCycleValue(current, items, divisor, iterations, cycleDelta);
    }

    private static int FirstOverflowIteration(
        int current,
        int[] items,
        int divisor,
        int iterations,
        long cycleDelta)
    {
        if (cycleDelta == 0)
        {
            return 0;
        }

        var prefix = 0L;
        var best = long.MaxValue;
        for (var i = 0; i < divisor; i++)
        {
            prefix += items[i];
            var q = cycleDelta > 0
                ? FirstPositiveOverflowCycle(current, prefix, cycleDelta)
                : FirstNegativeOverflowCycle(current, prefix, -cycleDelta);
            var iteration = q * divisor + i + 1L;
            if (q > 0 && iteration <= iterations && iteration < best)
            {
                best = iteration;
            }
        }

        return best == long.MaxValue ? 0 : (int)best;
    }

    private static long FirstPositiveOverflowCycle(int current, long prefix, long cycleDelta)
    {
        var room = (long)int.MaxValue - current - prefix;
        return Math.Max(1, room / cycleDelta + 1);
    }

    private static long FirstNegativeOverflowCycle(int current, long prefix, long step)
    {
        var room = (long)current + prefix - int.MinValue;
        return Math.Max(1, room / step + 1);
    }

    private static int FinalCycleValue(
        int current,
        int[] items,
        int divisor,
        int iterations,
        long cycleDelta)
    {
        var cycles = iterations / divisor;
        var remainder = iterations % divisor;
        var prefix = 0L;
        for (var i = 0; i < remainder; i++)
        {
            prefix += items[i];
        }

        return (int)(current + cycles * cycleDelta + prefix);
    }

    private static bool TryAddI32(int left, int right, out int result)
    {
        var sum = (long)left + right;
        if (sum < int.MinValue || sum > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (int)sum;
        return true;
    }

    private static void ChargeThrough(
        SandboxContext context,
        int iterations,
        int loopFuelPerIteration,
        long readFuel)
    {
        context.ChargeLoopIterations(iterations, loopFuelPerIteration);
        context.ChargeBulkFuel(readFuel, iterations);
    }
}
