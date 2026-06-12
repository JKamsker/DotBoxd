using CodeEnforcer;

namespace CodeEnforcer.Tests;

public sealed class CodeFileCollectorTests
{
    [Theory]
    [InlineData("src/App/Generated.g.cs")]
    [InlineData("src/App/Form.Designer.cs")]
    [InlineData("src/App/bin/Debug/Generated.cs")]
    [InlineData("src/App/obj/Release/Generated.cs")]
    public void SkipsGeneratedAndBuildOutputFiles(string path)
    {
        Assert.True(CodeFileCollector.ShouldSkip(path));
    }

    [Fact]
    public void DoesNotSkipNormalSourceFile()
    {
        Assert.False(CodeFileCollector.ShouldSkip("src/App/Service.cs"));
    }
}
