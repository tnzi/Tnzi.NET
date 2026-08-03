using System.Security.Cryptography;

namespace Tnzi.Notification.Services;

/// <inheritdoc cref="INotificationOptOutService" />
public class NotificationOptOutService : ApplicationService, INotificationOptOutService
{
    private readonly IRepository<OptOut, Guid> _repository;
    private readonly IOptionsMonitor<NotificationOptions> _options;

    public NotificationOptOutService(
        IServiceProvider serviceProvider,
        IRepository<OptOut, Guid> repository,
        IOptionsMonitor<NotificationOptions> options)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// 地址归一化：去空白 + 小写。收件人名单里的大小写与实际发送地址常常不一致，
    /// 不归一化就会出现"退订了却还在收"这种最难查的失效。
    /// </summary>
    private static string Normalize(string address)
        => (address ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>分类归一化：空串与 null 是同一件事（整渠道退订）。</summary>
    private static string? NormalizeCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ? null : category.Trim();

    /// <inheritdoc />
    public async Task<Result> OptOutAsync(
        string address,
        NotificationType channel,
        string? category = null,
        string? source = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(address);
        if (normalized.Length == 0)
            return Fail("An address is required to opt out.", 400, ErrorCodes.NOTIFICATION_ERROR);

        var cat = NormalizeCategory(category);

        // 幂等：反复点退订链接表达的是同一件事，不该在表里堆出几十行。
        var existing = await _repository.AsQueryable()
            .FirstOrDefaultAsync(
                o => o.Address == normalized && o.Channel == channel && o.Category == cat,
                cancellationToken);
        if (existing != null)
            return Ok();

        await _repository.InsertAsync(new OptOut
        {
            Address = normalized,
            Channel = channel,
            Category = cat,
            Source = source,
            Reason = reason,
        }, cancellationToken: cancellationToken);

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> OptInAsync(
        string address,
        NotificationType channel,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(address);
        var cat = NormalizeCategory(category);

        var existing = await _repository.AsQueryable()
            .FirstOrDefaultAsync(
                o => o.Address == normalized && o.Channel == channel && o.Category == cat,
                cancellationToken);

        // 本来就没退订过 = 已经是想要的状态，不是错误。
        if (existing == null)
            return Ok();

        await _repository.DeleteAsync(existing, cancellationToken: cancellationToken);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<bool> IsOptedOutAsync(
        string address,
        NotificationType channel,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(address);
        var cat = NormalizeCategory(category);

        // 整渠道退订（Category = null）覆盖该渠道下的任何分类。
        return await _repository.AsQueryable().AnyAsync(
            o => o.Address == normalized
                 && o.Channel == channel
                 && (o.Category == null || o.Category == cat),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FilterAllowedAsync(
        IEnumerable<string> addresses,
        NotificationType channel,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(addresses);

        // 保持输入顺序并去重：调用方给的名单顺序常常是有意义的（按重要性排的），
        // 而重复地址会让同一个人收到两封。
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in addresses)
        {
            var normalized = Normalize(raw);
            if (normalized.Length > 0 && seen.Add(normalized))
                ordered.Add(normalized);
        }

        if (ordered.Count == 0)
            return Array.Empty<string>();

        var cat = NormalizeCategory(category);

        // ★ 一次查完。逐个 IsOptedOutAsync 在一次千人群发上就是一千次往返。
        var blocked = await _repository.AsQueryable()
            .Where(o => ordered.Contains(o.Address)
                        && o.Channel == channel
                        && (o.Category == null || o.Category == cat))
            .Select(o => o.Address)
            .ToListAsync(cancellationToken);

        var blockedSet = new HashSet<string>(blocked, StringComparer.Ordinal);
        return ordered.Where(a => !blockedSet.Contains(a)).ToList();
    }

    /// <inheritdoc />
    public string CreateUnsubscribeToken(string address, NotificationType channel, string? category = null)
    {
        var payload = $"{Normalize(address)}|{(int)channel}|{NormalizeCategory(category)}";
        var signature = Sign(payload);
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}.{Base64UrlEncode(signature)}";
    }

    /// <inheritdoc />
    public UnsubscribeTokenPayload? ResolveUnsubscribeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
            return null;

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = Base64UrlDecode(token[..dot]);
            providedSignature = Base64UrlDecode(token[(dot + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);

        // 定长比较：按字节提前返回会把签名一位一位地泄露出去。
        if (!CryptographicOperations.FixedTimeEquals(Sign(payload), providedSignature))
            return null;

        var parts = payload.Split('|');
        if (parts.Length != 3 || !int.TryParse(parts[1], out var channelValue))
            return null;
        if (!Enum.IsDefined(typeof(NotificationType), channelValue))
            return null;

        return new UnsubscribeTokenPayload(
            parts[0],
            (NotificationType)channelValue,
            parts[2].Length == 0 ? null : parts[2]);
    }

    private byte[] Sign(string payload)
    {
        var secret = _options.CurrentValue.OptOut.TokenSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            // 刻意抛而不是回退到某个内置默认值：默认密钥会让签名形同虚设，
            // 且这种失效毫无症状 —— 链接照常工作，直到有人发现自己"被退订"了。
            throw new InvalidOperationException(
                "Notification:OptOut:TokenSecret is not configured. One-click unsubscribe links are " +
                "signed with it; without a deployment-specific secret anyone could unsubscribe any " +
                "address, so no token is issued.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    // 令牌进 URL，所以用 URL-safe 变体并去掉填充。
    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
