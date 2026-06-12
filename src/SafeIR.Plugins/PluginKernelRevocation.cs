namespace SafeIR.Plugins;

using SafeIR;

internal static class PluginKernelRevocation
{
    public static void ThrowIfRevoked(bool revoked)
    {
        if (!revoked)
        {
            return;
        }

        throw new SandboxRuntimeException(new SandboxError(
            SandboxErrorCode.PolicyDenied,
            "plugin kernel capability was revoked"));
    }
}
