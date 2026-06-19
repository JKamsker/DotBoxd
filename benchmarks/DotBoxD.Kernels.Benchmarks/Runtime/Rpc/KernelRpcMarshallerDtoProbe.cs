using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class KernelRpcMarshallerDtoProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 500_000;

    private static readonly ConcurrentDictionary<Type, LegacyAnonymousShape> LegacyShapes = new();
    private static readonly ConcurrentDictionary<Type, LegacySettableShape> LegacySettableShapes = new();

    public static void Run()
    {
        var anonymousSample = new { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Zone = "crypt" };
        var anonymousType = anonymousSample.GetType();
        var anonymousRecord = (RecordValue)SandboxValue.FromRecord(
        [
            SandboxValue.FromGuid(anonymousSample.Id),
            SandboxValue.FromString(anonymousSample.Zone)
        ]);
        var settableRecord = (RecordValue)SandboxValue.FromRecord(
        [
            SandboxValue.FromInt32(73),
            SandboxValue.FromString("crypt")
        ]);
        var settableType = typeof(SettableDto);

        _ = Measure(Warmup, () => LegacyFromSandboxValue(anonymousRecord, anonymousType));
        _ = Measure(Warmup, () => KernelRpcMarshaller.FromSandboxValue(anonymousRecord, anonymousType)!);
        _ = Measure(Warmup, () => LegacySettableFromSandboxValue(settableRecord, settableType));
        _ = Measure(Warmup, () => KernelRpcMarshaller.FromSandboxValue(settableRecord, settableType)!);

        var legacy = Measure(Iterations, () => LegacyFromSandboxValue(anonymousRecord, anonymousType));
        var cached = Measure(Iterations, () => KernelRpcMarshaller.FromSandboxValue(anonymousRecord, anonymousType)!);
        var legacySettable = Measure(Iterations, () => LegacySettableFromSandboxValue(settableRecord, settableType));
        var cachedSettable = Measure(Iterations, () => KernelRpcMarshaller.FromSandboxValue(settableRecord, settableType)!);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("legacy cached reflection anonymous DTO", legacy);
        Write("compiled shape anonymous DTO", cached);
        Write("legacy reflection settable DTO", legacySettable);
        Write("compiled shape settable DTO", cachedSettable);
    }

    private static object LegacyFromSandboxValue(RecordValue record, Type type)
        => LegacyShapes.GetOrAdd(type, static candidate => new LegacyAnonymousShape(candidate))
            .Construct(record);

    private static object LegacySettableFromSandboxValue(RecordValue record, Type type)
        => LegacySettableShapes.GetOrAdd(type, static candidate => new LegacySettableShape(candidate))
            .Construct(record);

    private static Measurement Measure(int iterations, Func<object> deserialize)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum = unchecked((checksum * 31) + RuntimeHelpers.GetHashCode(deserialize()));
        }

        watch.Stop();
        return new Measurement(watch.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, checksum);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-40} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B checksum={measurement.Checksum:N0}");

    private sealed class LegacyAnonymousShape
    {
        private readonly ConstructorInfo _constructor;
        private readonly PropertyInfo[] _fields;

        public LegacyAnonymousShape(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            _fields = type.GetProperties(flags)
                .Where(static property => property.CanRead &&
                    property.GetIndexParameters().Length == 0 &&
                    !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
                .OrderBy(static property => property.MetadataToken)
                .ToArray();
            _constructor = type.GetConstructors()
                .First(constructor => constructor.GetParameters().Length == _fields.Length);
        }

        public object Construct(RecordValue record)
        {
            var arguments = new object?[_fields.Length];
            for (var i = 0; i < _fields.Length; i++)
            {
                arguments[i] = KernelRpcMarshaller.FromSandboxValue(record.Fields[i], _fields[i].PropertyType);
            }

            return _constructor.Invoke(arguments);
        }
    }

    private sealed class LegacySettableShape
    {
        private readonly PropertyInfo[] _fields;
        private readonly Type _type;

        public LegacySettableShape(Type type)
        {
            _type = type;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            _fields = type.GetProperties(flags)
                .Where(static property => property.CanRead &&
                    property.GetIndexParameters().Length == 0 &&
                    !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
                .OrderBy(static property => property.MetadataToken)
                .ToArray();
        }

        public object Construct(RecordValue record)
        {
            var arguments = new object?[_fields.Length];
            for (var i = 0; i < _fields.Length; i++)
            {
                arguments[i] = KernelRpcMarshaller.FromSandboxValue(record.Fields[i], _fields[i].PropertyType);
            }

            var instance = Activator.CreateInstance(_type)!;
            for (var i = 0; i < _fields.Length; i++)
            {
                _fields[i].SetValue(instance, arguments[i]);
            }

            return instance;
        }
    }

    private sealed class SettableDto
    {
        public int Id { get; set; }

        public string Zone { get; set; } = string.Empty;
    }

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int Checksum);
}
