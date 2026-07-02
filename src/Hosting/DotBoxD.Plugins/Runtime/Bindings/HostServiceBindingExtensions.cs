using System.Reflection;
using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Execution;

public static class HostServiceBindingExtensions
{
    private const string ExtensibleControlType = "DotBoxD.Abstractions.IExtensibleControl";
    private const string ServiceControlType = "DotBoxD.Abstractions.IServiceControl";
    private const string RpcServiceAttributeType = "DotBoxD.Services.Attributes.RpcServiceAttribute";
    private const string DotBoxDServiceAttributeType = "DotBoxD.Services.Attributes.DotBoxDServiceAttribute";

    public static SandboxHostBuilder AddBindingsFrom<TService>(
        this SandboxHostBuilder builder,
        TService implementation)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(implementation);

        var visited = new HashSet<Type>();
        var registeredBindings = new Dictionary<string, HostServiceBindingRegistration>(StringComparer.Ordinal);
        AddServiceBindings(builder, typeof(TService), implementation, visited, registeredBindings);
        return builder;
    }

    private static void AddServiceBindings(
        SandboxHostBuilder builder,
        Type serviceType,
        object implementation,
        HashSet<Type> visited,
        Dictionary<string, HostServiceBindingRegistration> registeredBindings)
    {
        if (!serviceType.IsInterface)
        {
            throw new InvalidOperationException(
                $"Host service contract '{serviceType.FullName}' must be an interface.");
        }

        if (!visited.Add(serviceType))
        {
            return;
        }

        foreach (var method in ServiceMethods(serviceType))
        {
            if (ShouldSkipMethod(method))
            {
                continue;
            }

            if (TryAddHandleServiceBindings(builder, serviceType, implementation, method, registeredBindings))
            {
                continue;
            }

            var declaringType = method.DeclaringType ?? serviceType;
            var target = ResolveTargetMethod(declaringType, implementation.GetType(), method);
            var binding = method.GetCustomAttribute<HostBindingAttribute>();
            if (binding is null)
            {
                throw new InvalidOperationException(
                    $"Host service method '{declaringType.FullName}.{method.Name}' must declare [HostBinding] on its service contract.");
            }

            AddBinding(
                builder,
                registeredBindings,
                HostServiceBindingFactory.CreateBinding(method, target, implementation, binding),
                HostServiceBindingRouteSignature.ForMethod(method));
        }

        foreach (var property in ServiceProperties(serviceType))
        {
            if (ShouldSkipProperty(property))
            {
                continue;
            }

            if (TryAddPropertyBinding(builder, serviceType, implementation, property, registeredBindings))
            {
                continue;
            }

            if (!HasDotBoxDServiceAttribute(property.PropertyType))
            {
                continue;
            }

            var child = ReadPropertyValue(serviceType, implementation, property);
            if (child is null)
            {
                throw new InvalidOperationException(
                    $"Host service property '{serviceType.FullName}.{property.Name}' returned null.");
            }

            AddServiceBindings(builder, property.PropertyType, child, visited, registeredBindings);
        }
    }

    private static bool TryAddHandleServiceBindings(
        SandboxHostBuilder builder,
        Type parentServiceType,
        object parentImplementation,
        MethodInfo factoryMethod,
        Dictionary<string, HostServiceBindingRegistration> registeredBindings)
    {
        if (HostServiceBindingFactory.UnwrapReturnType(factoryMethod.ReturnType) is not { } handleServiceType ||
            !HasDotBoxDServiceAttribute(handleServiceType))
        {
            return false;
        }

        if (!handleServiceType.IsInterface)
        {
            throw new InvalidOperationException(
                $"Host service contract '{handleServiceType.FullName}' must be an interface.");
        }

        var factoryDeclaringType = factoryMethod.DeclaringType ?? parentServiceType;
        var targetFactory = ResolveTargetMethod(factoryDeclaringType, parentImplementation.GetType(), factoryMethod);
        foreach (var handleMethod in ServiceMethods(handleServiceType))
        {
            if (ShouldSkipMethod(handleMethod))
            {
                continue;
            }

            var binding = handleMethod.GetCustomAttribute<HostBindingAttribute>();
            if (binding is null)
            {
                throw new InvalidOperationException(
                    $"Host service handle method '{handleServiceType.FullName}.{handleMethod.Name}' must declare [HostBinding].");
            }

            AddBinding(
                builder,
                registeredBindings,
                HostServiceBindingFactory.CreateHandleBinding(
                    factoryMethod,
                    targetFactory,
                    parentImplementation,
                    handleMethod,
                    binding),
                HostServiceBindingRouteSignature.ForHandle(factoryMethod, handleMethod));
        }

        return true;
    }

    private static bool TryAddPropertyBinding(
        SandboxHostBuilder builder,
        Type serviceType,
        object implementation,
        PropertyInfo property,
        Dictionary<string, HostServiceBindingRegistration> registeredBindings)
    {
        var binding = property.GetCustomAttribute<HostBindingAttribute>();
        if (binding is null)
        {
            return false;
        }

        if (binding.IsAutoBinding)
        {
            throw new InvalidOperationException(
                $"Host service property '{serviceType.FullName}.{property.Name}' must declare an explicit binding id.");
        }

        var getter = property.GetMethod
            ?? throw new InvalidOperationException($"Host service property '{property.Name}' must have a getter.");
        var declaringType = getter.DeclaringType ?? serviceType;
        var targetGetter = ResolveTargetMethod(declaringType, implementation.GetType(), getter);
        AddBinding(
            builder,
            registeredBindings,
            HostServiceBindingFactory.CreatePropertyBinding(property, targetGetter, implementation, binding),
            HostServiceBindingRouteSignature.ForProperty(property));
        return true;
    }

    private static void AddBinding(
        SandboxHostBuilder builder,
        Dictionary<string, HostServiceBindingRegistration> registeredBindings,
        BindingDescriptor descriptor,
        HostServiceBindingRouteSignature signature)
    {
        if (!registeredBindings.TryAdd(descriptor.Id, new HostServiceBindingRegistration(descriptor, signature)))
        {
            var existing = registeredBindings[descriptor.Id];
            if (BindingShapesMatch(existing.Descriptor, descriptor))
            {
                if (existing.Signature.Matches(signature))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Host service duplicate host binding route '{descriptor.Id}' maps to the same positional " +
                    "sandbox shape with different CLR contract or DTO field names. Overloaded host service " +
                    "methods must use distinct binding routes.");
            }

            throw new InvalidOperationException(
                $"Host service duplicate host binding route '{descriptor.Id}' maps to multiple service member shapes. " +
                "Overloaded host service methods must use distinct binding routes.");
        }

        builder.AddBinding(descriptor);
    }

    private static bool BindingShapesMatch(BindingDescriptor left, BindingDescriptor right)
    {
        return left.Version == right.Version &&
               left.ReturnType == right.ReturnType &&
               left.Effects == right.Effects &&
               string.Equals(left.RequiredCapability, right.RequiredCapability, StringComparison.Ordinal) &&
               left.CostModel == right.CostModel &&
               left.AuditLevel == right.AuditLevel &&
               left.Safety == right.Safety &&
               left.Compiled == right.Compiled &&
               left.IsAsync == right.IsAsync &&
               left.Parameters.SequenceEqual(right.Parameters);
    }

    private static MethodInfo ResolveTargetMethod(Type interfaceType, Type implementationType, MethodInfo method)
    {
        var map = implementationType.GetInterfaceMap(interfaceType);
        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            if (map.InterfaceMethods[i] == method)
            {
                return map.TargetMethods[i];
            }
        }

        throw new InvalidOperationException(
            $"Host service implementation '{implementationType.FullName}' does not implement '{interfaceType.FullName}.{method.Name}'.");
    }

    private static object? ReadPropertyValue(Type interfaceType, object implementation, PropertyInfo property)
    {
        var getter = property.GetMethod
            ?? throw new InvalidOperationException($"Host service property '{property.Name}' must have a getter.");
        var getterDeclaringType = getter.DeclaringType ?? interfaceType;
        var targetGetter = ResolveTargetMethod(getterDeclaringType, implementation.GetType(), getter);
        return targetGetter.Invoke(implementation, null);
    }

    private static IEnumerable<MethodInfo> ServiceMethods(Type serviceType)
        => ServiceTypes(serviceType).SelectMany(static type => type.GetMethods());

    private static IEnumerable<PropertyInfo> ServiceProperties(Type serviceType)
        => ServiceTypes(serviceType).SelectMany(static type => type.GetProperties());

    private static IEnumerable<Type> ServiceTypes(Type serviceType)
    {
        foreach (var inherited in serviceType.GetInterfaces().OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            yield return inherited;
        }

        yield return serviceType;
    }

    private static bool ShouldSkipMethod(MethodInfo method)
        => method.IsSpecialName ||
           method.IsGenericMethodDefinition ||
           IsControlType(method.DeclaringType);

    private static bool ShouldSkipProperty(PropertyInfo property)
        => property.GetMethod is null ||
           property.GetMethod.IsStatic ||
           property.GetIndexParameters().Length != 0 ||
           IsControlType(property.DeclaringType);

    private static bool IsControlType(MemberInfo? type)
        => type is Type t &&
           (string.Equals(t.FullName, ExtensibleControlType, StringComparison.Ordinal) ||
            string.Equals(t.FullName, ServiceControlType, StringComparison.Ordinal));

    private static bool HasDotBoxDServiceAttribute(Type type)
        => HasDirectDotBoxDServiceAttribute(type) || type.GetInterfaces().Any(HasDirectDotBoxDServiceAttribute);

    private static bool HasDirectDotBoxDServiceAttribute(Type type)
        => type.GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().FullName is RpcServiceAttributeType or DotBoxDServiceAttributeType);
}
