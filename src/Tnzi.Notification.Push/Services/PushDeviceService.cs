namespace Tnzi.Notification.Push.Services;

/// <inheritdoc cref="IPushDeviceService"/>
public class PushDeviceService : ApplicationService, IPushDeviceService
{
    private readonly IRepository<PushDevice, Guid> _repository;

    public PushDeviceService(IServiceProvider serviceProvider, IRepository<PushDevice, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    /// <inheritdoc />
    public async Task<Result<PushDeviceDto>> RegisterAsync(RegisterPushDeviceDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (!TryNormalizeToken(input.Token, out var token, out var tokenFailure))
            return Fail<PushDeviceDto>(tokenFailure!, 400);

        if (!TryNormalizeExternalDeviceId(input.ExternalDeviceId, out _, out var externalFailure))
            return Fail<PushDeviceDto>(externalFailure!, 400);

        var userId = GetRequiredCurrentUser().Id!.Value;
        var now = DateTime.UtcNow;

        // 按令牌定位，不按 (用户, 令牌)：一台设备换人登录时，这一行要**改挂**到新用户，
        // 而不是多出一行。多出一行的后果是前一个用户继续收到本该发给新用户的推送，
        // 且没有任何症状 —— 两行都合法、都能投递成功。
        var existing = await _repository.FirstOrDefaultAsync(d => d.Token == token, cancellationToken);
        if (existing != null)
        {
            // ★★★ 令牌是**凭据**，不只是地址：持有它的人可以改写这台设备的推送归属。
            // 这一行原先无条件把 UserId 改成调用者，于是任何已登录用户只要拿到别人的令牌
            // （从日志里、从那台设备上），就能把那一行改挂给自己 —— 受害者从此收不到推送，
            // 而两边的接口都返回成功、日志干净、行数不变，**零症状**。
            //
            // ★ 拒绝而不是接管，即使这会挡掉一个真实场景（前一个用户没退出登录，
            // 同一台设备换人登录）。两个方向的代价不对称：接管错了是**无声地**掐掉
            // 某个人的推送，拒绝错了是新用户收到一条**说得出补救办法**的错误。
            // 正常的换人本来就先走登出（那会删掉或摘掉这一行），重装 App 则会拿到新令牌。
            if (existing.UserId.HasValue && existing.UserId.Value != userId)
            {
                Logger.LogWarning(
                    "Push device registration refused: token {DeviceToken} is held by another account. Requested by {RequestingUserId}, held by {HoldingUserId}.",
                    PushTokenMask.Of(token), userId, existing.UserId.Value);

                return Fail<PushDeviceDto>(
                    "This push token is already registered to a different account on this device. "
                    + "Sign out of that account first, or reinstall the app to obtain a new token.",
                    409);
            }

            // UserId 为空的行是**无主**的（登出后摘掉归属的、或纯匿名的），认领它是本意。
            existing.UserId = userId;
            existing.Platform = input.Platform;
            existing.DeviceName = input.DeviceName;
            existing.ExternalDeviceId = ExternalDeviceIdOf(input) ?? existing.ExternalDeviceId;
            existing.LastSeenAt = now;
            // ★ 刻意不动 DeviceKeyHash。这台设备可能先以匿名身份提交过表单、用户后来才注册账号，
            // 清掉它就让那些表单的回执静默停发。两种身份并存，各管各的寻址维度。
            await _repository.UpdateAsync(existing, cancellationToken);
            return Ok(existing.MapTo<PushDeviceDto>());
        }

        var device = new PushDevice
        {
            UserId = userId,
            Token = token!,
            Platform = input.Platform,
            DeviceName = input.DeviceName,
            ExternalDeviceId = ExternalDeviceIdOf(input),
            LastSeenAt = now
        };

        await _repository.InsertAsync(device, cancellationToken);
        return Ok(device.MapTo<PushDeviceDto>());
    }

    /// <inheritdoc />
    public async Task<Result> UnregisterByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var trimmed = token?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Fail("Push token is required.", 400);

        var userId = GetRequiredCurrentUser().Id!.Value;

        // 归属写进查询：令牌若已被改挂给别人（那台设备换人登录了），这里什么都不删。
        var device = await _repository.FirstOrDefaultAsync(
            d => d.Token == trimmed && d.UserId == userId, cancellationToken);

        // ★ 找不到就当成功。登出必须是幂等的：重复登出、或者这台设备的令牌
        // 已被投递路径判定失效而退役掉，都不该让客户端拿到一个错误 ——
        // 那会让「已经不再收推送」这个正确状态看起来像一次失败。
        if (device == null)
            return Ok();

        await DetachUserOrDeleteAsync(device, cancellationToken);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result> UnregisterAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;

        // 归属条件写进查询而不是查到后再判：查到别人的设备时答 404 而不是 403，
        // 否则这个端点会变成一个「这个 id 存不存在」的探针。
        var device = await _repository.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);
        if (device == null)
            return Fail("Device not found.", 404);

