namespace Tnzi.AI.Tests.Security;

public class ShellCommandAnalyzerTests
{
    [Fact]
    public void Analyze_PipelineAndSeparator_SplitsSegments()
    {
        var analyzer = new ShellCommandAnalyzer();

        var result = analyzer.Analyze("git status && rm temp.txt | cat");

        result.HasCommandSeparator.ShouldBeTrue();
        result.HasPipeline.ShouldBeTrue();
        result.Segments.Count.ShouldBe(3);
        result.Segments[0].CommandPrefix.ShouldBe("git");
        result.Segments[1].CommandPrefix.ShouldBe("rm");
        result.Segments[2].CommandPrefix.ShouldBe("cat");
    }

    [Fact]
    public void Analyze_DestructiveCommand_FlagsAsDestructiveCandidate()
    {
        var analyzer = new ShellCommandAnalyzer();

        var result = analyzer.Analyze("Remove-Item temp.txt");

        result.IsDestructiveCandidate.ShouldBeTrue();
        result.Segments.Count.ShouldBe(1);
        result.Segments[0].CommandPrefix.ShouldBe("remove-item");
    }

    [Fact]
    public void Analyze_QuotedOperators_DoNotSplitInsideQuotes()
    {
        var analyzer = new ShellCommandAnalyzer();

        var result = analyzer.Analyze("echo \"a && b\"; git status");

        result.Segments.Count.ShouldBe(2);
        result.Segments[0].CommandText.ShouldContain("a && b");
        result.Segments[1].CommandPrefix.ShouldBe("git");
    }
}
