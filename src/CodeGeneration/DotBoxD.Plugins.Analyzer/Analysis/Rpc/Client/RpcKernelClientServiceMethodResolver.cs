namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

internal static class RpcKernelClientServiceMethodResolver
{
    public static IMethodSymbol Resolve(INamedTypeSymbol serviceType, IMethodSymbol kernelMethod)
    {
        if (serviceType.TypeKind != TypeKind.Interface)
        {
            throw new NotSupportedException("Server extension client generation requires an interface contract type.");
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
            $"Server extension method '{serviceMethod.Name}' must match kernel method '{expectedName}' or '{expectedName}Async'.");
    }

    private static void ValidateParameters(IMethodSymbol serviceMethod, IMethodSymbol kernelMethod)
    {
        var kernelParameterCount = kernelMethod.Parameters.Length - 1;
        if (serviceMethod.Parameters.Length != kernelParameterCount)
        {
            throw new NotSupportedException(
                $"Server extension method '{serviceMethod.Name}' must declare {kernelParameterCount} parameter(s).");
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
            $"Server extension parameter '{serviceParameter.Name}' must match kernel parameter '{kernelParameter.Name}'.");
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
            $"Server extension parameter '{serviceParameter.Name}' modifier '{DescribeParameterModifiers(serviceParameter)}' must match kernel parameter '{kernelParameter.Name}' modifier '{DescribeParameterModifiers(kernelParameter)}'.");
    }

    private static void ValidateReturn(IMethodSymbol serviceMethod, IMethodSymbol kernelMethod)
    {
        var serviceReturn = DotBoxDTypeNameReader.UnwrapTaskLike(serviceMethod.ReturnType);
        var kernelReturn = DotBoxDTypeNameReader.UnwrapTaskLike(kernelMethod.ReturnType);
        if (SymbolEqualityComparer.Default.Equals(serviceReturn, kernelReturn))
        {
            return;
        }

        throw new NotSupportedException(
            $"Server extension method '{serviceMethod.Name}' return type must match kernel method '{kernelMethod.Name}'.");
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
            $"Server extension interface '{serviceType.ToDisplayString()}' must declare exactly one method and no other members.");

    private static void RejectRefLikeParameter(IParameterSymbol parameter, string owner)
    {
        if (parameter.RefKind == RefKind.None)
        {
            return;
        }

        throw new NotSupportedException(
            $"Server extension {owner} parameter '{parameter.Name}' cannot use ref, in, or out modifiers.");
    }

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
}
