using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Tnzi.AI.Channels.Adapters.Telegram;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// Telegram 出站：论坛话题（<c>TopicId</c>）必须映射到 <c>message_thread_id</c>，
/// 否则话题里的提问被回到 General，提问者永远看不到回复而每条日志都是成功。
/// </summary>
public class TelegramChannelAdapterTests
{
    [Fact]
    public async Task SendAsync_WithTopicId_SetsMessageThreadId()
    {
        var (adapter, requests) = CreateAdapter();

        await adapter.SendAsync(new OutboundMessage("telegram", "123", Guid.Empty, "hi", TopicId: "42"));

        var request = requests.ShouldHaveSingleItem().ShouldBeOfType<SendMessageRequest>();
        request.MessageThreadId.ShouldBe(42);
        request.ChatId.Identifier.ShouldBe(123);
        request.Text.ShouldBe("hi");
    }

    [Fact]
    public async Task SendAsync_WithoutTopicId_LeavesMessageThreadIdNull()
    {
        var (adapter, requests) = CreateAdapter();

        await adapter.SendAsync(new OutboundMessage("telegram", "123", Guid.Empty, "hi"));

        var request = requests.ShouldHaveSingleItem().ShouldBeOfType<SendMessageRequest>();
        request.MessageThreadId.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_WithNonNumericTopicId_LeavesMessageThreadIdNull()
    {
        var (adapter, requests) = CreateAdapter();

        await adapter.SendAsync(new OutboundMessage("telegram", "123", Guid.Empty, "hi", TopicId: "not-a-number"));

        var request = requests.ShouldHaveSingleItem().ShouldBeOfType<SendMessageRequest>();
        request.MessageThreadId.ShouldBeNull();
    }

    [Theory]
    [InlineData(ChatType.Private, "user")]
    [InlineData(ChatType.Group, "group")]
    [InlineData(ChatType.Supergroup, "group")]
    public async Task InboundUpdate_ReportsThePeerKindFromChatType(ChatType chatType, string expected)
    {
        // 绑定规则的 PeerKind 按它匹配：不报出来，带 PeerKind 的规则永远不会命中。
        // 入站只有长轮询这一个公开入口，这里直接驱动它的更新处理方法。
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = new TelegramChannelAdapter(
            NullLogger<TelegramChannelAdapter>.Instance,
            bus,
            MsOptions.Create(new ChannelsModuleOptions { Telegram = new TelegramAdapterOptions { Enabled = true, BotToken = "123456:token" } }),
            Mock.Of<ITelegramBotClient>());

        var update = new Update
        {
            Message = new Message
            {
                Chat = new Chat { Id = -100123, Type = chatType },
                From = new User { Id = 42, FirstName = "u" },
                Text = "hello"
            }
        };

        var handler = typeof(TelegramChannelAdapter).GetMethod("HandleUpdateAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)handler.Invoke(adapter, [Mock.Of<ITelegramBotClient>(), update, CancellationToken.None])!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var received = await bus.ConsumeInboundAsync(cts.Token);
        received.PeerKind.ShouldBe(expected);
        received.ChatId.ShouldBe("-100123");
    }

    private static (TelegramChannelAdapter Adapter, List<IRequest<Message>> Requests) CreateAdapter()
    {
        var requests = new List<IRequest<Message>>();
        var client = new Mock<ITelegramBotClient>();
        client
            .Setup(c => c.SendRequest(It.IsAny<IRequest<Message>>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Message>, CancellationToken>((req, _) => requests.Add(req))
            .ReturnsAsync(new Message());

        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Telegram = new TelegramAdapterOptions { Enabled = true, BotToken = "123456:token", MaxRetries = 0 }
        });

        var adapter = new TelegramChannelAdapter(
            NullLogger<TelegramChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            options,
            client.Object);

        return (adapter, requests);
    }
}
