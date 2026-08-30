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
    /// 地址归一化：去空白 + 小写；号码型渠道再走一遍号码归一化。
    /// 收件人名单里的写法与实际发送地址常常不一致，不归一化就会出现"退订了却还在收"
    /// 这种最难查的失效。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>传真为什么要多走一步</b>：它的地址是电话号码，而同一个号码有
    /// <c>+1 (905) 555-1234</c> 与 <c>9055551234</c> 两种同样正常的写法。只做去空白加小写
    /// 会把它们留成两条互不相干的记录 —— 用一种写法退订，用另一种写法照发，
    /// 没有报错、没有退信，日志里是一次正常投递。用的是发送时的同一个
    /// <see cref="FaxNumber"/>，所以退订登记的形态与真正拨出去的号码永远对得上。
    /// </para>
    /// <para>
    /// ★★ <b>刻意不把同一条规则套到 <see cref="NotificationType.Sms"/></b>：短信的退订记录
    /// 已经在库里按原始写法存着，此刻改归一化会让它们从这一刻起匹配不上 ——
    /// 那正是本方法要防的那种失效，方向还反了。短信要收口得连同一次数据迁移一起做。
    /// 传真是新渠道，库里没有历史记录，所以现在就能定死。
    /// </para>
    /// </remarks>
    private static string Normalize(string address, NotificationType channel)
    {
        var trimmed = (address ?? string.Empty).Trim().ToLowerInvariant();

        if (channel != NotificationType.Fax || trimmed.Length == 0)
            return trimmed;

        // 归一化不了的号码原样留着：它本来就发不出去，但"这个地址退订过"仍然要记下来。
        return FaxNumber.TryNormalize(trimmed, out var digits, out _) ? digits : trimmed;
    }

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
        var normalized = Normalize(address, channel);
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
        var normalized = Normalize(address, channel);
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
        var normalized = Normalize(address, channel);
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
        // 而重复地址会让同一个人收到两封。去重按归一化后的键 —— 同一个地址的两种写法是同一个人。
        //
        // ★ 返回的是**调用方给的那个原样地址**，不是归一化后的形态：归一化是本服务的内部账本，
        // 而调用方拿着返回值要跟自己手里的收件人对上号。传真号上这一点尤其致命 ——
        // `+1 (905) 555-1234` 归一化成 `9055551234`，若把后者还回去，调用方一个收件人都认不出来，
        // 于是整批传真被当成"已退订"全部取消，而没有任何一步失败过。
        var original = new List<string>();
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in addresses)
        {
            var normalized = Normalize(raw, channel);
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                original.Add(raw);
                keys.Add(normalized);
            }
        }

        if (keys.Count == 0)
            return Array.Empty<string>();

        var cat = NormalizeCategory(category);

        // ★ 一次查完。逐个 IsOptedOutAsync 在一次千人群发上就是一千次往返。
        var blocked = await _repository.AsQueryable()
            .Where(o => keys.Contains(o.Address)
                        && o.Channel == channel
                        && (o.Category == null || o.Category == cat))
            .Select(o => o.Address)
            .ToListAsync(cancellationToken);

        var blockedSet = new HashSet<string>(blocked, StringComparer.Ordinal);

        var allowed = new List<string>(original.Count);
        for (var i = 0; i < original.Count; i++)
        {
            if (!blockedSet.Contains(keys[i]))
                allowed.Add(original[i]);
        }

        return allowed;
    }

    /// <inheritdoc />
    public string CreateUnsubscribeToken(string address, NotificationType channel, string? category = null)
    {
        var payload = $"{Normalize(address, channel)}|{(int)channel}|{NormalizeCategory(category)}";
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
