using Tnzi.AI.Sandbox.Services;

namespace Tnzi.AI.Tests.Sandbox;

public class CommandSemanticsTests
{
    [Fact]
    public void InterpretExitCode_GrepNoMatch_ReturnsExplanation()
    {
        var result = CommandSemantics.InterpretExitCode("grep pattern file.txt", 1);
        Assert.NotNull(result);
        Assert.Contains("No matches found", result);
    }

    [Fact]
    public void InterpretExitCode_GrepError_ReturnsError()
    {
        var result = CommandSemantics.InterpretExitCode("grep pattern file.txt", 2);
        Assert.NotNull(result);
        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretExitCode_DiffDiffers_ReturnsExplanation()
    {
        var result = CommandSemantics.InterpretExitCode("diff file1 file2", 1);
        Assert.NotNull(result);
        Assert.Contains("differ", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretExitCode_UnknownCommand_ReturnsNull()
    {
        var result = CommandSemantics.InterpretExitCode("myapp --run", 1);
        Assert.Null(result);
    }

    [Fact]
    public void InterpretExitCode_ZeroExitCode_ReturnsNull()
    {
        var result = CommandSemantics.InterpretExitCode("grep pattern file.txt", 0);
        Assert.Null(result);
    }

    [Fact]
    public void IsNonErrorExitCode_GrepNoMatch_ReturnsTrue()
    {
        Assert.True(CommandSemantics.IsNonErrorExitCode("grep pattern file.txt", 1));
    }

    [Fact]
    public void IsNonErrorExitCode_GrepError_ReturnsFalse()
    {
        Assert.False(CommandSemantics.IsNonErrorExitCode("grep pattern file.txt", 2));
    }

    [Fact]
    public void ExtractBaseCommand_SimpleCommand_ReturnsCommand()
    {
        Assert.Equal("grep", CommandSemantics.ExtractBaseCommand("grep pattern file.txt"));
    }

    [Fact]
    public void ExtractBaseCommand_PipedCommand_ReturnsLastCommand()
    {
        Assert.Equal("grep", CommandSemantics.ExtractBaseCommand("cat file.txt | grep pattern"));
    }

    [Fact]
    public void ExtractBaseCommand_WithSudo_StripsPrefix()
    {
        Assert.Equal("grep", CommandSemantics.ExtractBaseCommand("sudo grep pattern file.txt"));
    }

    [Fact]
    public void ExtractBaseCommand_WithPath_StripsPath()
    {
        Assert.Equal("grep", CommandSemantics.ExtractBaseCommand("/usr/bin/grep pattern file.txt"));
    }
}
