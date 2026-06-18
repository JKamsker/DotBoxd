using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotBoxD.Kernels.Verifier.Generated;

internal static class MetadataName
{
    public static string TypeReference(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string TypeDefinition(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string Type(MetadataReader reader, EntityHandle handle)
        => handle.Kind switch {
            HandleKind.TypeReference => TypeReference(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeDefinition => TypeDefinition(reader, (TypeDefinitionHandle)handle),
            _ => handle.Kind.ToString()
        };

    public static (string TypeName, string MemberName) Member(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MemberReference) {
            var member = reader.GetMemberReference((MemberReferenceHandle)handle);
            return (ParentName(reader, member.Parent), reader.GetString(member.Name));
        }

        if (handle.Kind == HandleKind.MethodDefinition) {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            return ("", reader.GetString(method.Name));
        }

        if (handle.Kind == HandleKind.MethodSpecification) {
            var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
            return Member(reader, spec.Method);
        }

        return ("", handle.Kind.ToString());
    }

    public static (string TypeName, string MemberName, string Signature) MemberSignature(
        MetadataReader reader,
        EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MemberReference) {
            var member = reader.GetMemberReference((MemberReferenceHandle)handle);
            var typeName = ParentName(reader, member.Parent);
            var memberName = reader.GetString(member.Name);
            if (member.GetKind() != MemberReferenceKind.Method) {
                return (typeName, memberName, $"{typeName}.{memberName}:field");
            }

            return (typeName, memberName, MethodSignature(
                typeName,
                memberName,
                member.DecodeMethodSignature(MethodSignatureNameProvider.Instance, genericContext: null)));
        }

        if (handle.Kind == HandleKind.MethodDefinition) {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            var memberName = reader.GetString(method.Name);
            return ("", memberName, MethodSignature(
                "",
                memberName,
                method.DecodeSignature(MethodSignatureNameProvider.Instance, genericContext: null)));
        }

        return ("", handle.Kind.ToString(), handle.Kind.ToString());
    }

    private static string ParentName(MetadataReader reader, EntityHandle parent)
        => parent.Kind switch {
            HandleKind.TypeReference => TypeReference(reader, (TypeReferenceHandle)parent),
            HandleKind.TypeDefinition => TypeDefinition(reader, (TypeDefinitionHandle)parent),
            HandleKind.TypeSpecification => "TypeSpecification",
            _ => parent.Kind.ToString()
        };

    private static string MethodSignature(
        string typeName,
        string memberName,
        MethodSignature<string> signature)
    {
        var prefix = string.IsNullOrEmpty(typeName) ? memberName : typeName + "." + memberName;
        return prefix + "(" + string.Join(",", signature.ParameterTypes) + "):" + signature.ReturnType;
    }
}

internal sealed class MemberSignatureCache
{
    private readonly Dictionary<int, (string TypeName, string MemberName, string Signature)> _items = [];

    public (string TypeName, string MemberName, string Signature) Get(MetadataReader reader, EntityHandle handle)
    {
        var token = MetadataTokens.GetToken(handle);
        if (_items.TryGetValue(token, out var cached))
        {
            return cached;
        }

        var member = MetadataName.MemberSignature(reader, handle);
        _items[token] = member;
        return member;
    }
}
