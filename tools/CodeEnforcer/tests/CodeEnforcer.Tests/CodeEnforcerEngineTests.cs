using CodeEnforcer;

namespace CodeEnforcer.Tests;

public sealed class CodeEnforcerEngineTests
{
    [Fact]
    public void ReportsFileAboveSoftLimitWithoutExclusion()
    {
        CodeEnforcerConfig config = new() { SoftLineLimit = 350, HardLineLimit = 500 };

        IReadOnlyList<CodeViolation> violations = Check([new CodeFile("src/App/Large.cs", 351)], config);

        CodeViolation violation = Assert.Single(violations);
        Assert.Equal("CE0001", violation.Rule);
        Assert.Contains("soft limit", violation.Message);
    }

    [Fact]
    public void AllowsFileAboveSoftLimitWhenExcluded()
    {
        CodeEnforcerConfig config = new() { SoftLineLimit = 350, HardLineLimit = 500 };
        config.FileExclusions.Add(new PathExclusion { Path = "src/App/Large.cs" });

        IReadOnlyList<CodeViolation> violations = Check([new CodeFile("src/App/Large.cs", 351)], config);

        Assert.Empty(violations);
    }

    [Fact]
    public void RequiresJustificationAboveHardLimit()
    {
        CodeEnforcerConfig config = new() { SoftLineLimit = 350, HardLineLimit = 500 };
        config.FileExclusions.Add(new PathExclusion { Path = "src/App/Giant.cs" });

        IReadOnlyList<CodeViolation> violations = Check([new CodeFile("src/App/Giant.cs", 501)], config);

        CodeViolation violation = Assert.Single(violations);
        Assert.Equal("CE0002", violation.Rule);
        Assert.Contains("justification", violation.Message);
    }

    [Fact]
    public void AllowsHardLimitWhenExcludedWithJustification()
    {
        CodeEnforcerConfig config = new() { SoftLineLimit = 350, HardLineLimit = 500 };
        config.FileExclusions.Add(new PathExclusion
        {
            Path = "src/App/Giant.cs",
            Justification = "Legacy file awaiting split."
        });

        IReadOnlyList<CodeViolation> violations = Check([new CodeFile("src/App/Giant.cs", 501)], config);

        Assert.Empty(violations);
    }

    [Fact]
    public void ReportsFolderAboveFileLimit()
    {
        CodeEnforcerConfig config = new() { MaxFilesPerFolder = 2 };
        CodeFile[] files =
        [
            new("src/App/A.cs", 10),
            new("src/App/B.cs", 10),
            new("src/App/C.cs", 10)
        ];

        IReadOnlyList<CodeViolation> violations = Check(files, config);

        CodeViolation violation = Assert.Single(violations);
        Assert.Equal("CE0003", violation.Rule);
        Assert.Equal("src/App", violation.Path);
    }

    [Fact]
    public void AllowsFolderAboveFileLimitWhenExcluded()
    {
        CodeEnforcerConfig config = new() { MaxFilesPerFolder = 2 };
        config.FolderExclusions.Add(new PathExclusion { Path = "src/App" });

        IReadOnlyList<CodeViolation> violations = Check(
            [
                new CodeFile("src/App/A.cs", 10),
                new CodeFile("src/App/B.cs", 10),
                new CodeFile("src/App/C.cs", 10)
            ],
            config);

        Assert.Empty(violations);
    }

    private static IReadOnlyList<CodeViolation> Check(IReadOnlyList<CodeFile> files, CodeEnforcerConfig config) =>
        new CodeEnforcerEngine().Check(files, config);
}
