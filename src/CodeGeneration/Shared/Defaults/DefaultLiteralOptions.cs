namespace DotBoxD.CodeGeneration.Shared.Defaults;

internal readonly record struct DefaultLiteralOptions(
    bool LowercaseNumericSuffixes,
    bool UseUncheckedEnumCasts)
{
    public static DefaultLiteralOptions SourceGenerator { get; } = new(false, false);

    public static DefaultLiteralOptions Analyzer { get; } = new(true, true);
}
