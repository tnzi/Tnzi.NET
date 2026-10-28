namespace Tnzi.Identity.Services;

/// <inheritdoc cref="IInvitationService"/>
public class InvitationService : ApplicationService, IInvitationService
{
    /// <summary>令牌在 <c>AuthToken</c> 里的归属标记。与刷新令牌、2FA 临时令牌互不干扰。</summary>
    internal const string TokenLoginProvider = IdentityConstants.LoginProvider.Invitation;

    /// <summary>同一用户同时只保留一枚：重发一张就该让上一张作废。</summary>
    internal const string TokenName = IdentityConstants.TokenName.InvitationToken;

    /// <summary>
    /// 未激活账号的锁定年限。用「一百年」而不是永久，与 <c>UserService</c> 的停用同一写法。
    /// </summary>
    /// <remarks>
    /// ★ 是年数常量而不是算好的 <c>DateTimeOffset</c> 静态字段：后者在**类型第一次被加载**时
    /// 求值，此后整个进程生命周期里都用那一刻的时间。长时间运行的进程没问题（差几天而已），
    /// 但它是那类「看着没错、在某个边界上突然不对」的写法，没有理由留着。
    /// </remarks>
    private const int PendingLockoutYears = 100;

    private readonly UserManager<User> _userManager;
    private readonly IUserService _userService;
    private readonly IAuthTokenService _authTokenService;
    private readonly IInvitationAcceptanceHandler _acceptanceHandler;
    private readonly IInvitationUrlGenerator _urlGenerator;
    private readonly IOptionsMonitor<IdentityOptions> _options;
    private readonly IUserTenantScopeProvider _scope;
    private readonly IConfiguration? _configuration;
    private readonly IAuthService? _authService;
    private readonly IFunctionAuthorizationService? _functionAuthorization;

    /// <summary>
    /// 初始化一个 <see cref="InvitationService"/> 类型的新实例。
    /// </summary>
    public InvitationService(
        IServiceProvider serviceProvider,
        UserManager<User> userManager,
        IUserService userService,
        IAuthTokenService authTokenService,
        IInvitationAcceptanceHandler acceptanceHandler,
        IInvitationUrlGenerator urlGenerator,
        IOptionsMonitor<IdentityOptions> options,
        IUserTenantScopeProvider scope,
        IConfiguration? configuration = null,
        IAuthService? authService = null,
        IFunctionAuthorizationService? functionAuthorization = null)
        : base(serviceProvider)
    {
        _userManager = Check.NotNull(userManager);
        _userService = Check.NotNull(userService);
        _authTokenService = Check.NotNull(authTokenService);
        _acceptanceHandler = Check.NotNull(acceptanceHandler);
        _urlGenerator = Check.NotNull(urlGenerator);
        _options = Check.NotNull(options);
        _scope = Check.NotNull(scope);
        _configuration = configuration;
        _authService = authService;
        _functionAuthorization = functionAuthorization;
    }

    /// <inheritdoc />
    public async Task<Result<InvitationDto>> InviteAsync(CreateInvitationDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(input.Email) && string.IsNullOrWhiteSpace(input.PhoneNumber))
        {
            // 没有任何送达方式的邀请是发不出去的。挡在这里而不是等到发信那一步，
            // 否则会留下一个永远无人接受、也没人知道为什么的 Pending 账号。
            return Fail<InvitationDto>(
                "An invitation needs an email address or a phone number to be delivered to",
                400,
                ErrorCodes.VALIDATION_ERROR);
        }

        // ★ 有效期在建号之前校验：此前它到签发那一步才用，int.MaxValue 小时在 AddHours 上抛异常时
        //   账号已经建好、已经置成 Pending，留下一个没有令牌的待接受账号与一个 500。
        if (InvalidLifetime<InvitationDto>(input.LifetimeHours) is { } invalidLifetime)
        {
            return invalidLifetime;
        }

