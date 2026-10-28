namespace Tnzi.Chat.Services;

public class BroadcastService : ApplicationService, IBroadcastService
{
    private readonly IRepository<User, Guid> _userRepository;
    private readonly IOptions<MultiTenancyOptions> _multiTenancyOptions;
    private readonly IUserRoleService? _userRoleService;
    private readonly IFunctionAuthorizationService? _functionAuthorization;
    private readonly IChatAccessService _chatAccess;
    private readonly IUserTenantScopeProvider _userScope;
    private readonly ICurrentTenant _currentTenant;
    private readonly IServiceScopeFactory _scopeFactory;

    public BroadcastService(
        IServiceProvider serviceProvider,
        IRepository<User, Guid> userRepository,
        IOptions<MultiTenancyOptions> multiTenancyOptions,
        IChatAccessService chatAccess,
        IUserTenantScopeProvider userScope,
        ICurrentTenant currentTenant,
        IServiceScopeFactory scopeFactory,
        IUserRoleService? userRoleService = null,
        IFunctionAuthorizationService? functionAuthorization = null) : base(serviceProvider)
    {
        _userRepository = Check.NotNull(userRepository);
        _multiTenancyOptions = Check.NotNull(multiTenancyOptions);
        _chatAccess = Check.NotNull(chatAccess);
        _userScope = Check.NotNull(userScope);
        _currentTenant = Check.NotNull(currentTenant);
        _scopeFactory = Check.NotNull(scopeFactory);
        _userRoleService = userRoleService;
        _functionAuthorization = functionAuthorization;
    }

    public Task<Result<int>> BroadcastToUsersAsync(IEnumerable<Guid> userIds, string content)
    {
        Check.NotNullOrWhiteSpace(content, nameof(content));
        return SendToUsersAsync(userIds, new ChatNotification { Content = content }, BroadcastTargetType.Users);
    }

    public Task<Result<int>> NotifyUsersAsync(IEnumerable<Guid> userIds, ChatNotification notification)
    {
        Check.NotNull(notification);
        Check.NotNullOrWhiteSpace(notification.Content, nameof(notification.Content));
        return SendToUsersAsync(userIds, notification, BroadcastTargetType.Users);
    }

    public Task<Result<int>> BroadcastToRoleAsync(Guid roleId, string content)
    {
        Check.NotNullOrWhiteSpace(content, nameof(content));
        return NotifyRoleAsync(roleId, new ChatNotification { Content = content });
    }

    public async Task<Result<int>> NotifyRoleAsync(Guid roleId, ChatNotification notification)
    {
        Check.NotNull(notification);
        Check.NotNullOrWhiteSpace(notification.Content, nameof(notification.Content));
        if (_userRoleService == null)
            return Fail<int>("Role broadcast requires Identity module.", 501);
        var userIds = await _userRoleService.GetRoleUserIdsAsync(roleId);
        return await SendToUsersAsync(userIds, notification, BroadcastTargetType.Roles, "1 role(s)");
    }

