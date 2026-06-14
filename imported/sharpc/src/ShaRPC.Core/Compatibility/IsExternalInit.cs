// Polyfill for the init-only setter marker type, which is not present in the
// netstandard2.1 reference assemblies. Required so that records and init accessors
// (e.g. MessageFramer.FramedMessage) compile on this target.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
