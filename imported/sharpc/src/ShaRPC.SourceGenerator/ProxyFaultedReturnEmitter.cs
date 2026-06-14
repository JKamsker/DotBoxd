namespace ShaRPC.SourceGenerator;

internal static class ProxyFaultedReturnEmitter
{
    public static bool CanReturnFaulted(MethodReturnKind returnKind) =>
        returnKind is MethodReturnKind.Task
            or MethodReturnKind.TaskOf
            or MethodReturnKind.TaskOfStream
            or MethodReturnKind.TaskOfPipe
            or MethodReturnKind.TaskOfAsyncEnumerable
            or MethodReturnKind.ValueTask
            or MethodReturnKind.ValueTaskOf
            or MethodReturnKind.ValueTaskOfStream
            or MethodReturnKind.ValueTaskOfPipe
            or MethodReturnKind.ValueTaskOfAsyncEnumerable;

    public static string Build(MethodModel method, string exceptionName) =>
        method.ReturnKind switch
        {
            MethodReturnKind.Task =>
                $"global::System.Threading.Tasks.Task.FromException({exceptionName})",
            MethodReturnKind.TaskOf or
                MethodReturnKind.TaskOfStream or
                MethodReturnKind.TaskOfPipe or
                MethodReturnKind.TaskOfAsyncEnumerable =>
                $"global::System.Threading.Tasks.Task.FromException<{GetTaskResultType(method)}>({exceptionName})",
            MethodReturnKind.ValueTask =>
                $"new global::System.Threading.Tasks.ValueTask(global::System.Threading.Tasks.Task.FromException({exceptionName}))",
            MethodReturnKind.ValueTaskOf or
                MethodReturnKind.ValueTaskOfStream or
                MethodReturnKind.ValueTaskOfPipe or
                MethodReturnKind.ValueTaskOfAsyncEnumerable =>
                $"new global::System.Threading.Tasks.ValueTask<{GetValueTaskResultType(method)}>(global::System.Threading.Tasks.Task.FromException<{GetValueTaskResultType(method)}>({exceptionName}))",
            _ => throw new System.InvalidOperationException("Return kind cannot carry a faulted task."),
        };

    public static string BuildCanceled(MethodModel method, string exceptionName) =>
        method.ReturnKind switch
        {
            MethodReturnKind.Task =>
                $"global::System.Threading.Tasks.Task.FromCanceled({exceptionName}.CancellationToken)",
            MethodReturnKind.TaskOf or
                MethodReturnKind.TaskOfStream or
                MethodReturnKind.TaskOfPipe or
                MethodReturnKind.TaskOfAsyncEnumerable =>
                $"global::System.Threading.Tasks.Task.FromCanceled<{GetTaskResultType(method)}>({exceptionName}.CancellationToken)",
            MethodReturnKind.ValueTask =>
                $"new global::System.Threading.Tasks.ValueTask(global::System.Threading.Tasks.Task.FromCanceled({exceptionName}.CancellationToken))",
            MethodReturnKind.ValueTaskOf or
                MethodReturnKind.ValueTaskOfStream or
                MethodReturnKind.ValueTaskOfPipe or
                MethodReturnKind.ValueTaskOfAsyncEnumerable =>
                $"new global::System.Threading.Tasks.ValueTask<{GetValueTaskResultType(method)}>(global::System.Threading.Tasks.Task.FromCanceled<{GetValueTaskResultType(method)}>({exceptionName}.CancellationToken))",
            _ => throw new System.InvalidOperationException("Return kind cannot carry a canceled task."),
        };

    public static string GetValueTaskResultType(MethodModel method) =>
        method.ReturnKind switch
        {
            MethodReturnKind.ValueTaskOf => method.UnwrappedReturnType!,
            MethodReturnKind.ValueTaskOfStream => "global::System.IO.Stream",
            MethodReturnKind.ValueTaskOfPipe => "global::System.IO.Pipelines.Pipe",
            MethodReturnKind.ValueTaskOfAsyncEnumerable =>
                $"global::System.Collections.Generic.IAsyncEnumerable<{method.UnwrappedReturnType}>",
            _ => throw new System.InvalidOperationException("Return kind is not a generic ValueTask."),
        };

    private static string GetTaskResultType(MethodModel method) =>
        method.ReturnKind switch
        {
            MethodReturnKind.TaskOf => method.UnwrappedReturnType!,
            MethodReturnKind.TaskOfStream => "global::System.IO.Stream",
            MethodReturnKind.TaskOfPipe => "global::System.IO.Pipelines.Pipe",
            MethodReturnKind.TaskOfAsyncEnumerable =>
                $"global::System.Collections.Generic.IAsyncEnumerable<{method.UnwrappedReturnType}>",
            _ => throw new System.InvalidOperationException("Return kind is not a generic Task."),
        };
}
