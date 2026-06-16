namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;

internal static class RpcKernelClientServiceMethodResolver
{
    public static IMethodSymbol Resolve(INamedTypeSymbol serviceType, IMethodSymbol kernelMethod)
    {
        if (serviceType.TypeKind != TypeKind.Interface)
        {
            throw new NotSupportedException("Kernel RPC service client generation requires an interface contract type.");
        }

        var methods = new List<IMethodSymbol>();
        foreach (var member in ServiceMembers(serviceType))
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method)
            {
                methods.Add(method);
                continue;
            }

            throw UnsupportedServiceShape(serviceType);
        }

        if (methods.Count != 1)
        {
            throw UnsupportedServiceShape(serviceType);
        }

        var serviceMethod = methods[0];
        ValidateName(serviceMethod, kernelMethod);
        ValidateParameters(serviceMethod, kernelMethod);
        ValidateReturn(serviceMethod, kernelMethod);
        return serviceMethod;
    }

    private static void ValidateName(IMethodSymbol serviceMethod, IMethodSymbol kernelMethod)
    {
        var expectedName = kernelMethod.Name;
        if (string.Equals(serviceMethod.Name, expectedName, StringComparison.Ordinal) ||
            string.Equals(serviceMethod.Name, expectedName + "Async", StringComparison.Ordinal))
        {
            return;
        }

        throw new NotSupportedException(
            $"Kernel RPC service method '{serviceMethod.Name}' must match kernel method '{expectedName}' or '{expectedName}Async'.");
    }

    private static void ValidateParameters(IMethodSymbol serviceMethod, IMethodSymbol kernelMethod)
    {
        var kernelParameterCount = kernelMethod.Parameters.Length - 1;
        if (serviceMethod.Parameters.Length != kernelParameterCount)
        {
            throw new NotSupportedException(
                $"Kernel RPC service method '{serviceMethod.Name}' must declare {kernelParameterCount} parameter(s).");
        }

        for (var i = 0; i < kernelParameterCount; i++)
        {
            var serviceParameter = serviceMethod.Parameters[i];
            var kernelParameter = kernelMethod.Parameters[i];
            RejectRefLikeParameter(serviceParameter, "service");
            RejectRefLikeParameter(kernelParameter, "kernel");
            ValidateParameterType(serviceParameter, kernelParameter);
            ValidateParameterModifiers(serviceParameter, kernelParameter);
        }
    }

    private static void ValidateParameterType(
        IParameterSymbol serviceParameter,
        IParameterSymbol kernelParameter)
    {
        if (SymbolEqualityComparer.Default.Equals(serviceParameter.Type, kernelParameter.Type))
        {
            return;
        }

        throw new NotSupportedException(
            $"Kernel RPC service parameter '{serviceParameter.Name}' must match kernel parameter '{kernelParameter.Name}'.");
    }

    private static void ValidateParameterModifiers(
        IParameterSymbol serviceParameter,
        IParameterSymbol kernelParameter)
    {
        if (serviceParameter.RefKind == kernelParameter.RefKind &&
            serviceParameter.IsParams == kernelParameter.IsParams)
        {
            return;
        }

        throw new NotSupportedException(
            $"Kernel RPC service parameter '{serviceParameter.Name}' modifier '{DescribeParameterModifiers(serviceParameter)}' must match kernel parameter '{kernelParameter.Name}' modifier '{DescribeParameterModifiers(kernelParameter)}'.");
    }

    private static void ValidateReturn(IMethodSymbol serviceMethod, IMethodSymbol kernelMethod)
    {
        if (SymbolEqualityComparer.Default.Equals(UnwrapReturn(serviceMethod.ReturnType), kernelMethod.ReturnType))
        {
            return;
        }

        throw new NotSupportedException(
            $"Kernel RPC service method '{serviceMethod.Name}' return type must match kernel method '{kernelMethod.Name}'.");
    }

    private static IEnumerable<ISymbol> ServiceMembers(INamedTypeSymbol serviceType)
    {
        foreach (var member in serviceType.GetMembers())
        {
            yield return member;
        }

        foreach (var interfaceType in serviceType.AllInterfaces)
        {
            foreach (var member in interfaceType.GetMembers())
            {
                yield return member;
            }
        }
    }

    private static NotSupportedException UnsupportedServiceShape(INamedTypeSymbol serviceType)
        => new(
            $"Kernel RPC service interface '{serviceType.ToDisplayString()}' must declare exactly one method and no other members.");

    private static void RejectRefLikeParameter(IParameterSymbol parameter, string owner)
    {
        if (parameter.RefKind == RefKind.None)
        {
            return;
        }

        throw new NotSupportedException(
            $"Kernel RPC {owner} parameter '{parameter.Name}' cannot use ref, in, or out modifiers.");
    }

    private static ITypeSymbol UnwrapReturn(ITypeSymbol type)
        => IsGenericTask(type, out var inner) || IsGenericValueTask(type, out inner) ? inner : type;

    private static string DescribeParameterModifiers(IParameterSymbol parameter)
    {
        var modifier = parameter.RefKind switch
        {
            RefKind.Ref => "ref",
            RefKind.In => "in",
            RefKind.Out => "out",
            RefKind.None => "none",
            _ => parameter.RefKind.ToString()
        };

        return parameter.IsParams
            ? modifier == "none" ? "params" : "params " + modifier
            : modifier;
    }

    private static bool IsGenericTask(ITypeSymbol type, out ITypeSymbol inner)
        => TryGenericTaskLike(type, "Task", out inner);

    private static bool IsGenericValueTask(ITypeSymbol type, out ITypeSymbol inner)
        => TryGenericTaskLike(type, "ValueTask", out inner);

    private static bool TryGenericTaskLike(ITypeSymbol type, string name, out ITypeSymbol inner)
    {
        if (type is INamedTypeSymbol
            {
                IsGenericType: true,
                TypeArguments.Length: 1,
                Name: var typeName,
                ContainingNamespace: { } ns
            } named &&
            string.Equals(typeName, name, StringComparison.Ordinal) &&
            string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
        {
            inner = named.TypeArguments[0];
            return true;
        }

        inner = type;
        return false;
    }
}
