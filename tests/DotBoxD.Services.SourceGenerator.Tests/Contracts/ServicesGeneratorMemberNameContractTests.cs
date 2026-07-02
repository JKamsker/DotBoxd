using System.Reflection;
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

/// <summary>
/// Pins every member name the generated proxies/dispatchers bind to (held in
/// <see cref="ServicesGeneratorMemberNames"/>) to the real runtime symbol via <c>nameof</c>. Renaming or
/// removing any bound member in DotBoxD.Services fails this test — either at compile time (the
/// <c>nameof</c> stops resolving) or at assertion time — instead of silently emitting code that no longer
/// compiles in consumer projects. The key-set assertion additionally fails the moment a new constant is
/// added without a matching anchor here.
/// </summary>
public sealed class ServicesGeneratorMemberNameContractTests
{
    [Fact]
    public void Member_name_constants_match_referenced_runtime_members()
    {
        var expected = ExpectedMemberNames();

        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in typeof(ServicesGeneratorMemberNames).GetNestedTypes(BindingFlags.Public))
        {
            foreach (var field in group.GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string)))
            {
                actual[group.Name + "." + field.Name] = (string)field.GetRawConstantValue()!;
            }
        }

        Assert.Equal(expected.Keys.OrderBy(static key => key), actual.Keys.OrderBy(static key => key));
        foreach (var (name, expectedValue) in expected)
        {
            Assert.Equal(expectedValue, actual[name]);
        }
    }

    private static Dictionary<string, string> ExpectedMemberNames()
        => new(StringComparer.Ordinal)
        {
            ["RpcInvoker.InvokeAsync"] = nameof(IRpcInvoker.InvokeAsync),
            ["RpcInvoker.InvokeOnInstanceAsync"] = nameof(IRpcInvoker.InvokeOnInstanceAsync),
            ["RpcInvoker.InvokeValueAsync"] = nameof(IRpcInvoker.InvokeValueAsync),
            ["RpcInvoker.InvokeValueOnInstanceAsync"] = nameof(IRpcInvoker.InvokeValueOnInstanceAsync),
            ["RpcInvoker.InvokeStreamAsync"] = nameof(IRpcInvoker.InvokeStreamAsync),
            ["RpcInvoker.InvokeStreamOnInstanceAsync"] = nameof(IRpcInvoker.InvokeStreamOnInstanceAsync),
            ["RpcInvoker.InvokePipeAsync"] = nameof(IRpcInvoker.InvokePipeAsync),
            ["RpcInvoker.InvokePipeOnInstanceAsync"] = nameof(IRpcInvoker.InvokePipeOnInstanceAsync),
            ["RpcInvoker.InvokeAsyncEnumerable"] = nameof(IRpcInvoker.InvokeAsyncEnumerable),
            ["RpcInvoker.InvokeAsyncEnumerableOnInstance"] = nameof(IRpcInvoker.InvokeAsyncEnumerableOnInstance),
            ["RpcInvoker.InvokeAsyncEnumerableAsync"] = nameof(IRpcInvoker.InvokeAsyncEnumerableAsync),
            ["RpcInvoker.InvokeAsyncEnumerableOnInstanceAsync"] = nameof(IRpcInvoker.InvokeAsyncEnumerableOnInstanceAsync),
            ["RpcInvoker.ReserveStream"] = nameof(IRpcInvoker.ReserveStream),
            ["RpcInvoker.ReleaseStream"] = nameof(IRpcInvoker.ReleaseStream),

            ["ServiceDispatcher.ServiceName"] = nameof(IServiceDispatcher.ServiceName),
            ["ServiceDispatcher.DispatchAsync"] = nameof(IServiceDispatcher.DispatchAsync),
            ["ServiceDispatcher.DispatchOnInstanceAsync"] = nameof(IServiceDispatcher.DispatchOnInstanceAsync),

            ["InstanceRegistry.TryGet"] = nameof(IInstanceRegistry.TryGet),
            ["InstanceRegistry.Register"] = nameof(IInstanceRegistry.Register),
            ["InstanceRegistry.ReleaseAsync"] = nameof(IInstanceRegistry.ReleaseAsync),

            ["Serializer.Serialize"] = nameof(ISerializer.Serialize),
            ["Serializer.Deserialize"] = nameof(ISerializer.Deserialize),

            ["RpcStreamingContext.GetStream"] = nameof(IRpcStreamingContext.GetStream),
            ["RpcStreamingContext.GetPipe"] = nameof(IRpcStreamingContext.GetPipe),
            ["RpcStreamingContext.GetAsyncEnumerable"] = nameof(IRpcStreamingContext.GetAsyncEnumerable),
            ["RpcStreamingContext.SetResponse"] = nameof(IRpcStreamingContext.SetResponse),
            ["RpcStreamingContext.Disabled"] = nameof(RpcStreamingContext.Disabled),

            ["RpcStreamAttachment.FromStream"] = nameof(RpcStreamAttachment.FromStream),
            ["RpcStreamAttachment.FromPipe"] = nameof(RpcStreamAttachment.FromPipe),
            ["RpcStreamAttachment.FromAsyncEnumerable"] = nameof(RpcStreamAttachment.FromAsyncEnumerable),

            ["RpcStreamKind.Binary"] = nameof(RpcStreamKind.Binary),
            ["RpcStreamKind.Items"] = nameof(RpcStreamKind.Items),

            ["RpcPeer.Provide"] = nameof(RpcPeer.Provide),
            ["RpcPeer.Get"] = nameof(RpcPeer.Get),

            ["GeneratedServiceRegistry.RegisterServices"] = nameof(GeneratedServiceRegistry.RegisterServices),
            ["GeneratedServiceRegistry.Register"] = nameof(GeneratedServiceRegistry.Register),
            ["GeneratedServiceRegistry.CreateProxy"] = nameof(GeneratedServiceRegistry.CreateProxy),
            ["GeneratedServiceRegistry.CreateDispatcher"] = nameof(GeneratedServiceRegistry.CreateDispatcher),

            ["ServiceRegistrationSink.AddService"] = nameof(IRpcServiceRegistrationSink.AddService),

            ["ServiceHandle.ServiceName"] = nameof(ServiceHandle.ServiceName),
            ["ServiceHandle.InstanceId"] = nameof(ServiceHandle.InstanceId),

            ["NotFoundKind.Instance"] = nameof(ServiceNotFoundException.NotFoundKind.Instance),
            ["NotFoundKind.Service"] = nameof(ServiceNotFoundException.NotFoundKind.Service),
            ["NotFoundKind.Method"] = nameof(ServiceNotFoundException.NotFoundKind.Method),

            ["GeneratedReturnKind.Void"] = nameof(GeneratedReturnKind.Void),
            ["GeneratedReturnKind.Sync"] = nameof(GeneratedReturnKind.Sync),
            ["GeneratedReturnKind.SyncNestedService"] = nameof(GeneratedReturnKind.SyncNestedService),
            ["GeneratedReturnKind.Task"] = nameof(GeneratedReturnKind.Task),
            ["GeneratedReturnKind.TaskOfT"] = nameof(GeneratedReturnKind.TaskOfT),
            ["GeneratedReturnKind.ValueTask"] = nameof(GeneratedReturnKind.ValueTask),
            ["GeneratedReturnKind.ValueTaskOfT"] = nameof(GeneratedReturnKind.ValueTaskOfT),
            ["GeneratedReturnKind.TaskOfNestedService"] = nameof(GeneratedReturnKind.TaskOfNestedService),
            ["GeneratedReturnKind.ValueTaskOfNestedService"] = nameof(GeneratedReturnKind.ValueTaskOfNestedService),
            ["GeneratedReturnKind.AsyncEnumerable"] = nameof(GeneratedReturnKind.AsyncEnumerable),
            ["GeneratedReturnKind.TaskOfAsyncEnumerable"] = nameof(GeneratedReturnKind.TaskOfAsyncEnumerable),
            ["GeneratedReturnKind.ValueTaskOfAsyncEnumerable"] = nameof(GeneratedReturnKind.ValueTaskOfAsyncEnumerable),
            ["GeneratedReturnKind.Stream"] = nameof(GeneratedReturnKind.Stream),
            ["GeneratedReturnKind.TaskOfStream"] = nameof(GeneratedReturnKind.TaskOfStream),
            ["GeneratedReturnKind.ValueTaskOfStream"] = nameof(GeneratedReturnKind.ValueTaskOfStream),
            ["GeneratedReturnKind.Pipe"] = nameof(GeneratedReturnKind.Pipe),
            ["GeneratedReturnKind.TaskOfPipe"] = nameof(GeneratedReturnKind.TaskOfPipe),
            ["GeneratedReturnKind.ValueTaskOfPipe"] = nameof(GeneratedReturnKind.ValueTaskOfPipe),
        };
}
