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

    /// <summary>
    /// 管理端按地址筛选时的归一化：与登记同一条规则；传真号码片段归一化不了时再退一步只留数字。
    /// </summary>
    /// <remarks>
    /// ★ 传真登记存的是纯数字，而操作者手里的片段常是名片上的写法（<c>(905) 555</c>）。
    /// 不够 <see cref="FaxNumber.MinDigits"/> 位的片段 <see cref="FaxNumber.TryNormalize"/> 拒收，
    /// 若原样拿去做包含匹配，括号与空格让它永远命中不了任何一行 —— 页面说「没有记录」，
    /// 而那个号码明明退订过。所以只由数字与号码分隔符组成的片段剥成数字再匹配；
    /// 夹着别的字符的（字母、分机记号）不是号码片段，原样匹配。
    /// </remarks>
    private static string NormalizeSearchAddress(string address, NotificationType channel)
    {
        var normalized = Normalize(address, channel);
        if (channel != NotificationType.Fax || normalized.Length == 0)
            return normalized;

        var isNumberFragment = normalized.All(c => char.IsAsciiDigit(c) || char.IsWhiteSpace(c) || FaxSearchSeparators.Contains(c))
                               && normalized.Any(char.IsAsciiDigit);
        return isNumberFragment ? new string(normalized.Where(char.IsAsciiDigit).ToArray()) : normalized;
    }

    /// <summary>号码片段里允许出现的分隔符，与 <see cref="FaxNumber"/> 接受的写法同一组。</summary>
    private const string FaxSearchSeparators = "+-()./";

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

        await InsertOrLoadExistingAsync(new OptOut
        {
            Address = normalized,
            Channel = channel,
            Category = cat,
            Source = source,
            Reason = reason,
        }, cancellationToken);

        return Ok();
    }

    /// <summary>
    /// 插入一条退订；并发请求已经插入了同一 (地址, 渠道, 分类) 时返回那一行。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 判重是 read-then-write，两个并发请求（收件人连点两下退订链接、管理员补录撞上一键退订）
    /// 都会读到「还没有」，由唯一索引把第二个挡下。挡下的那一次表达的就是已经成立的事实，
    /// 应当成功而不是 500 —— 一键退订的页面报错，收件人会以为自己没退掉。
    /// </para>
    /// <para>
    /// ★ 显式 flush：事务延迟保存时违例会推迟到提交那一刻才抛，在这里接不住。
    /// ★ 必须 <see cref="IRepository{TEntity}.Discard"/>：插入失败的实体仍以 Added 留在变更跟踪器里，
    /// 同一作用域下一次保存会重放它，异常落在一个完全无关的位置。
    /// </para>
    /// </remarks>
    private async Task<OptOut> InsertOrLoadExistingAsync(OptOut entity, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.InsertAsync(entity, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
            return entity;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            _repository.Discard(entity);

            var address = entity.Address;
            var channel = entity.Channel;
            var category = entity.Category;
            return await _repository.AsQueryable()
                       .AsNoTracking()
                       .FirstOrDefaultAsync(o => o.Address == address && o.Channel == channel && o.Category == category, cancellationToken)
                   ?? throw new InvalidOperationException(
                       "The opt-out was rejected as a duplicate, but no matching record could be read back.", ex);
        }
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
    public async Task<Result<IPagedList<OptOutDto>>> GetPagedListAsync(OptOutQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        // 地址按归一化后的写法比对：库里存的就是那个形态，操作者随手敲的大小写 / 空白不该让他查不到。
        // 筛了渠道就走登记时的同一条归一化：传真存的是纯数字，粘贴 +1 (905) 555-1234 也要命中 9055551234 那一行；
        // 归一化不了的片段（区号、后几位）原样做包含匹配。没筛渠道时不知道该按哪条规则，只去空白加小写。
        var address = string.IsNullOrWhiteSpace(query.Address)
            ? null
            : query.Channel.HasValue
                ? NormalizeSearchAddress(query.Address, query.Channel.Value)
                : query.Address.Trim().ToLowerInvariant();
        var category = NormalizeCategory(query.Category);
        // 整渠道退订（Category = null）覆盖该渠道下的任何分类。只看某个分类的抑制名单要能把这些行一起列出来，
        // 否则它解释不了「这个人为什么收不到」：他退订的是整个渠道，而这一页看不见。开关只在筛了分类时有意义。
        var includeChannelWide = category != null && query.IncludeChannelWide;

        var filtered = _repository.AsQueryable()
            .AsNoTracking()
            .Where(o =>
                (address == null || o.Address.Contains(address)) &&
                (!query.Channel.HasValue || o.Channel == query.Channel.Value) &&
                (category == null || o.Category == category || (includeChannelWide && o.Category == null)) &&
                (!query.From.HasValue || o.CreationTime >= query.From.Value) &&
                (!query.To.HasValue || o.CreationTime <= query.To.Value));

        var totalCount = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderByDescending(o => o.CreationTime)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        return Ok<IPagedList<OptOutDto>>(new PagedList<OptOutDto>(
            items.MapToList<OptOutDto>(), query.PageIndex, query.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<OptOutDto>> RegisterAsync(CreateOptOutDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var normalized = Normalize(input.Address, input.Channel);
        if (normalized.Length == 0)
            return Fail<OptOutDto>("An address is required to opt out.", 400, ErrorCodes.NOTIFICATION_ERROR);

        var cat = NormalizeCategory(input.Category);

        // 与一键退订同一条幂等规则：同一 地址+渠道+分类 只有一行。已存在就原样返回，
        // 不改写它的 Source / Reason —— 那是最初那次退订的追溯信息，管理员补录不该覆盖收件人自己的记录。
        var existing = await _repository.AsQueryable()
            .FirstOrDefaultAsync(
                o => o.Address == normalized && o.Channel == input.Channel && o.Category == cat,
                cancellationToken);
        if (existing != null)
            return Ok(existing.MapTo<OptOutDto>());

        var operatorId = CurrentUser?.Id;
        var entity = new OptOut
        {
            Address = normalized,
            Channel = input.Channel,
            Category = cat,
            Source = operatorId.HasValue ? $"admin:{operatorId.Value}" : "admin",
            Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim(),
        };
        var stored = await InsertOrLoadExistingAsync(entity, cancellationToken);

        return Ok(stored.MapTo<OptOutDto>());
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.AsQueryable().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (existing == null)
            return Fail("Opt-out record not found.", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        await _repository.DeleteAsync(existing, cancellationToken: cancellationToken);
        return Ok();
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
