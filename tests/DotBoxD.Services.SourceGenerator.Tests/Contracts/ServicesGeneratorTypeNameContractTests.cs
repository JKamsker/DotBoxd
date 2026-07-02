using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Generated;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;

namespace DotBoxD.Services.SourceGenerator.Tests.Contracts;

public sealed class ServicesGeneratorTypeNameContractTests
{
    [Fact]
    public void Type_name_constants_match_referenced_contracts()
    {
        var expected = ExpectedTypeNames();
        var actual = typeof(ServicesGeneratorTypeNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!, StringComparer.Ordinal);

        Assert.Equal(expected.Keys.OrderBy(static key => key), actual.Keys.OrderBy(static key => key));
        foreach (var (name, expectedValue) in expected)
        {
            Assert.Equal(expectedValue, actual[name]);
        }
    }

    private static Dictionary<string, string> ExpectedTypeNames()
        => new(StringComparer.Ordinal)
        {
            [nameof(ServicesGeneratorTypeNames.GlobalPrefix)] = "global::",

            [nameof(ServicesGeneratorTypeNames.RpcServiceAttribute)] = TypeName(typeof(RpcServiceAttribute)),
            [nameof(ServicesGeneratorTypeNames.RpcMethodAttribute)] = TypeName(typeof(RpcMethodAttribute)),
            [nameof(ServicesGeneratorTypeNames.DotBoxDServiceAttribute)] =
                "DotBoxD.Services.Attributes.DotBoxDServiceAttribute",
            [nameof(ServicesGeneratorTypeNames.DotBoxDMethodAttribute)] =
                "DotBoxD.Services.Attributes.DotBoxDMethodAttribute",
            [nameof(ServicesGeneratorTypeNames.CancellationTokenMetadata)] = TypeName(typeof(CancellationToken)),

            [nameof(ServicesGeneratorTypeNames.GeneratedNamespace)] = typeof(GeneratedService).Namespace!,
            [nameof(ServicesGeneratorTypeNames.GeneratedFactoryType)] = "DotBoxDGenerated",
            [nameof(ServicesGeneratorTypeNames.GeneratedExtensionsType)] = "DotBoxDGeneratedExtensions",

            [nameof(ServicesGeneratorTypeNames.SystemCollectionsGenericNamespace)] = typeof(IAsyncEnumerable<>).Namespace!,
            [nameof(ServicesGeneratorTypeNames.SystemIoNamespace)] = typeof(Stream).Namespace!,
            [nameof(ServicesGeneratorTypeNames.SystemIoPipelinesNamespace)] = typeof(Pipe).Namespace!,
            [nameof(ServicesGeneratorTypeNames.SystemThreadingTasksNamespace)] = typeof(Task).Namespace!,

            [nameof(ServicesGeneratorTypeNames.GlobalArgumentNullException)] = GlobalTypeName(typeof(ArgumentNullException)),
            [nameof(ServicesGeneratorTypeNames.GlobalArray)] = GlobalTypeName(typeof(Array)),
            [nameof(ServicesGeneratorTypeNames.GlobalBufferWriter)] = GlobalTypeName(typeof(IBufferWriter<>)),
            [nameof(ServicesGeneratorTypeNames.GlobalCancellationToken)] = GlobalTypeName(typeof(CancellationToken)),
            [nameof(ServicesGeneratorTypeNames.GlobalException)] = GlobalTypeName(typeof(Exception)),
            [nameof(ServicesGeneratorTypeNames.GlobalIAsyncDisposable)] = GlobalTypeName(typeof(IAsyncDisposable)),
            [nameof(ServicesGeneratorTypeNames.GlobalIAsyncEnumerable)] = GlobalTypeName(typeof(IAsyncEnumerable<>)),
            [nameof(ServicesGeneratorTypeNames.GlobalIDisposable)] = GlobalTypeName(typeof(IDisposable)),
            [nameof(ServicesGeneratorTypeNames.GlobalNotSupportedException)] = GlobalTypeName(typeof(NotSupportedException)),
            [nameof(ServicesGeneratorTypeNames.GlobalObject)] = GlobalTypeName(typeof(object)),
            [nameof(ServicesGeneratorTypeNames.GlobalOperationCanceledException)] = GlobalTypeName(typeof(OperationCanceledException)),
            [nameof(ServicesGeneratorTypeNames.GlobalReadOnlyList)] = GlobalTypeName(typeof(IReadOnlyList<>)),
            [nameof(ServicesGeneratorTypeNames.GlobalReadOnlyMemory)] = GlobalTypeName(typeof(ReadOnlyMemory<>)),
            [nameof(ServicesGeneratorTypeNames.GlobalStream)] = GlobalTypeName(typeof(Stream)),
            [nameof(ServicesGeneratorTypeNames.GlobalTask)] = GlobalTypeName(typeof(Task)),
            [nameof(ServicesGeneratorTypeNames.GlobalType)] = GlobalTypeName(typeof(Type)),
            [nameof(ServicesGeneratorTypeNames.GlobalValueTask)] = GlobalTypeName(typeof(ValueTask)),
            [nameof(ServicesGeneratorTypeNames.GlobalEnumeratorCancellationAttribute)] =
                GlobalAttributeName(typeof(EnumeratorCancellationAttribute)),
            [nameof(ServicesGeneratorTypeNames.GlobalPipe)] = GlobalTypeName(typeof(Pipe)),

            [nameof(ServicesGeneratorTypeNames.GlobalDotBoxDGenerated)] =
                ServicesGeneratorTypeNames.GlobalPrefix +
                ServicesGeneratorTypeNames.GeneratedNamespace + "." +
                ServicesGeneratorTypeNames.GeneratedFactoryType,
            [nameof(ServicesGeneratorTypeNames.GlobalGeneratedMethod)] = GlobalTypeName(typeof(GeneratedMethod)),
            [nameof(ServicesGeneratorTypeNames.GlobalGeneratedParameter)] = GlobalTypeName(typeof(GeneratedParameter)),
            [nameof(ServicesGeneratorTypeNames.GlobalGeneratedReturnKind)] = GlobalTypeName(typeof(GeneratedReturnKind)),
            [nameof(ServicesGeneratorTypeNames.GlobalGeneratedService)] = GlobalTypeName(typeof(GeneratedService)),
            [nameof(ServicesGeneratorTypeNames.GlobalGeneratedServiceRegistry)] =
                GlobalTypeName(typeof(GeneratedServiceRegistry)),
            [nameof(ServicesGeneratorTypeNames.GlobalServiceRegistrationSink)] =
                GlobalTypeName(typeof(IRpcServiceRegistrationSink)),
            [nameof(ServicesGeneratorTypeNames.GlobalGeneratedServiceRegistrationSink)] =
                GlobalTypeName(typeof(IRpcGeneratedServiceRegistrationSink)),

            [nameof(ServicesGeneratorTypeNames.GlobalRpcPeer)] = GlobalTypeName(typeof(RpcPeer)),
            [nameof(ServicesGeneratorTypeNames.GlobalRpcStreamHandle)] = GlobalTypeName(typeof(RpcStreamHandle)),
            [nameof(ServicesGeneratorTypeNames.GlobalRpcStreamKind)] = GlobalTypeName(typeof(RpcStreamKind)),
            [nameof(ServicesGeneratorTypeNames.GlobalServiceHandle)] = GlobalTypeName(typeof(ServiceHandle)),
            [nameof(ServicesGeneratorTypeNames.GlobalInstanceRegistry)] = GlobalTypeName(typeof(IInstanceRegistry)),
            [nameof(ServicesGeneratorTypeNames.GlobalRpcInvoker)] = GlobalTypeName(typeof(IRpcInvoker)),
            [nameof(ServicesGeneratorTypeNames.GlobalServiceDispatcher)] = GlobalTypeName(typeof(IServiceDispatcher)),
            [nameof(ServicesGeneratorTypeNames.GlobalNonStreamingServiceDispatcher)] =
                GlobalTypeName(typeof(INonStreamingServiceDispatcher)),
            [nameof(ServicesGeneratorTypeNames.GlobalSerializer)] = GlobalTypeName(typeof(ISerializer)),
            [nameof(ServicesGeneratorTypeNames.GlobalRpcStreamAttachment)] = GlobalTypeName(typeof(RpcStreamAttachment)),
            [nameof(ServicesGeneratorTypeNames.GlobalRpcStreamingContext)] = GlobalTypeName(typeof(RpcStreamingContext)),
            [nameof(ServicesGeneratorTypeNames.GlobalRpcStreamingContextInterface)] =
                GlobalTypeName(typeof(IRpcStreamingContext)),
            [nameof(ServicesGeneratorTypeNames.GlobalServiceNotFoundException)] =
                GlobalTypeName(typeof(ServiceNotFoundException)),
            [nameof(ServicesGeneratorTypeNames.GlobalServiceNotFoundKind)] =
                GlobalTypeName(typeof(ServiceNotFoundException)) + ".NotFoundKind",
            [nameof(ServicesGeneratorTypeNames.GlobalServiceProtocolException)] =
                GlobalTypeName(typeof(ServiceProtocolException)),
        };

    private static string GlobalTypeName(Type type) => ServicesGeneratorTypeNames.GlobalPrefix + TypeName(type);

    private static string GlobalAttributeName(Type type) => ServicesGeneratorTypeNames.GlobalPrefix + AttributeName(type);

    private static string AttributeName(Type type)
    {
        var typeName = TypeName(type);
        return typeName.EndsWith(nameof(Attribute), StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - nameof(Attribute).Length)
            : typeName;
    }

    private static string TypeName(Type type)
    {
        var name = type.Name;
        var genericMarker = name.IndexOf('`');
        if (genericMarker >= 0)
        {
            name = name.Substring(0, genericMarker);
        }

        return string.IsNullOrEmpty(type.Namespace) ? name : type.Namespace + "." + name;
    }
}
