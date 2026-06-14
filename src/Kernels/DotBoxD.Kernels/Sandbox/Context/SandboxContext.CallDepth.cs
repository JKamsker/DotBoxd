namespace DotBoxD.Kernels;

public sealed partial class SandboxContext
{
    internal void RequireAdditionalCallDepth(int additionalDepth)
    {
        if (additionalDepth <= 0)
        {
            return;
        }

        if (additionalDepth > Budget.Limits.MaxCallDepth - _callDepth)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "call depth exceeded"));
        }
    }
}
