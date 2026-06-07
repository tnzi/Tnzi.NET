using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Tnzi.AI.Channels.Adapters.Telegram;

/// <summary>
/// Telegram Bot 适配器 — 使用长轮询接收消息，通过 IChannelMessageBus 路由。
/// 支持文本消息、命令、照片和文档。
/// </summary>
public class TelegramChannelAdapter : IChannelAdapter
{
    private readonly ILogger<TelegramChannelAdapter> _logger;
    private readonly IChannelMessageBus _bus;
    private readonly TelegramAdapterOptions _options;
    private readonly TelegramBotClient _botClient;
    private readonly HashSet<long> _allowedUsers;
    private CancellationTokenSource? _cts;

    public string Name => "telegram";
    public bool SupportsStreaming => false;
    public bool SupportsFileAttachment => true;

    public TelegramChannelAdapter(
        ILogger<TelegramChannelAdapter> logger,
        IChannelMessageBus bus,
        IOptions<ChannelsModuleOptions> options)
    {
        _logger = Check.NotNull(logger);
        _bus = Check.NotNull(bus);
        _options = Check.NotNull(options).Value.Telegram;

        if (string.IsNullOrWhiteSpace(_options.BotToken))
            throw new ArgumentException("Telegram BotToken is required when adapter is enabled");

        _botClient = new TelegramBotClient(_options.BotToken);
        _allowedUsers = _options.AllowedUsers.Count > 0
            ? new HashSet<long>(_options.AllowedUsers)
            : [];
    }

    /// <summary>检查用户是否被允许（空白名单=不限制）</summary>
    public bool IsUserAllowed(long userId)
    {
        return _allowedUsers.Count == 0 || _allowedUsers.Contains(userId);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _cts.Token);

        _logger.LogInformation("Telegram adapter started with long-polling (timeout: {Timeout}s)", _options.PollingTimeoutSeconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        _logger.LogInformation("Telegram adapter stopped");
        return Task.CompletedTask;
    }

    public Task SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        var chatId = long.Parse(message.ChatId);

        return ChannelSendHelper.SendChunkedWithRetryAsync(
            message.Text,
            maxLength: 4096,
            maxRetries: _options.MaxRetries,
            sendChunk: (chunk, token) => _botClient.SendMessage(chatId, chunk, cancellationToken: token),
            _logger,
            "Telegram",
            ct);
    }

    public async Task<bool> SendFileAsync(OutboundMessage message, ResolvedAttachment attachment, CancellationToken ct = default)
    {
        var chatId = long.Parse(message.ChatId);

        if (!System.IO.File.Exists(attachment.ActualPath))
        {
            _logger.LogWarning("Attachment file not found: {Path}", attachment.ActualPath);
            return false;
        }

        await using var stream = System.IO.File.OpenRead(attachment.ActualPath);

        if (attachment.IsImage && attachment.Size <= _options.MaxPhotoSize)
        {
            await _botClient.SendPhoto(chatId,
                InputFile.FromStream(stream, attachment.FileName),
                cancellationToken: ct);
        }
        else if (attachment.Size <= _options.MaxDocumentSize)
        {
            await _botClient.SendDocument(chatId,
                InputFile.FromStream(stream, attachment.FileName),
                cancellationToken: ct);
        }
        else
        {
            _logger.LogWarning("Attachment too large ({Size} bytes): {FileName}", attachment.Size, attachment.FileName);
            return false;
        }

        return true;
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.Message is not { } telegramMessage) return;
        if (telegramMessage.From == null) return;

        if (!IsUserAllowed(telegramMessage.From.Id))
        {
            _logger.LogDebug("Ignoring message from non-allowed user {UserId}", telegramMessage.From.Id);
            return;
        }

        var text = telegramMessage.Text ?? telegramMessage.Caption ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;

        var isCommand = text.StartsWith('/');
        var chatId = telegramMessage.Chat.Id.ToString();
        var userId = telegramMessage.From.Id.ToString();
        var topicId = telegramMessage.MessageThreadId?.ToString();

        var inbound = new InboundMessage(
            ChannelName: "telegram",
            ChatId: chatId,
            UserId: userId,
            Text: text,
            Type: isCommand ? InboundMessageType.Command : InboundMessageType.Chat,
            TopicId: topicId,
            ThreadTs: telegramMessage.MessageId.ToString());

        await _bus.PublishInboundAsync(inbound, ct);
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }
}
