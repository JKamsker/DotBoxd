using DotBoxD.Services.Server;

namespace DotBoxD.Services.Generated;

internal static class GeneratedServiceMetadataValidator
{
    public static void ValidateForRegistration<TService>(GeneratedService service, string paramName)
        where TService : class
    {
        ValidateServiceShape(service, paramName);
        if (service.ServiceType != typeof(TService))
        {
            throw new ArgumentException(
                $"Generated service metadata describes {FormatType(service.ServiceType)}, " +
                $"but it was registered for {FormatType(typeof(TService))}.",
                paramName);
        }
        ValidateImplementationTypes(service, paramName);
        ValidateMethods(service.Methods, paramName);
    }

    public static void Validate(
        GeneratedService service,
        string paramName,
        bool validateImplementationTypes = true)
    {
        ValidateServiceShape(service, paramName);
        if (validateImplementationTypes)
        {
            ValidateImplementationTypes(service, paramName);
        }
        ValidateMethods(service.Methods, paramName);
    }

    private static void ValidateServiceShape(GeneratedService service, string paramName)
    {
        if (service.ServiceType is null)
        {
            throw new ArgumentException("Generated service metadata must include a service type.", paramName);
        }
        if (service.ProxyType is null)
        {
            throw new ArgumentException("Generated service metadata must include a proxy type.", paramName);
        }
        if (service.DispatcherType is null)
        {
            throw new ArgumentException("Generated service metadata must include a dispatcher type.", paramName);
        }
        if (string.IsNullOrEmpty(service.ServiceName))
        {
            throw new ArgumentException("Generated service metadata must include a service name.", paramName);
        }
    }

    private static void ValidateImplementationTypes(GeneratedService service, string paramName)
    {
        if (!service.ServiceType.IsAssignableFrom(service.ProxyType))
        {
            throw new ArgumentException(
                $"Generated proxy type {FormatType(service.ProxyType)} must implement " +
                $"{FormatType(service.ServiceType)}.",
                paramName);
        }
        if (!typeof(IServiceDispatcher).IsAssignableFrom(service.DispatcherType))
        {
            throw new ArgumentException(
                $"Generated dispatcher type {FormatType(service.DispatcherType)} must implement " +
                $"{FormatType(typeof(IServiceDispatcher))}.",
                paramName);
        }
    }

    private static void ValidateMethods(IReadOnlyList<GeneratedMethod>? methods, string paramName)
    {
        if (methods is null)
        {
            return;
        }

        for (var i = 0; i < methods.Count; i++)
        {
            ValidateMethod(methods[i], paramName);
        }
    }

    private static void ValidateMethod(GeneratedMethod method, string paramName)
    {
        if (string.IsNullOrEmpty(method.Name))
        {
            throw new ArgumentException("Generated method metadata must include a method name.", paramName);
        }
        if (string.IsNullOrEmpty(method.WireName))
        {
            throw new ArgumentException("Generated method metadata must include a wire name.", paramName);
        }
        if (method.ReturnType is null)
        {
            throw new ArgumentException("Generated method metadata must include a return type.", paramName);
        }
        if (method.ReturnKind is < GeneratedReturnKind.Void or > GeneratedReturnKind.ValueTaskOfPipe)
        {
            throw new ArgumentException("Generated method metadata has an unsupported return kind.", paramName);
        }

        ValidateParameters(method.Parameters, paramName);
    }

    private static void ValidateParameters(IReadOnlyList<GeneratedParameter>? parameters, string paramName)
    {
        if (parameters is null)
        {
            return;
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (string.IsNullOrEmpty(parameter.Name))
            {
                throw new ArgumentException("Generated parameter metadata must include a parameter name.", paramName);
            }
            if (parameter.Type is null)
            {
                throw new ArgumentException("Generated parameter metadata must include a parameter type.", paramName);
            }
            if (parameter.Position != i)
            {
                throw new ArgumentException("Generated parameter metadata positions must be zero-based and ordered.", paramName);
            }
        }
    }

    private static string FormatType(Type type) => type.FullName ?? type.Name;
}
