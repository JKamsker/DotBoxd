namespace ShaRPC.Core.Generated;

/// <summary>
/// Classifies the generated RPC-facing return shape of a service method.
/// </summary>
public enum ShaRpcGeneratedReturnKind
{
    Void,
    Sync,
    Task,
    TaskOfT,
    ValueTask,
    ValueTaskOfT,
    TaskOfNestedService,
    ValueTaskOfNestedService,
    AsyncEnumerable,
    TaskOfAsyncEnumerable,
    ValueTaskOfAsyncEnumerable,
    Stream,
    TaskOfStream,
    ValueTaskOfStream,
    Pipe,
    TaskOfPipe,
    ValueTaskOfPipe,
}
