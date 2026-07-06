namespace Tnzi.Chat.Services;

public class GroupService : ApplicationService, IGroupService
{
    private readonly IRepository<Conversation, Guid> _conversationRepository;
    private readonly IRepository<ConversationMember, Guid> _memberRepository;
    private readonly IRepository<ChatMessage, Guid> _messageRepository;
    private readonly IConversationService _conversationService;
    private readonly IOptionsSnapshot<ChatOptions> _options;

    public GroupService(
        IServiceProvider serviceProvider,
        IRepository<Conversation, Guid> conversationRepository,
        IRepository<ConversationMember, Guid> memberRepository,
        IRepository<ChatMessage, Guid> messageRepository,
        IConversationService conversationService,
        IOptionsSnapshot<ChatOptions> options) : base(serviceProvider)
    {
        _conversationRepository = Check.NotNull(conversationRepository);
        _memberRepository = Check.NotNull(memberRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _conversationService = Check.NotNull(conversationService);
        _options = Check.NotNull(options);
    }

    // Sync — no await in body; using static avoids CS1998 warning.
    private static bool IsOwner(Conversation conv, Guid userId) => conv.OwnerId == userId;

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

        var me = GetRequiredCurrentUser().Id!.Value;

        var memberIds = (input.MemberIds ?? new List<Guid>()).Where(id => id != Guid.Empty && id != me).Distinct().ToList();
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
        // (me) is a member, so GetByIdAsync passes the membership check and reuses the mapper —
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
            foreach (var uid in toAdd)
            {
                if (existingActive.Contains(uid)) continue;
                var revoked = existing.FirstOrDefault(m => m.UserId == uid && m.RemovedAt != null);
                if (revoked != null)
                {
                    revoked.RemovedAt = null;
                    await _memberRepository.UpdateAsync(revoked, ct);
                }
                else
                {
                    await _memberRepository.InsertAsync(new ConversationMember { ConversationId = conversationId, UserId = uid, Role = MemberRole.Member }, ct);
                }
                added.Add(uid);
            }
            if (added.Count > 0)
            {
                conv.MemberCount += added.Count;
                await SystemMessageAsync(conversationId, $"{added.Count} member(s) added", now, ct);
                conv.LastMessageAt = now;
                conv.LastMessagePreview = "[Member added]";
                await _conversationRepository.UpdateAsync(conv, ct);
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
            member.RemovedAt = now;
            await _memberRepository.UpdateAsync(member, ct);
            conv.MemberCount = Math.Max(0, conv.MemberCount - 1);
            await SystemMessageAsync(conversationId, "A member was removed", now, ct);
            conv.LastMessageAt = now;
            conv.LastMessagePreview = "[Member removed]";
            await _conversationRepository.UpdateAsync(conv, ct);
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

        conv.Title = title.Trim();
        await _conversationRepository.UpdateAsync(conv);
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

        conv.Notice = string.IsNullOrWhiteSpace(notice) ? null : notice.Trim();
        await _conversationRepository.UpdateAsync(conv);
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
            member.RemovedAt = now;
            await _memberRepository.UpdateAsync(member, ct);
            conv.MemberCount = Math.Max(0, conv.MemberCount - 1);
            await SystemMessageAsync(conversationId, "A member left", now, ct);
            conv.LastMessageAt = now;
            conv.LastMessagePreview = "[Member left]";
            await _conversationRepository.UpdateAsync(conv, ct);
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
