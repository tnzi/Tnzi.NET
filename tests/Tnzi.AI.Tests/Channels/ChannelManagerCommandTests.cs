namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// 验证 ChannelManager 命令路由包含 /models 和 /memory 命令
/// </summary>
public class ChannelManagerCommandTests
{
    private static readonly string SourcePath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Tnzi.AI.Channels", "Manager", "ChannelManager.cs");

    private static string ReadSource() => File.ReadAllText(Path.GetFullPath(SourcePath));

    [Fact]
    public void HandleCommand_Models_ExistsInSource()
    {
        var source = ReadSource();
        source.ShouldContain("\"/models\"");
    }

    [Fact]
    public void HandleCommand_Memory_ExistsInSource()
    {
        var source = ReadSource();
        source.ShouldContain("\"/memory\"");
    }

    [Fact]
    public void HandleCommand_Help_IncludesNewCommands()
    {
        var source = ReadSource();
        source.ShouldContain("/models");
        source.ShouldContain("/memory");
    }
}