        // ★ 经 IUserService 建号，不自己拼 User：重名校验、组织可分配性校验与角色分配都只在那一条路上。
        //   绕过去就等于把它们悄悄关掉。
        // ★★★ 用不发布注册事件的那个入口：此刻账号没有密码、登录不进去，宣布「已注册」会让订阅者
        //   （框架自带的欢迎邮件）告诉对方「账号已可用」，被邀请人先收到它、再收到真正的邀请。
        //   注册事件在接受邀请、账号真正可用时由 AcceptAsync 发布。
        var created = await _userService.CreateForInvitationAsync(new CreateUserDto
        {
            UserName = input.UserName,
            Password = null,        // 密码由本人在接受邀请时设置
            Email = input.Email,
            PhoneNumber = input.PhoneNumber,
            OrganizationId = input.OrganizationId,
            RoleIds = input.RoleIds,
        });

        if (!created.Succeeded)
        {
            return Fail<InvitationDto>(created.Message!, created.Code ?? 400, created.ErrorCode);
        }

        var user = await _userManager.FindByGuidAsync(created.Data!.Id);
        if (user == null)
        {
            return Fail<InvitationDto>("User not found after creation", 500, ErrorCodes.IDENTITY_USER_CREATE_FAILED);
        }

        // ★★★ 置 Pending 失败必须把刚建的账号收回去，不能让它留在库里。
        //   留下的是一个**没有密码、角色却已预设好、而 PendingActions 里没有那一位** 的账号 ——
        //   守卫按状态判定，None 就是放行，于是验证码登录只要收到一封邮件就能带着
        //   那些角色进来。这正是本模块要挡住的那条路径，只是换成由一次失败的写入造成。
        var pending = await MarkPendingAsync(user, input.Profile);
        if (!pending.Succeeded)
        {
            await RollbackCreatedAccountAsync(user);
            return Fail<InvitationDto>(pending.Message!, pending.Code ?? 500, pending.ErrorCode);
        }

