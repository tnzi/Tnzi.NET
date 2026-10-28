namespace Tnzi.Chat.Services;

public class GroupService : ApplicationService, IGroupService
{
    private readonly IRepository<Conversation, Guid> _conversationRepository;
    private readonly IRepository<ConversationMember, Guid> _memberRepository;
    private readonly IRepository<ChatMessage, Guid> _messageRepository;
    private readonly IRepository<User, Guid> _userRepository;
    private readonly IConversationService _conversationService;
    private readonly IOptionsSnapshot<ChatOptions> _options;
    private readonly IFunctionAuthorizationService? _functionAuthorization;
    private readonly IChatAccessService? _chatAccess;
    private readonly IUserTenantScopeProvider _userScope;

    public GroupService(
        IServiceProvider serviceProvider,
        IRepository<Conversation, Guid> conversationRepository,
        IRepository<ConversationMember, Guid> memberRepository,
        IRepository<ChatMessage, Guid> messageRepository,
        IRepository<User, Guid> userRepository,
        IConversationService conversationService,
        IOptionsSnapshot<ChatOptions> options,
        IUserTenantScopeProvider userScope,
        IFunctionAuthorizationService? functionAuthorization = null,
        IChatAccessService? chatAccess = null) : base(serviceProvider)
    {
        _conversationRepository = Check.NotNull(conversationRepository);
        _memberRepository = Check.NotNull(memberRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _userRepository = Check.NotNull(userRepository);
        _conversationService = Check.NotNull(conversationService);
        _options = Check.NotNull(options);
        _userScope = Check.NotNull(userScope);
        _functionAuthorization = functionAuthorization;
        _chatAccess = chatAccess;
    }

    /// <summary>
    /// Drop ids that are not existing accounts (unknown or soft-deleted). Silent, like the
    /// rules below. Needed on its own: the tenant scope is unrestricted in a single-tenant
    /// deployment and the <c>chat.use</c> gate is inactive without Authorization, and both
    /// then pass any id through, so an unknown id became a member row counted in
    /// <c>MemberCount</c>.
    /// </summary>
    private async Task<List<Guid>> ExcludeNonexistentAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return ids;
        var existing = (await _userRepository.GetListAsync(ids)).Select(u => u.Id).ToHashSet();
        return existing.Count == ids.Count ? ids : ids.Where(existing.Contains).ToList();
    }

