namespace DotBoxD.Shared.HostBindings;

internal static class HostBindingMetadataRules
{
    public const long HostStateRead = 1;
    public const long HostStateWrite = 2;
    public const long Allocates = 4;

    public const string CpuEffect = "Cpu";
    public const string HostStateReadEffect = "HostStateRead";
    public const string HostStateWriteEffect = "HostStateWrite";
    public const string AllocEffect = "Alloc";

    public static string BindingId(string? containingNamespace, string typeMetadataName, string methodName)
        => "host." +
           (string.IsNullOrEmpty(containingNamespace)
               ? typeMetadataName
               : containingNamespace + "." + typeMetadataName) +
           "." + methodName;

    public static long ValidateDeclaredEffects(long declaredEffects, bool returnAllocates, string description)
    {
        var access = declaredEffects & (HostStateRead | HostStateWrite);
        if (access is not HostStateRead and not HostStateWrite)
        {
            throw new InvalidOperationException(
                $"{description} must declare exactly one of HostStateRead or HostStateWrite.");
        }

        var declaresAllocation = (declaredEffects & Allocates) == Allocates;
        if (declaresAllocation != returnAllocates)
        {
            throw new InvalidOperationException(
                returnAllocates
                    ? $"{description} must declare Allocates because its return shape allocates."
                    : $"{description} must not declare Allocates because its return shape does not allocate.");
        }

        return declaredEffects;
    }

    public static bool ReturnAllocatesManifestTag(string tag)
        => tag is "string" or "guid" or "list" or "map" or "record";

    public static bool ReturnAllocatesSandboxTypeName(string name)
        => name is not "Unit" and not "Bool" and not "I32" and not "I64" and not "F64";

    public static IEnumerable<string> EffectNames(long declaredEffects)
    {
        yield return CpuEffect;
        if ((declaredEffects & Allocates) == Allocates)
        {
            yield return AllocEffect;
        }

        if ((declaredEffects & HostStateWrite) == HostStateWrite)
        {
            yield return HostStateWriteEffect;
            yield break;
        }

        yield return HostStateReadEffect;
    }
}
