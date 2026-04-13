using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Channels.Adapters.Telegram;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Tests.AI.Channels;

public class TelegramChannelAdapterTests
{
    [Fact]
    public void Name_ReturnsTelegram()
    {
        var adapter = CreateAdapter();
        Assert.Equal("telegram", adapter.Name);
    }

    [Fact]
    public void SupportsStreaming_ReturnsFalse()
    {
        // Telegram does not support real-time streaming (edit-based)
        var adapter = CreateAdapter();
        Assert.False(adapter.SupportsStreaming);
    }

    [Fact]
    public void Constructor_NullBotToken_ThrowsArgumentException()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Telegram = new TelegramAdapterOptions { Enabled = true, BotToken = null }
        });

        Assert.Throws<ArgumentException>(() => new TelegramChannelAdapter(
            NullLogger<TelegramChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            options));
    }

    [Fact]
    public void Constructor_EmptyBotToken_ThrowsArgumentException()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Telegram = new TelegramAdapterOptions { Enabled = true, BotToken = "   " }
        });

        Assert.Throws<ArgumentException>(() => new TelegramChannelAdapter(
            NullLogger<TelegramChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            options));
    }

    [Fact]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        var adapter = CreateAdapter();
        await adapter.StopAsync();
        // 不应抛出异常
    }

    [Fact]
    public void AllowedUsers_EmptyList_MeansNoRestriction()
    {
        var adapter = CreateAdapter(allowedUsers: []);
        // 内部 IsUserAllowed 应对空列表返回 true
        Assert.True(adapter.IsUserAllowed(12345));
    }

    [Fact]
    public void AllowedUsers_WithList_FiltersCorrectly()
    {
        var adapter = CreateAdapter(allowedUsers: [100, 200]);
        Assert.True(adapter.IsUserAllowed(100));
        Assert.True(adapter.IsUserAllowed(200));
        Assert.False(adapter.IsUserAllowed(999));
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        var adapter = CreateAdapter();
        await adapter.DisposeAsync();
        await adapter.DisposeAsync();
        // 不应抛出异常
    }

    private static TelegramChannelAdapter CreateAdapter(List<long>? allowedUsers = null)
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Telegram = new TelegramAdapterOptions
            {
                Enabled = true,
                BotToken = "123456789:ABCDefGHIJklMNoPQRstUVwxYZ0123456789abc",
                AllowedUsers = allowedUsers ?? []
            }
        });

        return new TelegramChannelAdapter(
            NullLogger<TelegramChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            options);
    }
}