    /// <summary>
    /// Drop ids outside the caller's tenant (and ids that don't exist). <c>User</c> is not
    /// <c>IMultiTenant</c>, so nothing else stops a caller from pulling another tenant's
    /// account into a group by passing its id. Silent, like the two rules below: refusing
    /// would confirm that the id exists. No-op when multi-tenancy is off or there is no
    /// tenant context (<see cref="IUserTenantScopeProvider"/> is then unrestricted).
    /// </summary>
    private async Task<List<Guid>> ExcludeOutOfScopeAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return ids;
        var inScope = (await _userScope.FilterAsync(ids)).ToHashSet();
        return ids.Where(inScope.Contains).ToList();
    }

    // Sync - no await in body; using static avoids CS1998 warning.
    private static bool IsOwner(Conversation conv, Guid userId) => conv.OwnerId == userId;

    /// <summary>
    /// Drop super-admin ids from a member list. The contact directory already hides
    /// super admins, but the group-member write paths take caller-supplied ids and
    /// must enforce the same rule so nobody can pull a maintenance account into a
    /// business group by passing its id directly. Silent (not 403): one stray id
    /// shouldn't fail the whole operation, and refusing would disclose that the id
    /// belongs to a super admin. No-op when Authorization isn't loaded.
    /// </summary>
    private async Task<List<Guid>> ExcludeSuperAdminsAsync(List<Guid> ids)
    {
        if (_functionAuthorization == null || ids.Count == 0) return ids;
        var supers = await _functionAuthorization.GetSuperAdminUserIdsAsync();
        return supers.Count == 0 ? ids : ids.Where(id => !supers.Contains(id)).ToList();
    }

    /// <summary>
    /// Drop ids that lack <c>chat.use</c>. The picker already hides them, but the
    /// member write paths take caller-supplied ids, so a disabled user could be
    /// pulled into a group by passing its id directly - where every message to them
    /// would just be isolated. Silent (not 403), mirroring the super-admin rule.
    /// No-op when the gate is inactive (Authorization not loaded).
    /// </summary>
    private async Task<List<Guid>> ExcludeDisabledAsync(List<Guid> ids)
    {
        if (_chatAccess == null || ids.Count == 0) return ids;
        var disabled = await _chatAccess.FilterDisabledAsync(ids);
        return disabled.Count == 0 ? ids : ids.Where(id => !disabled.Contains(id)).ToList();
    }

    /// <summary>
    /// 在数据库端把一名成员标记为已移出；返回是否真的移出了（并发的另一次移出 / 退群已经做过时为 false）。
    /// </summary>
    /// <remarks>
    /// 条件里重查 <c>RemovedAt == null</c>：踢人与本人退群同时到达时只有一方生效，成员数只减一次。
    /// 不整行写回读出的快照：那会把这段时间里成员自己改的已读、设置一并撤销。
    /// </remarks>
    private async Task<bool> MarkRemovedAsync(Guid memberId, DateTime now, CancellationToken ct)
    {
        await _memberRepository.EnsureTransactionStartedAsync(ct);
        var affected = await _memberRepository.AsQueryable()
            .Where(m => m.Id == memberId && m.RemovedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RemovedAt, now), ct);
        return affected > 0;
    }

    /// <summary>
    /// 在数据库端调整成员数并刷新会话的最后一条消息。
    /// </summary>
    /// <remarks>
    /// ★ 成员数是计数器：读出来减一再整行写回，并发的加人 / 踢人 / 退群会互相覆盖（丢一次增减），
    /// 整行写回还会把并发的改名、解散一并撤销。这里只改这几列，增减在数据库里算。
    /// </remarks>
    private async Task TouchConversationAsync(Guid conversationId, int memberDelta, string preview, DateTime now, CancellationToken ct)
    {
        await _conversationRepository.EnsureTransactionStartedAsync(ct);
        var modifierId = CurrentUser?.Id;
        await _conversationRepository.AsQueryable()
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.MemberCount, c => c.MemberCount + memberDelta < 0 ? 0 : c.MemberCount + memberDelta)
                .SetProperty(c => c.LastMessageAt, now)
                .SetProperty(c => c.LastMessagePreview, preview)
                .SetProperty(c => c.LastModificationTime, now)
                .SetProperty(c => c.LastModifierId, modifierId), ct);
    }

    private async Task SystemMessageAsync(Guid conversationId, string text, DateTime now, CancellationToken ct)
    {
        await _messageRepository.InsertAsync(new ChatMessage
        {
            ConversationId = conversationId,
            SenderId = null,
            SentAt = now,
            ContentType = MessageContentType.System,
            Content = text
        }, ct);
    }

    public async Task<Result<ConversationDto>> CreateGroupAsync(CreateGroupDto input)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.Title, nameof(input.Title));

        // Deployment-level feature gate: the frontend hides the entry when disabled,
        // but the write path must be enforced here regardless.
        var opts = _options.Value;
        if (!opts.EnableGroups)
            return Fail<ConversationDto>("Group chat is disabled.", 403);

        // ★ 越界的群名在 SQL Server / PostgreSQL 上是一次 DbUpdateException（给用户 500）。
        // 判定落在**修剪后**的值上 —— 那才是要落库的那一个。
        var titleTooLong = ChatFieldLimits.Exceeded(input.Title.Trim(), ChatFieldLimits.Title, "Group name");
        if (titleTooLong != null)
            return Fail<ConversationDto>(titleTooLong, 400);

        var me = GetRequiredCurrentUser().Id!.Value;

        var memberIds = (input.MemberIds ?? new List<Guid>()).Where(id => id != Guid.Empty && id != me).Distinct().ToList();
        // Never seed a business group with an unknown id, another tenant's account, a super admin,
        // or a user who can't use chat (no `chat.use`) - all dropped silently even if passed directly.
        memberIds = await ExcludeNonexistentAsync(memberIds);
        memberIds = await ExcludeOutOfScopeAsync(memberIds);
        memberIds = await ExcludeSuperAdminsAsync(memberIds);
        memberIds = await ExcludeDisabledAsync(memberIds);
        if (opts.MaxGroupMembers > 0 && memberIds.Count + 1 > opts.MaxGroupMembers)
            return Fail<ConversationDto>($"Group size exceeds the maximum of {opts.MaxGroupMembers} members.", 400);

        var now = DateTime.UtcNow;

        var conv = await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var c = new Conversation
            {
                Type = ConversationType.Group,
                Title = input.Title.Trim(),
                OwnerId = me,
                MemberCount = memberIds.Count + 1,
                LastMessageAt = now,
                LastMessagePreview = "[Group created]"
            };
            await _conversationRepository.InsertAsync(c, ct);

            var members = new List<ConversationMember>
            {
                new() { ConversationId = c.Id, UserId = me, Role = MemberRole.Owner }
            };
            members.AddRange(memberIds.Select(id => new ConversationMember { ConversationId = c.Id, UserId = id, Role = MemberRole.Member }));
            await _memberRepository.InsertManyAsync(members, ct);

            await SystemMessageAsync(c.Id, "Group created", now, ct);
            return c;
        });

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new ConversationChangedEvent
            {
                ConversationId = conv.Id,
                ChangeType = ConversationChangeType.Created,
                AffectedUserIds = memberIds.Append(me).ToList()
            });
        }

        // Return a fully-enriched DTO consistent with ConversationService.MapConversationAsync
        // (Notice/IsSticky/IsMuted/MyRemark/MyAlias + member Status/LastSeenAt/Alias). The owner
        // (me) is a member, so GetByIdAsync passes the membership check and reuses the mapper -
        // avoids duplicating the enrichment logic.
        var enriched = await _conversationService.GetByIdAsync(conv.Id);
        return enriched.Succeeded ? Ok(enriched.Data!) : enriched;
    }

    public async Task<Result> AddMembersAsync(Guid conversationId, IEnumerable<Guid> userIds)
    {
        var opts = _options.Value;
        if (!opts.EnableGroups)
            return Fail("Group chat is disabled.", 403);

        var me = GetRequiredCurrentUser().Id!.Value;
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null || conv.Type != ConversationType.Group) return Fail("Group not found.", 404);
        if (!IsOwner(conv, me)) return Fail("Only the group owner can add members.", 403);

        var toAdd = (userIds ?? Enumerable.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
        // Same rules as CreateGroup: existing accounts of the same tenant only, no super admins,
        // and no users lacking `chat.use`.
        toAdd = await ExcludeNonexistentAsync(toAdd);
        toAdd = await ExcludeOutOfScopeAsync(toAdd);
        toAdd = await ExcludeSuperAdminsAsync(toAdd);
        toAdd = await ExcludeDisabledAsync(toAdd);
        if (toAdd.Count == 0) return Fail("No members to add.", 400);

        // Load existing members with tracking so UpdateAsync on revoked rows doesn't conflict.
        var existing = await _memberRepository.AsQueryable(withTracking: true)
            .Where(m => m.ConversationId == conversationId)
            .ToListAsync();
        var existingActive = existing.Where(m => m.RemovedAt == null).Select(m => m.UserId).ToHashSet();

        // Enforce the member cap against the actual number of NEW members (already-active
        // ids are skipped below and must not count toward the limit).
        var newCount = toAdd.Count(id => !existingActive.Contains(id));
        if (opts.MaxGroupMembers > 0 && conv.MemberCount + newCount > opts.MaxGroupMembers)
            return Fail($"Group size exceeds the maximum of {opts.MaxGroupMembers} members.", 400);

        var now = DateTime.UtcNow;
        var added = new List<Guid>();

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 裸 SQL（下面的 ExecuteUpdate）默认不进工作单元的事务，先开起来。
            await _memberRepository.EnsureTransactionStartedAsync(ct);

            foreach (var uid in toAdd)
            {
                if (existingActive.Contains(uid)) continue;
                var revoked = existing.FirstOrDefault(m => m.UserId == uid && m.RemovedAt != null);
                if (revoked != null)
                {
                    // 跟踪着的实体：只写 RemovedAt 这一列，不把这份快照的其它列整行写回去。
                    revoked.RemovedAt = null;
                }
                else
                {
                    await _memberRepository.InsertAsync(new ConversationMember { ConversationId = conversationId, UserId = uid, Role = MemberRole.Member }, ct);
                }
                added.Add(uid);
            }
            if (added.Count > 0)
            {
                await _memberRepository.SaveChangesAsync(ct);
                await SystemMessageAsync(conversationId, $"{added.Count} member(s) added", now, ct);
                await TouchConversationAsync(conversationId, added.Count, "[Member added]", now, ct);
            }
        });

        await PublishChangedAsync(conversationId, ConversationChangeType.MemberAdded);
        return Ok();
    }

    public async Task<Result> RemoveMemberAsync(Guid conversationId, Guid userId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null || conv.Type != ConversationType.Group) return Fail("Group not found.", 404);
        if (!IsOwner(conv, me)) return Fail("Only the group owner can remove members.", 403);
        if (userId == me) return Fail("Owner cannot remove themselves; dissolve the group instead.", 400);

        // Load with tracking so UpdateAsync doesn't conflict with EF change tracker.
        var member = await _memberRepository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == userId && m.RemovedAt == null);
        if (member == null) return Fail("Member not found.", 404);

        var now = DateTime.UtcNow;
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            if (await MarkRemovedAsync(member.Id, now, ct))
            {
                await SystemMessageAsync(conversationId, "A member was removed", now, ct);
                await TouchConversationAsync(conversationId, -1, "[Member removed]", now, ct);
            }
        });

        await PublishChangedAsync(conversationId, ConversationChangeType.MemberRemoved, extra: userId);
        return Ok();
    }

    public async Task<Result> RenameGroupAsync(Guid conversationId, string title)
    {
        Check.NotNullOrWhiteSpace(title, nameof(title));
        var me = GetRequiredCurrentUser().Id!.Value;
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null || conv.Type != ConversationType.Group) return Fail("Group not found.", 404);
        if (!IsOwner(conv, me)) return Fail("Only the group owner can rename the group.", 403);

        var trimmedTitle = title.Trim();
        var titleTooLong = ChatFieldLimits.Exceeded(trimmedTitle, ChatFieldLimits.Title, "Group name");
        if (titleTooLong != null) return Fail(titleTooLong, 400);

        conv.Title = trimmedTitle;
        // 跟踪着的实体（FindAsync）：只写 Title，不把读出时的 MemberCount / 解散标记整行写回去。
        await _conversationRepository.SaveChangesAsync();
        await PublishChangedAsync(conversationId, ConversationChangeType.Renamed);
        return Ok();
    }

    public async Task<Result> UpdateNoticeAsync(Guid conversationId, string? notice)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null || conv.Type != ConversationType.Group)
            return Fail("Group not found.", 404);
        if (!IsOwner(conv, me))
            return Fail("Only the group owner can edit the notice.", 403);

        var trimmedNotice = string.IsNullOrWhiteSpace(notice) ? null : notice.Trim();
        var noticeTooLong = ChatFieldLimits.Exceeded(trimmedNotice, ChatFieldLimits.Notice, "Group notice");
        if (noticeTooLong != null) return Fail(noticeTooLong, 400);

        conv.Notice = trimmedNotice;
        await _conversationRepository.SaveChangesAsync();
        return Ok();
    }

    public async Task<Result> DissolveGroupAsync(Guid conversationId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null || conv.Type != ConversationType.Group) return Fail("Group not found.", 404);
        if (!IsOwner(conv, me)) return Fail("Only the group owner can dissolve the group.", 403);

        var memberIds = (await _memberRepository.ToListAsync(m => m.ConversationId == conversationId && m.RemovedAt == null))
            .Select(m => m.UserId).ToList();

        await _conversationRepository.DeleteAsync(conv); // soft-delete (FullAudited)

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new ConversationChangedEvent
            {
                ConversationId = conversationId,
                ChangeType = ConversationChangeType.Dissolved,
                AffectedUserIds = memberIds
            });
        }
        return Ok();
    }

    public async Task<Result> LeaveAsync(Guid conversationId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null || conv.Type != ConversationType.Group) return Fail("Group not found.", 404);

        // Load with tracking so UpdateAsync doesn't conflict with EF change tracker.
        var member = await _memberRepository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == me && m.RemovedAt == null);
        if (member == null) return Fail("You are not a member of this group.", 403);
        if (conv.OwnerId == me) return Fail("Owner must dissolve the group instead of leaving.", 400);

        var now = DateTime.UtcNow;
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            if (await MarkRemovedAsync(member.Id, now, ct))
            {
                await SystemMessageAsync(conversationId, "A member left", now, ct);
                await TouchConversationAsync(conversationId, -1, "[Member left]", now, ct);
            }
        });

        await PublishChangedAsync(conversationId, ConversationChangeType.Left, extra: me);
        return Ok();
    }

    private async Task PublishChangedAsync(Guid conversationId, ConversationChangeType type, Guid? extra = null)
    {
        if (EventBus == null) return;
        var ids = (await _memberRepository.ToListAsync(m => m.ConversationId == conversationId && m.RemovedAt == null))
            .Select(m => m.UserId).ToList();
        if (extra.HasValue && !ids.Contains(extra.Value)) ids.Add(extra.Value);
        await EventBus.PublishAsync(new ConversationChangedEvent
        {
            ConversationId = conversationId,
            ChangeType = type,
            AffectedUserIds = ids
        });
    }
}
