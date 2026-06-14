namespace DotBoxd.Services.Generated;

/// <summary>
/// Classifies the generated RPC-facing return shape of a service method.
/// </summary>
public enum DotBoxdGeneratedReturnKind
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