        await DetachUserOrDeleteAsync(device, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// 解除一行的用户归属：还挂着匿名身份就只清 <c>UserId</c>，否则整行删掉。
    /// </summary>
    /// <remarks>
    /// ★ <b>登出的是人，不是设备。</b>这台设备若还持有匿名身份，那个身份是消费方业务记录里的
    /// 归属键（哪台设备提交了这份表单）—— 连行一起删掉，此前那些表单的回执就发不出去了，
    /// 而触发它的只是某个用户点了「退出登录」。
    /// </remarks>
    private async Task DetachUserOrDeleteAsync(PushDevice device, CancellationToken cancellationToken)
    {
        if (device.DeviceKeyHash != null)
        {
            device.UserId = null;
            await _repository.UpdateAsync(device, cancellationToken);
            return;
        }

        await _repository.DeleteAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<List<PushDeviceDto>>> GetMyDevicesAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetRequiredCurrentUser().Id!.Value;

        var devices = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt)
            .ToListAsync(cancellationToken);

        return Ok(devices.MapToList<PushDeviceDto>());
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<PushDeviceDto>>> GetPagedListAsync(PushDeviceQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var filtered = _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(d =>
                (!query.UserId.HasValue || d.UserId == query.UserId.Value) &&
                (!query.Platform.HasValue || d.Platform == query.Platform.Value) &&
                (!query.LastSeenAfter.HasValue || d.LastSeenAt >= query.LastSeenAfter.Value) &&
                // 匿名 = 没有登录用户。按 DeviceKeyHash 判会漏掉「登录用户 + 匿名身份并存」
                // 的那些行，而运维在这个筛选里想看的是「这台设备背后有没有人」。
                (!query.AnonymousOnly.HasValue ||
                    (query.AnonymousOnly.Value ? d.UserId == null : d.UserId != null)) &&
                (string.IsNullOrEmpty(query.ExternalDeviceId) ||
                    d.ExternalDeviceId == query.ExternalDeviceId));

        var totalCount = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderByDescending(d => d.LastSeenAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        var paged = new PagedList<PushDeviceDto>(
            items.MapToList<PushDeviceDto>(),
            query.PageIndex,
            query.PageSize,
            totalCount);

        return Ok<IPagedList<PushDeviceDto>>(paged);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _repository.FindAsync(id, cancellationToken);
        if (device == null)
            return Fail("Device not found.", 404);

        // admin 删除是整行删除，不走 DetachUserOrDeleteAsync 那套：运维点「删掉这台设备」的意图
        // 就是让它彻底不再收到任何推送，降级成「只解除了用户归属」与那个意图相反。
        await _repository.DeleteAsync(device, cancellationToken);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<AnonymousDeviceRegistrationDto>> RegisterAnonymousAsync(
        string? deviceKey, RegisterPushDeviceDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (!TryNormalizeToken(input.Token, out var token, out var tokenFailure))
            return Fail<AnonymousDeviceRegistrationDto>(tokenFailure!, 400);

        if (!TryNormalizeExternalDeviceId(input.ExternalDeviceId, out _, out var externalFailure))
            return Fail<AnonymousDeviceRegistrationDto>(externalFailure!, 400);

        // 空白当成「没带密钥」，不当成「密钥是空串」：把空串写进请求头的客户端，
        // 意思是「我还没有身份」。当成密钥的话它会哈希出一个完全合法的值，
        // 于是所有这样的客户端会共用同一行 —— 互相顶掉彼此的令牌。
        var trimmedKey = deviceKey?.Trim();
        var issuing = string.IsNullOrEmpty(trimmedKey);

        // ★ 带来的密钥要先过形态校验。刷新路径必须接受一枚**查不到对应行**的密钥
        // （行被退役后要能重建），所以「这是不是我签发的」查不出来 —— 不校验的话，
        // 一个为了省掉「存密钥」那一行而直接填 identifierForVendor 的客户端会被照单全收，
        // 而「服务端签发所以熵有保证」这条就整个落空了，且没有任何症状。
        if (!issuing && !AnonymousDeviceKey.IsWellFormed(trimmedKey))
        {
            return Fail<AnonymousDeviceRegistrationDto>(
                "The device key is not in the issued format. "
                + "Send the key this endpoint handed you, or omit the header to have one issued.", 400);
        }

        var key = issuing ? AnonymousDeviceKey.Issue() : trimmedKey!;

        var keyHash = OneTimeToken.Hash(key);
        // ★ 从密钥现算，不接受客户端上报的设备 Id：Id 是公开值，谁都能报别人的。
        var deviceId = AnonymousDeviceId.From(key);

        var upsert = await UpsertAnonymousAsync(deviceId, keyHash, token!, input, cancellationToken);
        if (!upsert.Succeeded)
            return Fail<AnonymousDeviceRegistrationDto>(upsert.Message!, upsert.Code ?? 409);

        return Ok(new AnonymousDeviceRegistrationDto
        {
            DeviceId = deviceId,
            // ★ 明文只在签发那一次出现，之后无从取回。带着密钥来刷新的不回它 ——
            // 回一份对方已经有的东西，只是让它多走一趟网络、多进一次日志。
            DeviceKey = issuing ? key : null
        });
    }

    /// <inheritdoc />
    public async Task<Result> UnregisterAnonymousAsync(string? deviceKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            return Fail("Device key is required.", 401);

        // 形态不对的密钥不可能对应任何一行，就不必去查库了。这里<b>不</b>像注册那样
        // 回一条「格式不对」：注销是幂等的，形态不对与密钥对不上都答成功，
        // 两者回答同一句话，端点因此不会变成一个「这枚密钥存不存在」的探针。
        var trimmedKey = deviceKey.Trim();
        if (!AnonymousDeviceKey.IsWellFormed(trimmedKey))
            return Ok();

        // ★ 哈希的是**去空白后**的值，与注册那条一致。曾经这里直接哈希原值：于是一个把密钥
        // 连同换行一起存下来的客户端，注册落 A 哈希、注销算 B 哈希，两者对不上；
        // 而注销是幂等的，对不上就当「已经不在了」返回成功 —— 客户端收到 200，
        // 那一行原封不动地继续收推送。
        var keyHash = OneTimeToken.Hash(trimmedKey);
        var device = await _repository.FirstOrDefaultAsync(d => d.DeviceKeyHash == keyHash, cancellationToken);

        // 幂等，理由与登出那条一字不差：「已经不再收推送」是正确状态，不该长得像一次失败。
        // 顺带这也让「密钥不对」与「密钥对但那一行已被退役」回答同一句话，端点不会变成探针。
        if (device == null)
            return Ok();

        if (device.UserId.HasValue)
        {
            // 这台设备还登录着人：关掉的是匿名投递，不是那个用户的设备。
            device.DeviceKeyHash = null;
            await _repository.UpdateAsync(device, cancellationToken);
            return Ok();
        }

        await _repository.DeleteAsync(device, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// 匿名身份的 upsert：把 <paramref name="keyHash"/> 这个身份落到 <paramref name="token"/> 这台设备上。
    /// </summary>
    /// <remarks>
    /// 签发与刷新共用同一段，差别只在密钥是新造的还是客户端带来的 —— 要处理的落库情形完全相同。
    /// </remarks>
    private async Task<Result<PushDevice>> UpsertAnonymousAsync(
        Guid deviceId, string keyHash, string token, RegisterPushDeviceDto input, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var byIdentity = await _repository.FirstOrDefaultAsync(d => d.DeviceKeyHash == keyHash, cancellationToken);
        var byToken = await _repository.FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

        // 令牌被另一行占着：令牌是设备的地址，同一时刻只能属于一行。
        if (byToken != null && (byIdentity == null || byToken.Id != byIdentity.Id))
        {
            // ★★★ 这条路径是 [AllowAnonymous] 的。原先它无条件删掉占着令牌的那一行、
            // 并把它的 UserId 搬到匿名身份上 —— 于是一个**完全未认证**的调用者，只要拿到
            // 某个已登录用户的令牌，就能删掉他的设备行（他从此收不到推送），并让一个自己
            // 持密钥的身份继承他的 UserId；随后一次普通刷新就把令牌换成自己的设备，
            // 那个用户的推送从此发到攻击者手上。
            //
            // 未认证的调用者不得改动任何属于某个账号的行。这里没有「可能是同一台设备」
            // 这种模糊余地可讲：登录归属只能由那个登录着的人自己动。
            if (byToken.UserId.HasValue)
            {
                Logger.LogWarning(
                    "Anonymous push registration refused: token {DeviceToken} belongs to a signed-in account.",
                    PushTokenMask.Of(token));

                return Fail<PushDevice>(
                    "This push token belongs to a signed-in account on this device. "
                    + "Register it through the authenticated endpoint, or sign out first.",
                    409);
            }

            await _repository.DeleteAsync(byToken, cancellationToken);

            // ★ 显式 flush。下面要让同一个 Token 值出现在另一行上，而唯一索引不认
            // 「同一次 SaveChanges 里先删后插」。AutoSaveChanges 默认开着时这一步是空操作，
            // 但消费方把它关掉、或整段跑在一个 UnitOfWork 里时，少了它就撞唯一索引 ——
            // 而那种失败只在特定宿主配置下出现，本地跑得好好的。
            await _repository.SaveChangesAsync(cancellationToken);
        }

        if (byIdentity != null)
        {
            byIdentity.Token = token;
            byIdentity.Platform = input.Platform;
            byIdentity.DeviceName = input.DeviceName;
            byIdentity.LastSeenAt = now;
            // 没带就保留原值，不清空：这是可选的辨认信息，某个客户端版本不上报它，
            // 不该让运维手上那条线索凭空消失。
            byIdentity.ExternalDeviceId = ExternalDeviceIdOf(input) ?? byIdentity.ExternalDeviceId;
            await _repository.UpdateAsync(byIdentity, cancellationToken);
            return Ok(byIdentity);
        }

        var device = new PushDevice
        {
            // ★ Id 显式给派生值而不是交给框架生成。框架只在值为默认值时才生成，
            // 所以这一行就是「这台设备的 Id 由它的密钥决定」的全部实现。
            Id = deviceId,
            // ★ 匿名身份**从不携带登录归属**。被删掉的那一行只可能是无主的（上面已经拒掉
            // 有主的），所以这里没有什么可继承的 —— 用户归属只由认证过的注册路径写入。
            DeviceKeyHash = keyHash,
            Token = token,
            Platform = input.Platform,
            DeviceName = input.DeviceName,
            ExternalDeviceId = ExternalDeviceIdOf(input),
            LastSeenAt = now
        };

        // ★ 这里刻意**不**捕获唯一索引冲突去重试。两个请求同时为同一个令牌建行会撞一次，
        // 结果是一次 500、客户端重试即成 —— 而加一段容错要付两笔代价：它在单线程测试里
        // 造不出那个并发窗口（也就无从验证），而一段测不到的容错代码本身就是风险。
        //
        // 也不需要 IRepository.Discard：它解决的是「插入失败的实体仍留在跟踪器里，
        // 被本作用域**后续**的 SaveChanges 重放到一个无关位置」，那个形态出现在批处理循环里
        // （框架已为此撞过三次）。这里是单次操作：异常直接出栈、请求结束、作用域销毁，
        // 没有后续的 SaveChanges 可供重放。
        await _repository.InsertAsync(device, cancellationToken);
        return Ok(device);
    }

    /// <inheritdoc />
    public async Task<PushRecipientResolution> ResolveRecipientsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var resolution = new PushRecipientResolution();
        if (userIds == null || userIds.Count == 0)
            return resolution;

        var requested = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (requested.Count == 0)
            return resolution;

        var devices = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(d => d.UserId.HasValue && requested.Contains(d.UserId.Value))
            .ToListAsync(cancellationToken);

        var byUser = devices.ToLookup(d => d.UserId!.Value);

        // 按请求顺序展开，让调用方拿到的收件人次序是可预期的（便于比对与测试）。
        foreach (var userId in requested)
        {
            var owned = byUser[userId].ToList();
            if (owned.Count == 0)
            {
                resolution.UsersWithoutDevices.Add(userId);
                continue;
            }

            foreach (var device in owned)
            {
                resolution.Recipients.Add(new RecipientInput
                {
                    Address = device.Token,
                    // ★ UserId 必须带上：投递管线用它做偏好与频次上限过滤，
                    // 也用它决定这条消息归谁的站内收件箱。只填 Address 的话，
                    // 一个把推送渠道关掉的用户照样会收到 —— 过滤器对无主收件人一律放行。
                    UserId = userId,
                    Name = device.DeviceName
                });
            }
        }

        return resolution;
    }

    /// <inheritdoc />
    public async Task<PushDeviceResolution> ResolveByDeviceIdsAsync(
        IReadOnlyCollection<Guid> deviceIds, CancellationToken cancellationToken = default)
    {
        var resolution = new PushDeviceResolution();
        if (deviceIds == null || deviceIds.Count == 0)
            return resolution;

        var requested = deviceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (requested.Count == 0)
            return resolution;

        var devices = await _repository
            .AsQueryable()
            .AsNoTracking()
            .Where(d => requested.Contains(d.Id))
            .ToListAsync(cancellationToken);

        var byId = devices.ToDictionary(d => d.Id);

        foreach (var deviceId in requested)
        {
            if (!byId.TryGetValue(deviceId, out var device))
            {
                // ★ 报出来，不要只少返回一条。这台设备退役过（卸载、令牌被判死）而它的 Id
                // 还留在业务记录里 —— 那份记录的回执发不出去，调用方必须能知道这件事。
                resolution.DeviceIdsNotFound.Add(deviceId);
                continue;
            }

            resolution.Recipients.Add(new RecipientInput
            {
                Address = device.Token,
                // ★ 这一行若还登录着人就把 UserId 带上，让偏好与频次上限照常生效；
                // 纯匿名设备留空，那两个过滤器对无主收件人原样放行，而退订按地址仍然管用。
                UserId = device.UserId,
                Name = device.DeviceName
            });
        }

        return resolution;
    }

    /// <inheritdoc />
    public async Task<int> RetireAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return 0;

        var dead = await _repository.ToListAsync(d => d.Token == token, cancellationToken);
        if (dead.Count == 0)
            return 0;

        foreach (var device in dead)
        {
            await _repository.DeleteAsync(device, cancellationToken);
        }

        Logger.LogInformation(
            "Retired {Count} push device row(s) whose token was rejected as permanently invalid.", dead.Count);

        return dead.Count;
    }

    /// <summary>
    /// 规范化并校验一个上报上来的推送令牌。
    /// </summary>
    /// <remarks>
    /// 抽出来是因为三条注册路径要做同一件事，而<b>就地拒绝</b>比撞到列宽有用得多：
    /// 那一次 SaveChanges 抛出来的是一条数据库层的截断错误，既说不清是哪个字段、
    /// 也不会告诉客户端上限是多少。
    /// </remarks>
    /// <summary>
    /// 取出规范化后的客户端设备标识，空白视同没填。
    /// </summary>
    /// <remarks>
    /// 超长时返回 <see langword="null"/>（丢掉那个值）而不是抛异常：调用方在入口处已经用
    /// <see cref="TryNormalizeExternalDeviceId"/> 拒绝过一次，走到这里说明值是合法的。
    /// 万一将来有调用方漏了那一步，<b>失败关闭</b>比写坏数据好 —— 丢的是一条辨认线索，
    /// 而不是一行永远对不上任何设备的记录。
    /// </remarks>
    private static string? ExternalDeviceIdOf(RegisterPushDeviceDto input)
    {
        TryNormalizeExternalDeviceId(input.ExternalDeviceId, out var value, out _);
        return value;
    }

    /// <summary>
    /// 校验客户端自报的设备标识。
    /// </summary>
    /// <remarks>
    /// 超长<b>就地拒绝而不是截断</b>：这一列存的是一个 UUID 量级的短标识，超出 128 通常意味着
    /// 客户端把别的东西塞进来了（整段 JSON、一条日志），截断会把那个错误变成一行看起来正常、
    /// 却永远对不上任何设备的数据。与令牌那条同一口径。
    /// </remarks>
    private static bool TryNormalizeExternalDeviceId(string? raw, out string? value, out string? failureReason)
    {
        var trimmed = raw?.Trim();
        value = string.IsNullOrEmpty(trimmed) ? null : trimmed;

        if (value != null && value.Length > PushDeviceConfiguration.ExternalDeviceIdMaxLength)
        {
            value = null;
            failureReason =
                $"External device id exceeds the {PushDeviceConfiguration.ExternalDeviceIdMaxLength}-character limit.";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryNormalizeToken(string? raw, out string? token, out string? failureReason)
    {
        token = raw?.Trim();

        if (string.IsNullOrEmpty(token))
        {
            failureReason = "Push token is required.";
            return false;
        }

        if (token.Length > PushDeviceConfiguration.TokenMaxLength)
        {
            failureReason = $"Push token exceeds the {PushDeviceConfiguration.TokenMaxLength}-character limit.";
            return false;
        }

        failureReason = null;
        return true;
    }
}
