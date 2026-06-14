namespace SafeIR.Plugins;

using System.Reflection;

/// <summary>
/// Resolves a kernel CLR type to its analyzer-generated <see cref="PluginPackage"/>, so a host or a
/// plugin shim can ship a kernel "by type" (e.g. <c>Register&lt;TService, TKernel&gt;()</c>) without
/// naming the generated <c>{Kernel}PluginPackage</c> explicitly.
/// </summary>
/// <remarks>
/// Generated packages may self-register a factory via a <c>[ModuleInitializer]</c> for NativeAOT/
/// trimming friendliness (call <see cref="Register"/>). When no factory is registered, the package is
/// resolved by the generator's naming convention — a public static <c>{KernelName}PluginPackage.Create()</c>
/// in the kernel's assembly — via reflection.
/// </remarks>
public static class KernelPackageRegistry
{
    private const string PackageSuffix = "PluginPackage";
    private const string KernelSuffix = "Kernel";
    private const string FactoryMethod = "Create";

    private static readonly object Gate = new();
    private static readonly Dictionary<Type, Func<PluginPackage>> Factories = [];

    /// <summary>Registers a package factory for a kernel type (typically from a generated [ModuleInitializer]).</summary>
    public static void Register(Type kernelType, Func<PluginPackage> factory)
    {
        ArgumentNullException.ThrowIfNull(kernelType);
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate)
        {
            Factories[kernelType] = factory;
        }
    }

    /// <summary>Resolves the generated package for <typeparamref name="TKernel"/>.</summary>
    public static PluginPackage Resolve<TKernel>() where TKernel : class => Resolve(typeof(TKernel));

    /// <summary>Resolves the generated package for a kernel type.</summary>
    public static PluginPackage Resolve(Type kernelType)
    {
        ArgumentNullException.ThrowIfNull(kernelType);
        Func<PluginPackage>? factory;
        lock (Gate)
        {
            Factories.TryGetValue(kernelType, out factory);
        }

        factory ??= ReflectPackageFactory(kernelType);
        return factory();
    }

    private static Func<PluginPackage> ReflectPackageFactory(Type kernelType)
    {
        // Generator naming convention (PluginKernelModelFactory.PackageName): strip a trailing
        // "Kernel" from the class name, then append "PluginPackage" (e.g. GuardianKernel -> GuardianPluginPackage).
        var baseName = kernelType.Name.EndsWith(KernelSuffix, StringComparison.Ordinal)
            ? kernelType.Name[..^KernelSuffix.Length]
            : kernelType.Name;
        var packageName = baseName + PackageSuffix;
        var fullName = string.IsNullOrEmpty(kernelType.Namespace)
            ? packageName
            : kernelType.Namespace + "." + packageName;
        var create = kernelType.Assembly
            .GetType(fullName)?
            .GetMethod(FactoryMethod, BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        if (create is null || !typeof(PluginPackage).IsAssignableFrom(create.ReturnType))
        {
            throw new InvalidOperationException(
                $"No generated package found for kernel '{kernelType.FullName}'. " +
                $"Expected a public static '{fullName}.{FactoryMethod}()' returning a PluginPackage, " +
                "or an explicit KernelPackageRegistry.Register for this type.");
        }

        return () => (PluginPackage)create.Invoke(null, null)!;
    }
}