        return await IssueAsync(user, input.LifetimeHours, isResend: false);
    }

    /// <summary>
    /// 按 id 取被邀请的账号，且只取当前租户范围内的（口径见 <see cref="UserTenantScope"/>）：
    /// 不在范围内与不存在同样返回 <c>null</c>，调用方一律答 404。
    /// </summary>
    /// <remarks>
    /// ★ 重发与撤销都必须先过这一道（以及其后的超管护栏）再碰令牌或账号。令牌表不是 <c>IMultiTenant</c>，
    /// 全局过滤器管不到它：只靠后面某一步自带的范围校验，外租户管理员就能把别家的邀请链接作废或换掉。
    /// </remarks>
    private async Task<User?> FindScopedUserAsync(Guid userId)
    {
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return null;
        }

        var scope = _scope.Current;
        if (scope.Contains(user))
        {
            return user;
        }

        LogWarning(
            "Rejected a cross-tenant invitation access: user {UserId} belongs to tenant {UserTenantId} but the request is scoped to tenant {TenantId}.",
            user.Id, user.TenantId, scope.TenantId);
        return null;
    }

    /// <summary>
    /// 调用者（非超管）要对一个超管账号的邀请动手时为 true，判据见 <see cref="SuperAdminTargetGuard"/>。
    /// </summary>
    /// <remarks>
    /// ★★★ 被邀请的账号在 <c>InviteAsync</c> 里就已经挂上了预设角色，所以「待接受的超管」就是超管目标。
    /// 重发会把一条新的接受链接交给调用者，接受是匿名的、默认还会直接签发登录令牌：
    /// 对超管目标重发，等于持 <c>user.update</c> 的普通管理员给自己发了一个超管账号。
    /// 同一个权限码下的重置密码早就挂着这道护栏，重发是同一件事（决定谁能以这个账号登录）换了一条路。
    /// 撤销同理：删账号那一步自己也会对超管答 403，在这里先拦下，回答与重发一致。
    /// </remarks>
    private Task<bool> IsSuperAdminTargetForbiddenAsync(Guid targetUserId)
        => SuperAdminTargetGuard.IsForbiddenAsync(_functionAuthorization, CurrentUser?.Id, targetUserId);

    /// <inheritdoc />
    public async Task<Result<InvitationDto>> ResendAsync(Guid userId, int? lifetimeHours = null, CancellationToken cancellationToken = default)
    {
        if (InvalidLifetime<InvitationDto>(lifetimeHours) is { } invalidLifetime)
        {
            return invalidLifetime;
        }

        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail<InvitationDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 在碰令牌之前：被邀请人手里那条链接在拒绝之后必须原样可用。
        if (await IsSuperAdminTargetForbiddenAsync(user.Id))
        {
            return Fail<InvitationDto>(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        if (!user.HasPendingAction(PendingUserActions.InvitationPending))
        {
            // 对已激活的账号「重发邀请」是没有意义的动作：那个人已经能登录了，
            // 他要的是找回密码。用 409 而不是 400，说的是「这个账号的状态不对」而非「参数不对」。
            return Fail<InvitationDto>(
                "This account has already accepted its invitation",
                409,
                ErrorCodes.IDENTITY_ACTIVATION_PENDING);
        }

        return await IssueAsync(user, lifetimeHours, isResend: true);
    }

    /// <inheritdoc />
    public async Task<Result> RevokeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await FindScopedUserAsync(userId);
        if (user == null)
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await IsSuperAdminTargetForbiddenAsync(user.Id))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        if (!user.HasPendingAction(PendingUserActions.InvitationPending))
        {
            // 撤销只针对还没被接受的邀请。已激活的账号要停用请走 DisableAsync ——
            // 那是一个语义完全不同、也该单独授权的动作。
            return Fail(
                "This account has already accepted its invitation; disable it instead of revoking",
                409,
                ErrorCodes.IDENTITY_ACTIVATION_PENDING);
        }

        // ★ 先删账号再作废令牌。删账号这一步可能被拒（IUserUsageProvider 声明账号在用时 409，超管目标 403），
        //   先作废令牌的话，拒绝时链接已经悄悄失效：管理员看到一条失败的请求以为什么都没发生，
        //   被邀请人手里的链接却打不开了，账号停在 Pending、再没有令牌，只能等人重发。
        //   反过来是安全的：账号软删之后，接受与预览按 id 取账号取不到，即使作废令牌那一步没走完，链接也用不了。
        var deleted = await _userService.DeleteAsync(user.Id);
        if (!deleted.Succeeded)
        {
            return deleted;
        }

        await _authTokenService.RemoveTokenAsync(user.Id, TokenLoginProvider, TokenName);
        await _authTokenService.RemoveTokenAsync(user.Id, TokenLoginProvider, ProfileTokenName);

        LogInformation("Invitation revoked for user {UserId} ({UserName}).", user.Id, user.UserName ?? string.Empty);
        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<InvitationPreviewDto>> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        var entry = await FindUsableAsync(token);
        if (entry == null)
        {
            return InvalidToken<InvitationPreviewDto>();
        }

        var user = await _userManager.FindByGuidAsync(entry.UserId);
        if (user == null || !user.HasPendingAction(PendingUserActions.InvitationPending))
        {
            return InvalidToken<InvitationPreviewDto>();
        }

        return Ok(new InvitationPreviewDto
        {
            UserName = user.UserName ?? string.Empty,
            MaskedEmail = MaskEmail(user.Email),
            MaskedPhoneNumber = MaskPhoneNumber(user.PhoneNumber),
            ExpiresAt = entry.ExpiresAt ?? DateTime.UtcNow,
            Profile = await ReadProfileAsync(user.Id),
        });
    }

    /// <inheritdoc />
    public async Task<Result<AcceptInvitationResultDto>> AcceptAsync(AcceptInvitationDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entry = await FindUsableAsync(input.Token);
        if (entry == null)
        {
            return InvalidToken<AcceptInvitationResultDto>();
        }

        var user = await _userManager.FindByGuidAsync(entry.UserId);
        if (user == null || !user.HasPendingAction(PendingUserActions.InvitationPending))
        {
            return InvalidToken<AcceptInvitationResultDto>();
        }

        // ★★★ 顺序即安全边界：先让消费应用消化提交，它说完成了才激活。
        //     handler 说没完成（还差绑 TOTP 之类）时，令牌不消费、状态不变，
        //     用户拿同一条链接回来继续 —— 与 passkey 注册令牌把校验和消费分开是同一理由。
        var outcome = await _acceptanceHandler.AcceptAsync(user, input, await ReadProfileAsync(user.Id), cancellationToken);
        if (!outcome.Succeeded)
        {
            return Fail<AcceptInvitationResultDto>(outcome.Message!, outcome.Code ?? 400, outcome.ErrorCode);
        }

        if (!outcome.Data!.Completed)
        {
            return Ok(new AcceptInvitationResultDto
            {
                Completed = false,
                RemainingSteps = outcome.Data.RemainingSteps,
            });
        }

        // ★ 抢占：谁把令牌从「未使用」翻成「已使用」，谁才有资格激活这个账号。
        //   MarkTokenAsUsedAsync 内部是一次条件更新，输的一方拿到 false。
        //   没有这一步，同一条链接被并发提交两次会激活两次、发两份登录令牌。
        if (!await _authTokenService.MarkTokenAsUsedAsync(entry.Id))
        {
            return InvalidToken<AcceptInvitationResultDto>();
        }

        // ★ 解锁与清状态放在**同一次写**里。分两次写有个安静的坏结果：
        //   SetLockoutEndDateAsync 在 LockoutEnabled 为 false 时直接返回失败且什么都不写，
        //   而紧随其后的 UpdateAsync 照样会把 PendingActions 落库 ——
        //   于是账号「已激活」但锁到一百年后：本人刚设好密码、链接也用掉了，却登不进去。
        // ★ 只清这一位，不是赋 None：赋 None 会把同时欠着的义务位（比如管理员在邀请
        //   发出后又勾了「下次登录必须改密」）一起抹掉，而它不报错也不会让测试变红。
        // ★ 清锁定是安全的，因为待接受期间这把锁只可能是邀请自己的占位：UserService 的停用 / 锁定
        //   对待接受的账号一律 409（指向撤销邀请）。否则管理员停掉的人也会在这里被一并放出来：
        //   停用写下的「一百年后」与占位锁定形状相同，这里分不出谁是谁。
        user.PendingActions &= ~PendingUserActions.InvitationPending;
        user.LockoutEnd = null;
        var activated = await _userManager.UpdateAsync(user);
        if (!activated.Succeeded)
        {
            // 令牌已经消费掉了，退不回去。如实报失败并留下足以定位的日志：
            // 这个账号此刻处于「令牌用掉了但没激活」的状态，只能由管理员重发。
            LogError(
                "Invitation token for user {UserId} was consumed but the account could not be activated: {Errors}. "
                + "The invitee must be sent a new invitation.",
                user.Id, activated.FormatErrors());

            return Fail<AcceptInvitationResultDto>(
                "Could not activate the account. Please ask your administrator to send a new invitation.",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        LogInformation("Invitation accepted by user {UserId} ({UserName}).", user.Id, user.UserName ?? string.Empty);

        // 预填资料只在「已邀请、还没接受」这个窗口里有意义，交给 handler 之后就该消失（见 ProfileTokenName）。
        await _authTokenService.RemoveTokenAsync(user.Id, TokenLoginProvider, ProfileTokenName);

        // 账号从这一刻起才可用：注册事件在这里发（发邀请时刻意没发，见 InviteAsync），每个账号恰好一次：
        // 令牌的条件消费保证同一份邀请只会走到这里一次。
        await PublishEventAsync(new UserRegisteredEvent
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email,
            RegistrationTime = DateTime.UtcNow,
        });

        var result = new AcceptInvitationResultDto { Completed = true };

        if (_options.CurrentValue.Invitation.SignInAfterAccept && _authService != null)
        {
            // ★ 走 IssueTokenAsync 而不是自己签：这条路上有登录守卫、2FA 判定、会话协调器
            //   与登录成功事件。账号刚转出 Pending，此刻它和任何账号一样该被守卫过一遍
            //   （例如消费方挂的准入守卫）。管理员要拦住待接受的人，用的是撤销邀请，不是停用。
            var issued = await _authService.IssueTokenAsync(user, LoginMethod.Invitation);
            if (issued.Succeeded)
            {
                result.Token = issued.Data;
            }
            else
            {
                // 激活本身已经成功且不可回退（令牌已消费）。签发失败不该把它说成失败 ——
                // 那会让用户以为要重来一遍，而他手里的链接已经用掉了。让他去登录页即可。
                LogWarning(
                    "Invitation accepted for user {UserId} but token issuance was denied: {Reason}",
                    user.Id, issued.Message ?? "unknown");
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// 调用方指定的链接有效期不在 [1, <see cref="InvitationOptions.MaxLinkLifetimeHours"/>] 小时内时给出 400；
    /// 没指定（用配置值）或在范围内时为 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 此前只夹了下限：一个大到离谱的值要么在签发时抛异常，要么签出一条百年有效的接受链接，而接受是匿名的。
    /// 上限 30 天与配置项的上限相同（理由见 <see cref="InvitationOptions.LinkLifetimeHours"/>）。
    /// </remarks>
    private Result<T>? InvalidLifetime<T>(int? lifetimeHours)
    {
        if (lifetimeHours is null or (>= 1 and <= InvitationOptions.MaxLinkLifetimeHours))
        {
            return null;
        }

        return Fail<T>(
            $"The invitation lifetime must be between 1 and {InvitationOptions.MaxLinkLifetimeHours} hours",
            400,
            ErrorCodes.VALIDATION_ERROR);
    }

    /// <summary>
    /// 签发一枚新的邀请令牌，拼出链接，并发出事件让应用去投递。
    /// </summary>
    private async Task<Result<InvitationDto>> IssueAsync(User user, int? lifetimeHours, bool isResend)
    {
        // 调用方给的有效期已在建号 / 重发之前由 InvalidLifetime 挡过；配置值由选项校验器管住同一个范围，
        // 这里再夹一次只是不让一份没经过校验的配置在这一步抛异常。
        var hours = lifetimeHours
            ?? Math.Clamp(_options.CurrentValue.Invitation.LinkLifetimeHours, 1, InvitationOptions.MaxLinkLifetimeHours);
        var expiresAt = DateTime.UtcNow.AddHours(hours);

        // 256 位随机数：没有字典可查，所以存哈希时不需要加盐或慢哈希（理由写在原语里）。
        var token = OneTimeToken.Create();

        // upsert 语义：唯一索引 (UserId, LoginProvider, Name, SessionId) 让重发直接覆盖，
        // 上一条链接随之失效 —— 这正是想要的行为，不必额外去删。
        await _authTokenService.SaveTokenAsync(
            user.Id,
            TokenLoginProvider,
            TokenName,
            OneTimeToken.Hash(token),
            expiresAt);

        var acceptUrl = _urlGenerator.GenerateUrl(user, token);

        await PublishEventAsync(new UserInvitedEvent
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            AcceptUrl = acceptUrl,
            ExpiresAt = expiresAt,
            InvitedBy = CurrentUser?.Id,
            IsResend = isResend,
            SiteName = SiteNameResolver.Resolve(_configuration, Logger),
        });

        LogInformation(
            "Invitation {Kind} for user {UserId}, valid until {ExpiresAt:o}.",
            isResend ? "resent" : "issued", user.Id, expiresAt);

        return Ok(new InvitationDto
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            AcceptUrl = acceptUrl,
            ExpiresAt = expiresAt,
        });
    }

    /// <summary>
    /// 把账号置为「等待接受邀请」，并顺带锁上。
    /// </summary>
    /// <remarks>
    /// ★ 两件事都要做，但作用不同：<see cref="PendingUserActions.InvitationPending"/> 是安全边界
    /// （守卫读它），锁定只是搭便车 —— 框架里「活跃用户」的口径是按 <c>LockoutEnd</c> 算的，
    /// 一并置上，统计与筛选就自动把未激活的人排除，不必逐处改。
    /// </remarks>
    private async Task<Result> MarkPendingAsync(User user, JsonElement? profile)
    {
        // ★ 安全边界先落地，且**必须检查结果**：守卫读的是这个字段，写失败而无人过问，
        //   等于放出一个没有密码、角色已预设、却不受守卫管辖的账号。
        //   一次 UpdateAsync 即可 —— UserManager 保存的是整个实体。
        user.PendingActions |= PendingUserActions.InvitationPending;
        var stateSaved = await _userManager.UpdateAsync(user);
        if (!stateSaved.Succeeded)
        {
            return Fail(
                $"Failed to mark the account as pending: {stateSaved.FormatErrors()}",
                500,
                ErrorCodes.IDENTITY_USER_UPDATE_FAILED);
        }

        // ★ 锁定是搭便车，不是安全边界（理由见 PendingUserActions）：它只让既有的
        //   「活跃用户」口径自动排除这个账号。失败了记一条告警就够 —— 拿它当硬失败，
        //   会让一个 store 不支持锁定的部署连邀请都发不出去，而守卫照样拦得住。
        //   顺序不能反：SetLockoutEndDateAsync 在 LockoutEnabled 为 false 时直接返回失败。
        var lockoutEnabled = await _userManager.SetLockoutEnabledAsync(user, true);
        var lockedOut = lockoutEnabled.Succeeded
            ? await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(PendingLockoutYears))
            : lockoutEnabled;

        if (!lockedOut.Succeeded)
        {
            LogWarning(
                "Invited user {UserId} could not be locked out ({Errors}); the pending-activation guard still blocks sign-in, "
                + "but the account will be counted as active in user statistics.",
                user.Id, lockedOut.FormatErrors());
        }

        if (profile.HasValue)
        {
            await _authTokenService.SaveTokenAsync(
                user.Id,
                TokenLoginProvider,
                ProfileTokenName,
                profile.Value.GetRawText());
        }

        return Ok();
    }

    /// <summary>
    /// 置 Pending 失败时收回刚创建的账号。
    /// </summary>
    /// <remarks>
    /// 补偿失败只能记日志：此刻返回给管理员的已经是一个失败结果，而库里留着一个
    /// <b>不受守卫管辖</b>的无密码账号。这条日志是唯一的信号，措辞要让人看懂该去做什么。
    /// </remarks>
    private async Task RollbackCreatedAccountAsync(User user)
    {
        var deleted = await _userService.DeleteAsync(user.Id);
        if (!deleted.Succeeded)
        {
            LogError(
                "Invitation setup failed for user {UserId} ({UserName}) and the account could not be rolled back: {Reason}. "
                + "This account has no password but is NOT in the pending state, so the activation guard will not block it. "
                + "Delete or disable it manually.",
                user.Id, user.UserName ?? string.Empty, deleted.Message ?? "unknown");
        }
    }

    /// <summary>
    /// 管理员预填资料的存放位置。
    /// </summary>
    /// <remarks>
    /// ★ 借 <c>AuthToken</c> 存而不是给 <c>User</c> 加一列：这份数据只在
    /// 「已邀请、还没接受」这个窗口里有意义。放进用户表就意味着
    /// 一列永久存在的、只有极少数行非空的 JSON。
    /// <para>
    /// ★★ 它<b>不带过期时间</b>，所以过期清理永远清不到它；撤销是软删账号，这一行也不会随账号消失。
    /// 生命周期因此由本服务显式收尾：接受成功（激活落库之后）与撤销成功时删掉。
    /// 不给它设过期是刻意的：重发可以延长链接有效期，资料行若按旧的过期时间被清掉，被邀请人接受时就丢了预填资料。
    /// 此前两处都不删，管理员预填的个人资料在每一份接受过或撤销过的邀请上无限期留存。
    /// </para>
    /// </remarks>
    private const string ProfileTokenName = "InvitationProfile";

    private async Task<JsonElement?> ReadProfileAsync(Guid userId)
    {
        var raw = await _authTokenService.GetTokenAsync(userId, TokenLoginProvider, ProfileTokenName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            // 存进去时是合法 JSON，读出来不是，说明这一行被外部改过。
            // 记下来并当作「没有预填资料」，不要让整条接受流程死在一份可有可无的初值上。
            LogWarning(
                "Invitation profile for user {UserId} is not valid JSON ({Error}); ignoring it.",
                userId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 按哈希取回一枚仍然可用的令牌。失效 / 过期 / 不存在一律得到 <c>null</c>。
    /// </summary>
    private async Task<AuthToken?> FindUsableAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var entry = await _authTokenService.FindTokenByValueAsync(TokenLoginProvider, TokenName, OneTimeToken.Hash(token));
        if (entry == null || entry.IsUsed)
        {
            return null;
        }

        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.UtcNow ? null : entry;
    }

    /// <summary>
    /// 失效、过期、不存在共用同一个回答 —— 区分开就是在帮人试探哪些邀请链接是真的。
    /// </summary>
    private Result<T> InvalidToken<T>()
        => Fail<T>("Invalid or expired invitation link", 400, ErrorCodes.IDENTITY_INVITATION_INVALID);

    /// <summary>
    /// 掩码邮箱：<c>tan@example.com</c> → <c>t**@e******.com</c>。
    /// </summary>
    /// <remarks>
    /// 本人认得出是自己的地址，旁人拼不回来。留首字母与顶级域，其余一律遮掉。
    /// 首字母按码元取：本地部分或域名以 emoji 等补充平面字符开头时那一段一个字符都不露，
    /// 而不是露出半个代理项（预览页上是一个替换字符）。
    /// </remarks>
    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1)
        {
            return "***";
        }

        var local = email[..at];
        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');

        var localHead = local.FitToLength(1);
        var domainHead = domain.FitToLength(1);
        var maskedLocal = localHead + new string('*', Math.Max(1, local.Length - localHead.Length));
        var maskedDomain = dot > 0
            ? domainHead + new string('*', Math.Max(1, dot - domainHead.Length)) + domain[dot..]
            : domainHead + new string('*', Math.Max(1, domain.Length - domainHead.Length));

        return $"{maskedLocal}@{maskedDomain}";
    }

    /// <summary>
    /// 掩码手机号：只保留末四位，其余一律遮掉；太短则整个遮掉。
    /// </summary>
    /// <remarks>
    /// ★ 与上面的 <see cref="MaskEmail"/> 同一条判据：本人认得出，旁人拼不回来。
    /// <b>刻意不保留前缀</b> —— 前三位是国家码 / 区号，与末四位合起来会把可能的号码
    /// 收窄到很小的集合，而号主只靠末四位就认得出自己的号。这一页由<b>任何拿到邀请链接的人</b>
    /// 可达（链接本身就是凭据），所以它比框架发码回执用的
    /// <c>ContactAddressMasking.MaskPhone</c> 只能更严、不能更松；现在两者等严。
    /// </remarks>
    private static string? MaskPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        return phoneNumber.Length <= 4
            ? new string('*', phoneNumber.Length)
            : $"{new string('*', phoneNumber.Length - 4)}{phoneNumber[^4..]}";
    }
}
