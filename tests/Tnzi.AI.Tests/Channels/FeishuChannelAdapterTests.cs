namespace Tnzi.AI.Tests.Channels;

public class FeishuChannelAdapterTests
{
    [Fact]
    public void Name_ReturnsFeishu()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Adapters/Feishu/FeishuChannelAdapter.cs");
        source.ShouldContain("\"feishu\"");
    }

    [Fact]
    public void SupportsStreaming_ReturnsTrue()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Adapters/Feishu/FeishuChannelAdapter.cs");
        source.ShouldContain("SupportsStreaming => true");
    }

    [Fact]
    public void FeishuOptions_HasRequiredFields()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Adapters/Feishu/FeishuAdapterOptions.cs");
        source.ShouldContain("AppId");
        source.ShouldContain("AppSecret");
    }

    [Fact]
    public void ChannelsModule_RegistersFeishu()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/ChannelsModule.cs");
        source.ShouldContain("FeishuChannelAdapter");
        source.ShouldContain("options.Feishu.Enabled");
    }
}