    public async Task<Result<int>> BroadcastAsync(BroadcastDto input)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.Content, nameof(input.Content));

        var tooLong = TooLong(new ChatNotification { Content = input.Content });
        if (tooLong != null) return Fail<int>(tooLong, 400);

        // System-wide notification: enumerate every (non-deleted) user. The repository's
        // global query filter excludes soft-deleted users. This loads user rows to project
        // their ids - acceptable for an admin broadcast (the per-user fan-out below is the
        // dominant cost); very large deployments would override with a queued mechanism.
        BroadcastTargetType targetType;
        string summary;
        List<Guid> targetIds;

        if (input.All)
        {
            // Multi-tenant guard: User is NOT IMultiTenant (no TenantId), so the global query
            // filter cannot scope this enumeration to the current tenant - "every user" would
            // span all tenants and leak one tenant's broadcast to the others. Require explicit
            // role/user targeting in multi-tenant mode.
            if (_multiTenancyOptions.Value.Enabled)
                return Fail<int>("System-wide broadcast is not supported in multi-tenant mode; target by role or user instead.", 400);

            // Exclude super admins from a system-wide business broadcast: they are
            // maintenance/operations accounts, not business recipients, and a
            // "notify everyone" business message has no meaning for them (it would
            // just fill their System conversation with business noise). Explicit
            // role/user targeting (the else branch) is honoured as-is - if an operator
            // deliberately targets a super-admin role, that is intentional.
            var excludedIds = _functionAuthorization == null
                ? new List<Guid>()
                : (await _functionAuthorization.GetSuperAdminUserIdsAsync()).ToList();
            var allUsers = await _userRepository.ToListAsync(u => !excludedIds.Contains(u.Id));
            targetIds = allUsers.Select(u => u.Id).ToList();
            targetType = BroadcastTargetType.All;
            summary = "All users";
        }
        else
        {
            var targets = new HashSet<Guid>();
            if (input.UserIds != null) foreach (var u in input.UserIds) targets.Add(u);
            if (input.RoleIds != null && _userRoleService != null)
            {
                foreach (var roleId in input.RoleIds)
                {
                    var users = await _userRoleService.GetRoleUserIdsAsync(roleId);
                    foreach (var u in users) targets.Add(u);
                }
            }
            if (targets.Count == 0) return Fail<int>("No broadcast targets resolved.", 400);
            targetIds = targets.ToList();

            var roleCount = input.RoleIds?.Count ?? 0;
            var userCount = input.UserIds?.Count ?? 0;
            if (roleCount > 0)
            {
                targetType = BroadcastTargetType.Roles;
                summary = userCount > 0 ? $"{roleCount} role(s) + {userCount} user(s)" : $"{roleCount} role(s)";
            }
            else
            {
                targetType = BroadcastTargetType.Users;
                summary = $"{userCount} user(s)";
            }
        }

        // Admin-UI broadcast: plain text, provenance carried by the CurrentUser (SenderId).
        // Unlike the direct/rich paths this always records the log - even a resolved-but-empty
        // "All users" audience is a real admin action worth an audit row.
        var notification = new ChatNotification { Content = input.Content };
        var (delivered, failed) = await DeliverAsync(targetIds, notification);
        await RecordBroadcastAsync(notification, targetType, summary, delivered);
        return Ok(delivered, DeliverySummary(delivered, failed));
    }

    /// <summary>
    /// 直接/富通知发送路径：规范化目标、无目标短路（不记日志），否则投递并记一次审计。
    /// </summary>
    private async Task<Result<int>> SendToUsersAsync(
        IEnumerable<Guid> userIds, ChatNotification notification, BroadcastTargetType targetType, string? summary = null)
    {
        // ★ 越界的正文/标题/链接在 SQL Server / PostgreSQL 上是一次 DbUpdateException，
        // 也就是给调用方一个 500；正确的答复是 400。校验放在这个汇合点上，
        // 三个公开入口共用（各写一遍正是这类规则漂开的方式）。
        var tooLong = TooLong(notification);
        if (tooLong != null) return Fail<int>(tooLong, 400);

        var ids = (userIds ?? Enumerable.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return Ok(0);

        var (delivered, failed) = await DeliverAsync(ids, notification);
        await RecordBroadcastAsync(notification, targetType, summary ?? $"{ids.Count} user(s)", delivered);
        return Ok(delivered, DeliverySummary(delivered, failed));
    }

    /// <summary>受限字段（内容 / 标题 / 链接 / 分类 / 来源）任一越界时给出说明，合法时 <see langword="null"/>。</summary>
    private static string? TooLong(ChatNotification notification)
        => ChatFieldLimits.Exceeded(notification.Content, ChatFieldLimits.MessageContent, "Notification content")
           ?? ChatFieldLimits.Exceeded(notification.Title, ChatFieldLimits.Title, "Notification title")
           ?? ChatFieldLimits.Exceeded(notification.LinkUrl, ChatFieldLimits.LinkUrl, "Notification link")
           ?? ChatFieldLimits.Exceeded(notification.Category, ChatFieldLimits.Category, "Notification category")
           ?? ChatFieldLimits.Exceeded(notification.Source, ChatFieldLimits.BroadcastSource, "Notification source");

    /// <summary>投递结果的一句话说明。全部成功时不说话（免得每次广播都带一句噪音）。</summary>
    private static string? DeliverySummary(int delivered, int failed)
        => failed == 0
            ? null
            : $"{delivered} recipient(s) received the broadcast; {failed} could not be delivered and were skipped.";

    /// <summary>
    /// 逐用户投递到其「系统通知」会话（幂等 DirectKey `system:{userId:N}`）。纯 fan-out，不记审计。
    /// 返回（成功人数, 失败人数）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>一个人失败不能掀掉整轮扇出。</b>每个收件人各自单独提交（见 <see cref="DeliverToRecipientAsync"/>），
    /// 所以第 500 个抛异常时前 499 条<b>已经落库、已经推给对方了</b>；
    /// 异常一路出栈的话 <see cref="RecordBroadcastAsync"/> 也不会执行，管理员看到的是一个 500，
    /// 于是他重发一次 —— 前 499 个人收到两条。取舍与 <c>RecordBroadcastAsync</c> 那条一致：
    /// 记下来、数出来、接着发，最后把失败数报回去。
    /// </para>
    /// <para>
    /// ★★ <b>「单独提交」必须在物理上独立于调用方的事务。</b>本服务作用域里的 <c>ExecuteInUnitOfWorkAsync</c>
    /// 在调用方已经开着事务时（宿主开了 <c>AspNetCore:EnableGlobalUnitOfWork</c>，或消费方在自己的工作单元里调
    /// <c>NotifyUsersAsync</c>）是<b>嵌套</b>的：内层「提交」只 flush，内层异常回滚会把环境事务整个撤销。
    /// 于是第 N 个人失败会让前 N-1 人的会话、消息与挂起的推送一起消失，异常被吞、循环继续，
    /// 此后的人在自动提交模式下落库，返回的送达数仍把前 N-1 人算作成功。所以每个收件人在自己的 DI 作用域
    /// （自带 DbContext 与工作单元）里提交，与 Finance 周期单据「每期一个作用域」同一手法。
    /// 代价是投递不随调用方回滚：调用方的事务之后失败，已经发出的通知不会被撤回。
    /// 要「业务提交了才通知」的调用方应在提交之后再调（例如在事件处理器里）。
    /// </para>
    /// <para>
    /// ★★ <b>多租户：每个收件人的行落在收件人自己的租户里。</b>会话 / 成员 / 消息都是 <c>IMultiTenant</c>，
    /// 全局过滤器是严格等值、新行的 <c>TenantId</c> 取环境租户。没有租户上下文的调用方（后台服务、宿主管理员，
    /// 文档明确允许从这些地方调）若按环境租户查找与写入，查不到收件人在租户 A 的系统会话，于是在
    /// <c>TenantId = null</c> 下另建一套：收件人永远看不见，投递却记成功。所以先按用户表查出每个收件人的租户，
    /// 再在 <see cref="ICurrentTenant.Change"/> 里投递。用户表里查不到的 id（不存在或已软删）不论单租户多租户都计为失败，不为它建任何行。
    /// </para>
    /// <para>
    /// ★★ <b>调用方钉在某个租户上时（已认证且有租户上下文），只投给本租户的账号。</b>越界的 id 计为失败。
    /// 上一条让行落进收件人的租户，若不拦这一道，一个租户的管理员就能往别家租户员工的系统通知里写消息。
    /// <c>User</c> 不是 <c>IMultiTenant</c>，这道边界只能由 <see cref="IUserTenantScopeProvider"/> 补。
    /// </para>
    /// </remarks>
    private async Task<(int Delivered, int Failed)> DeliverAsync(List<Guid> ids, ChatNotification notification)
    {
        var failed = 0;

        // 调用方的租户边界：越界与不存在的 id 一起省略，计为失败（不建行、不推送）。不裁剪时原样返回、不查库。
        if (ids.Count > 0)
        {
            var inScope = (await _userScope.FilterAsync(ids)).ToHashSet();
            if (inScope.Count < ids.Count)
            {
                failed += ids.Count(id => !inScope.Contains(id));
                ids = ids.Where(inScope.Contains).ToList();
            }
        }

        // A broadcast is a message too: skip recipients who currently can't use chat
        // (no chat.use) so nothing accumulates in their System conversation while
        // disabled. Fail-open when Authorization is absent (FilterDisabledAsync → empty).
        if (ids.Count > 0)
        {
            var disabled = await _chatAccess.FilterDisabledAsync(ids);
            if (disabled.Count > 0) ids = ids.Where(id => !disabled.Contains(id)).ToList();
        }

        // 收件人必须是存在的账号（已软删的不算）：查不到的 id 计为失败，不为它建任何行。
        // 单租户时也要查：租户边界不裁剪时原样放行，chat.use 门未激活时同样放行，此前一个不存在的 id
        // 会得到一个永远没人看的系统会话，还被计作送达。多租户时同一次查询带出每个收件人的租户（见 remarks）。
        Dictionary<Guid, Guid?>? recipientTenants = null;
        if (ids.Count > 0)
        {
            var known = (await _userRepository.GetListAsync(ids)).ToDictionary(u => u.Id, u => u.TenantId);
            if (known.Count < ids.Count)
            {
                failed += ids.Count(id => !known.ContainsKey(id));
                ids = ids.Where(known.ContainsKey).ToList();
            }

            if (_multiTenancyOptions.Value.Enabled)
                recipientTenants = known;
        }

        var now = DateTime.UtcNow;
        var preview = ChatPreview.Build(MessageContentType.System, notification.Content);
        var delivered = 0;

        foreach (var uid in ids)
        {
            try
            {
                // 查找、写入与事件发布都在收件人的租户里进行（事件处理器经 EventTenantContext 取到同一个租户）。
                // 租户覆盖存在静态 AsyncLocal 上，下面新建的作用域里的 DbContext 与 ICurrentTenant 读到的是同一个。
                using var recipientTenant = recipientTenants != null ? _currentTenant.Change(recipientTenants[uid]) : null;

                await DeliverToRecipientAsync(uid, notification, now, preview);
                delivered++;
            }
            catch (Exception ex)
            {
                failed++;
                Logger.LogError(ex,
                    "Broadcast delivery to user {UserId} failed; the remaining recipients are unaffected.", uid);
            }
        }

        return (delivered, failed);
    }

    /// <summary>
    /// 投递给一个收件人：在<b>它自己的</b> DI 作用域与工作单元里写会话 / 成员 / 消息，提交之后发布推送事件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 作用域随本次投递结束而释放：失败一方的变更跟踪器连同它一起消失，不会被下一个收件人的保存重放，
    /// 也碰不到调用方作用域里的任何东西（缘由见 <see cref="DeliverAsync"/>）。
    /// </para>
    /// <para>
    /// ★ <b>复用已有的系统会话时只写 <c>LastMessageAt</c> / <c>LastMessagePreview</c>（连同修改审计）这几列</b>，
    /// 不把读出来的快照整行写回：读写之间管理员删掉这个会话（<c>IsDeleted = true</c>）的话，整行写回会把它改回 false。
    /// 条件写走全局过滤器，已删除的会话不会被更新。与普通消息的发送路径同一做法。
    /// 这条写先于成员与消息：它一行都没写到，说明会话在读出之后被删了，就按「没有会话」新开一个，
    /// 而不是把消息写进一个收件人永远看不见的已删除会话、还把它计作送达。
    /// </para>
    /// </remarks>
    private async Task DeliverToRecipientAsync(Guid uid, ChatNotification notification, DateTime now, string preview)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var conversations = services.GetRequiredService<IRepository<Conversation, Guid>>();
        var members = services.GetRequiredService<IRepository<ConversationMember, Guid>>();
        var messages = services.GetRequiredService<IRepository<ChatMessage, Guid>>();
        var modifierId = CurrentUser?.Id;

        await RunInOwnUnitOfWorkAsync(services.GetService<IUnitOfWorkManager>(), async ct =>
        {
            var key = $"system:{uid:N}";
            var conversationId = await conversations.AsQueryable()
                .Where(c => c.DirectKey == key)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);

            if (conversationId != null)
            {
                // Reuse path: refresh the last-message columns first. The conditional write goes through
                // the global filter, so a conversation deleted after the read above is not touched (and
                // not revived); in that case it is treated as absent and a fresh one is opened below.
                // Writing the message into the deleted one instead would count it as delivered while the
                // user can never see it. The DirectKey unique index excludes soft-deleted rows.
                var existingId = conversationId.Value;
                await conversations.EnsureTransactionStartedAsync(ct);
                var refreshed = await conversations.AsQueryable()
                    .Where(c => c.Id == existingId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.LastMessageAt, now)
                        .SetProperty(c => c.LastMessagePreview, preview)
                        .SetProperty(c => c.LastModificationTime, now)
                        .SetProperty(c => c.LastModifierId, modifierId), ct);

                if (refreshed == 0)
                {
                    conversationId = null;
                }
                else
                {
                    // Bump unread in the database (two broadcasts to the same user must both count, and the
                    // user's concurrent mark-read / delete-chat must not be written back from a stale snapshot);
                    // a new broadcast also re-surfaces a hidden System conversation.
                    // If the member row is missing (data inconsistency), insert it.
                    await members.EnsureTransactionStartedAsync(ct);
                    var bumped = await members.AsQueryable()
                        .Where(m => m.ConversationId == existingId && m.UserId == uid)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(m => m.UnreadCount, m => m.UnreadCount + 1)
                            .SetProperty(m => m.IsHidden, false), ct);
                    if (bumped == 0)
                    {
                        await members.InsertAsync(new ConversationMember
                        {
                            ConversationId = existingId,
                            UserId = uid,
                            Role = MemberRole.Member,
                            UnreadCount = 1
                        }, ct);
                    }
                }
            }

            if (conversationId == null)
            {
                var conv = new Conversation
                {
                    Type = ConversationType.System,
                    DirectKey = key,
                    Title = "System Notifications",
                    MemberCount = 1,
                    LastMessageAt = now,
                    LastMessagePreview = preview
                };
                await conversations.InsertAsync(conv, ct);

                // 新会话的成员直接带 UnreadCount = 1 建出来（Guid 主键在 Add 时已生成，外键可以直接引用）。
                await members.InsertAsync(new ConversationMember
                {
                    ConversationId = conv.Id,
                    UserId = uid,
                    Role = MemberRole.Member,
                    UnreadCount = 1
                }, ct);
                conversationId = conv.Id;
            }

            var msg = new ChatMessage
            {
                ConversationId = conversationId.Value,
                SenderId = null,
                SentAt = now,
                ContentType = MessageContentType.System,
                Content = notification.Content,
                Title = notification.Title,
                LinkUrl = notification.LinkUrl,
                Category = notification.Category
            };
            await messages.InsertAsync(msg, ct);

            return (ConversationId: conversationId.Value, Message: msg);
        },
        async delivered =>
        {
            if (EventBus == null) return;

            await EventBus.PublishAsync(new ConversationMessageSentEvent
            {
                ConversationId = delivered.ConversationId,
                MessageId = delivered.Message.Id,
                SenderId = null,
                ContentType = MessageContentType.System,
                Preview = preview,
                RecipientUserIds = new List<Guid> { uid },
                // ★ 与普通消息一样带完整消息体：客户端只在消息体存在时把它追加进打开着的会话，
                // 随后对可见的活动会话标已读：不带它，用户开着「系统通知」时未读被清零、通知却没出现。
                // 系统消息没有发送者，不需要补发送者资料。
                Message = delivered.Message.MapTo<ChatMessageDto>()
            });
        });
    }

    /// <summary>
    /// 记录一次广播（辅助操作，失败静默，不影响广播主流程）。
    /// </summary>
    /// <remarks>
    /// 同样在自己的作用域里提交：在调用方的环境事务里嵌套执行时，审计行插入失败的回滚会把调用方的整个事务撤销，
    /// 而这里吞掉异常、调用方照常往下走，它此后的写入落在任何事务之外。
    /// </remarks>
    private async Task RecordBroadcastAsync(ChatNotification notification, BroadcastTargetType targetType, string summary, int recipientCount)
    {
        try
        {
            var senderId = CurrentUser?.Id;
            await using var scope = _scopeFactory.CreateAsyncScope();
            var logs = scope.ServiceProvider.GetRequiredService<IRepository<BroadcastLog, Guid>>();

            await RunInOwnUnitOfWorkAsync(scope.ServiceProvider.GetService<IUnitOfWorkManager>(), async ct =>
            {
                await logs.InsertAsync(new BroadcastLog
                {
                    SenderId = senderId,
                    Content = notification.Content,
                    TargetType = targetType,
                    TargetSummary = summary,
                    RecipientCount = recipientCount,
                    Source = notification.Source
                }, ct);
                return true;
            });
        }
        catch (Exception ex)
        {
            // Logging the broadcast is auxiliary; never fail the broadcast over it. Still
            // surface the failure - a silently missing audit row is indistinguishable from
            // a broadcast that never happened.
            Logger.LogWarning(ex, "Failed to record the broadcast audit row ({TargetType}, {Summary})", targetType, summary);
        }
    }

    /// <summary>
    /// 在<b>给定作用域的</b>工作单元里跑一段写入：成功提交、异常回滚后重抛；提交成功后再执行 <paramref name="afterCommit"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 形状与 <c>ApplicationService.ExecuteInUnitOfWorkAsync</c> 相同，区别只在管理器来自调用方新建的作用域：
    /// 那正是让它够不到调用方环境事务的全部理由。
    /// </para>
    /// <para>
    /// ★ <paramref name="afterCommit"/> 必须在<b>这个方法里</b>执行，不能交回调用方：调用方的执行流上挂着的仍是调用方的
    /// 环境事务（如果有），在那里发布的事件会被事务感知的总线推迟到调用方提交时，调用方随后回滚的话，
    /// 已经落库的这一条就再也不会被推送。在这里，环境工作单元是本作用域那个、已经提交完毕的，发布立即生效。
    /// </para>
    /// </remarks>
    private static async Task RunInOwnUnitOfWorkAsync<T>(
        IUnitOfWorkManager? unitOfWork, Func<CancellationToken, Task<T>> work, Func<T, Task>? afterCommit = null)
    {
        T result;
        if (unitOfWork == null)
        {
            result = await work(CancellationToken.None);
        }
        else
        {
            unitOfWork.EnableTransaction();
            try
            {
                result = await work(CancellationToken.None);
                await unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        if (afterCommit != null)
            await afterCommit(result);
    }
}
