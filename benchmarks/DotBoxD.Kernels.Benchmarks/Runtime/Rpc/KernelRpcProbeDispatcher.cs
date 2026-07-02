namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class KernelRpcProbeDispatcher
{
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--probe-kernel-rpc-value-items", StringComparer.OrdinalIgnoreCase))
        {
            KernelRpcValueItemsProbe.Run();
            return true;
        }

        if (args.Contains("--probe-kernel-rpc-value-list-writer", StringComparer.OrdinalIgnoreCase))
        {
            KernelRpcValueListWriterProbe.Run();
            return true;
        }

        if (args.Contains("--probe-kernel-rpc-binary-codec-empty-decode", StringComparer.OrdinalIgnoreCase))
        {
            KernelRpcBinaryCodecEmptyDecodeProbe.Run();
            return true;
        }

        if (args.Contains("--probe-invokeasync-capture-argument-writer", StringComparer.OrdinalIgnoreCase))
        {
            InvokeAsyncCaptureArgumentWriterProbe.Run();
            return true;
        }

        if (args.Contains("--probe-kernel-rpc-marshaller-dto", StringComparer.OrdinalIgnoreCase))
        {
            KernelRpcMarshallerDtoProbe.Run();
            return true;
        }

        if (args.Contains("--probe-kernel-rpc-marshaller-collections", StringComparer.OrdinalIgnoreCase))
        {
            KernelRpcMarshallerCollectionsProbe.Run();
            return true;
        }

        if (args.Contains("--probe-kernel-rpc-value-converter-collections", StringComparer.OrdinalIgnoreCase))
        {
            KernelRpcValueConverterCollectionsProbe.Run();
            return true;
        }

        return false;
    }
}
