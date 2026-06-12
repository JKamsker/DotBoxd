using CodeEnforcer;

namespace CodeEnforcer.Tests;

public sealed class CodeEnforcerOptionsTests
{
    [Fact]
    public void RejectsOverrideWhenSoftLimitExceedsHardLimit()
    {
        CodeEnforcerOptions options = CodeEnforcerOptions.Parse(
            ["--soft-line-limit", "501", "--hard-line-limit", "500"]);

        CodeEnforcerException exception = Assert.Throws<CodeEnforcerException>(() =>
            options.ApplyOverrides(new CodeEnforcerConfig()));

        Assert.Contains("softLineLimit", exception.Message);
    }
}
