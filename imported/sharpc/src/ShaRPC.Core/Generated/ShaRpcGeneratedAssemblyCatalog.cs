using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ShaRPC.Core.Generated;

internal static class ShaRpcGeneratedAssemblyCatalog
{
    private const string GeneratedFactoryTypeName = "ShaRPC.Generated.ShaRpcGenerated";

    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<ShaRpcGeneratedService>> s_serviceCatalogs = new();
    private static readonly ConcurrentDictionary<Assembly, Lazy<bool>> s_registrationAttempts = new();
    private static readonly ConcurrentDictionary<Assembly, SinkRegistrar<IShaRpcServiceRegistrationSink>> s_serviceSinks = new();
    private static readonly ConcurrentDictionary<Assembly, SinkRegistrar<IShaRpcGeneratedServiceRegistrationSink>> s_generatedSinks = new();

    public static bool EnsureRegistered(Assembly assembly)
    {
        var registration = s_registrationAttempts.GetOrAdd(
            assembly,
            static assembly => new Lazy<bool>(
                () => RegisterGeneratedFactory(assembly),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return registration.Value;
        }
        catch
        {
            EvictFaultedAttempt(assembly, registration);
            throw;
        }
    }

    /// <summary>
    /// Removes only the faulted attempt this caller actually holds. A key-only TryRemove could evict a
    /// fresh successor <see cref="Lazy{T}"/> that another thread installed after our attempt faulted,
    /// discarding that successor's registration; the value-comparing <see cref="ICollection{T}.Remove"/>
    /// is a no-op unless the stored Lazy is still ours (reference equality, since Lazy does not override
    /// Equals). Internal so a deterministic test can exercise the successor-preservation behaviour.
    /// </summary>
    internal static void EvictFaultedAttempt(Assembly assembly, Lazy<bool> faultedRegistration) =>
        ((ICollection<KeyValuePair<Assembly, Lazy<bool>>>)s_registrationAttempts)
            .Remove(new KeyValuePair<Assembly, Lazy<bool>>(assembly, faultedRegistration));

    // --- Test accessors (for the deterministic fault-recovery successor-preservation test) ---
    internal static void SetRegistrationAttemptForTest(Assembly assembly, Lazy<bool> attempt) =>
        s_registrationAttempts[assembly] = attempt;

    internal static Lazy<bool>? GetRegistrationAttemptForTest(Assembly assembly) =>
        s_registrationAttempts.TryGetValue(assembly, out var attempt) ? attempt : null;

    private static bool RegisterGeneratedFactory(Assembly assembly)
    {
        var generatedType = FindGeneratedType(assembly);
        if (generatedType is null)
        {
            return false;
        }

        try
        {
            RuntimeHelpers.RunClassConstructor(generatedType.TypeHandle);
            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ShaRPC generated factory registration failed for assembly '{assembly.FullName}'.",
                ex);
        }
    }

    public static IReadOnlyList<ShaRpcGeneratedService> GetServices(Assembly assembly) =>
        s_serviceCatalogs.GetOrAdd(assembly, static assembly => LoadGeneratedServices(assembly));

    public static void PublishServices(Assembly assembly, IReadOnlyList<ShaRpcGeneratedService> services) =>
        s_serviceCatalogs[assembly] = services;

    public static void RegisterServices(Assembly assembly, IShaRpcServiceRegistrationSink sink) =>
        s_serviceSinks
            .GetOrAdd(assembly, static assembly => CreateSinkRegistrar<IShaRpcServiceRegistrationSink>(
                assembly,
                "RegisterServices"))
            .Invoke(sink);

    public static void RegisterGeneratedServices(Assembly assembly, IShaRpcGeneratedServiceRegistrationSink sink) =>
        s_generatedSinks
            .GetOrAdd(assembly, static assembly => CreateSinkRegistrar<IShaRpcGeneratedServiceRegistrationSink>(
                assembly,
                "RegisterGeneratedServices"))
            .Invoke(sink);

    private static IReadOnlyList<ShaRpcGeneratedService> LoadGeneratedServices(Assembly assembly)
    {
        var generatedType = FindGeneratedType(assembly);
        if (generatedType is null)
        {
            return Array.Empty<ShaRpcGeneratedService>();
        }

        EnsureRegistered(assembly);
        if (s_serviceCatalogs.TryGetValue(assembly, out var services))
        {
            return services;
        }

        var property = generatedType.GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
        if (property?.GetValue(null) is IReadOnlyList<ShaRpcGeneratedService> legacyServices)
        {
            s_serviceCatalogs[assembly] = legacyServices;
            return legacyServices;
        }

        throw new InvalidOperationException(
            $"ShaRPC generated factory type '{GeneratedFactoryTypeName}' in assembly '{assembly.FullName}' " +
            "did not publish a compatible Services catalog.");
    }

    private static SinkRegistrar<TSink> CreateSinkRegistrar<TSink>(Assembly assembly, string methodName)
        where TSink : class
    {
        var generatedType = FindGeneratedType(assembly);
        if (generatedType is null)
        {
            return default;
        }

        EnsureRegistered(assembly);

        var method = generatedType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(TSink) },
            null);
        if (method is null)
        {
            throw new InvalidOperationException(
                $"ShaRPC generated factory type '{GeneratedFactoryTypeName}' in assembly '{assembly.FullName}' " +
                $"did not publish a compatible {methodName} method.");
        }

        return new SinkRegistrar<TSink>(
            (Action<TSink>)Delegate.CreateDelegate(typeof(Action<TSink>), method));
    }

    private static Type? FindGeneratedType(Assembly assembly) =>
        assembly.GetType(GeneratedFactoryTypeName, throwOnError: false);

    private readonly struct SinkRegistrar<TSink>
        where TSink : class
    {
        private readonly Action<TSink>? _register;

        public SinkRegistrar(Action<TSink> register) => _register = register;

        public void Invoke(TSink sink) => _register?.Invoke(sink);
    }
}
