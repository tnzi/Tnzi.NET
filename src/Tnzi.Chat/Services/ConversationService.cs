using Tnzi.Chat.Services.Internal;

namespace Tnzi.Chat.Services;

public class ConversationService : ApplicationService, IConversationService
{
    private readonly IRepository<Conversation, Guid> _conversationRepository;
    private readonly IRepository<ConversationMember, Guid> _memberRepository;
    private readonly IRepository<ChatMessage, Guid> _messageRepository;
    private readonly IRepository<MessageBlock, Guid> _messageBlockRepository;
    private readonly IChatContactService _contactService;
    private readonly IPresenceService _presence;
    private readonly IOptionsSnapshot<ChatOptions> _options;
    private readonly IChatAccessService _access;

    public ConversationService(
        IServiceProvider serviceProvider,
        IRepository<Conversation, Guid> conversationRepository,
        IRepository<ConversationMember, Guid> memberRepository,
        IRepository<ChatMessage, Guid> messageRepository,
        IRepository<MessageBlock, Guid> messageBlockRepository,
        IChatContactService contactService,
        IPresenceService presence,
        IOptionsSnapshot<ChatOptions> options,
        IChatAccessService access) : base(serviceProvider)
    {
        _conversationRepository = Check.NotNull(conversationRepository);
        _memberRepository = Check.NotNull(memberRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _messageBlockRepository = Check.NotNull(messageBlockRepository);
        _contactService = Check.NotNull(contactService);
        _presence = Check.NotNull(presence);
        _options = Check.NotNull(options);
        _access = Check.NotNull(access);
    }

    internal static string DirectKeyFor(Guid a, Guid b) =>
        a.CompareTo(b) <= 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";

    private async Task<ConversationMember?> GetActiveMemberAsync(Guid conversationId, Guid userId)
        => await _memberRepository.FirstOrDefaultAsync(
            m => m.ConversationId == conversationId && m.UserId == userId && m.RemovedAt == null);

    public async Task<Result<ConversationDto>> GetOrCreateDirectAsync(Guid otherUserId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        if (otherUserId == Guid.Empty || otherUserId == me)
            return Fail<ConversationDto>("Invalid target user.", 400);

        var key = DirectKeyFor(me, otherUserId);

        var existing = await _conversationRepository.FirstOrDefaultAsync(c => c.DirectKey == key);
        if (existing != null)
        {
            // Re-initiating a chat re-surfaces a conversation I previously hid/deleted
            // (my list entry only; the ClearedAt watermark still hides old history).
            var mine = await _memberRepository.AsQueryable(withTracking: true)
                .FirstOrDefaultAsync(m => m.ConversationId == existing.Id && m.UserId == me && m.RemovedAt == null && m.IsHidden);
            if (mine != null)
            {
                mine.IsHidden = false;
                await _memberRepository.UpdateAsync(mine);
            }
            return Ok(await MapConversationAsync(existing));
        }

        try
        {
            var conv = await ExecuteInUnitOfWorkAsync(async ct =>
            {
                var c = new Conversation
                {
                    Type = ConversationType.Direct,
                    DirectKey = key,
                    MemberCount = 2
                };
                await _conversationRepository.InsertAsync(c, ct);
                await _memberRepository.InsertManyAsync(new[]
                {
                    new ConversationMember { ConversationId = c.Id, UserId = me, Role = MemberRole.Member },
                    new ConversationMember { ConversationId = c.Id, UserId = otherUserId, Role = MemberRole.Member }
                }, ct);
                return c;
            });
            return Ok(await MapConversationAsync(conv));
        }
        catch (DbUpdateException)
        {
            var raced = await _conversationRepository.FirstOrDefaultAsync(c => c.DirectKey == key);
            if (raced != null) return Ok(await MapConversationAsync(raced));
            throw;
        }
    }

    public async Task<Result<ChatMessageDto>> SendMessageAsync(Guid conversationId, SendMessageDto input)
    {
        Check.NotNull(input);
        var me = GetRequiredCurrentUser().Id!.Value;

        var member = await GetActiveMemberAsync(conversationId, me);
        if (member == null)
            return Fail<ChatMessageDto>("You are not a member of this conversation.", 403);

        if (input.ContentType == MessageContentType.Text && string.IsNullOrWhiteSpace(input.Content))
            return Fail<ChatMessageDto>("Message content is required.", 400);
        if (input.ContentType == MessageContentType.Image || input.ContentType == MessageContentType.File)
        {
            // Deployment-level feature gate; the frontend hides the attachment entry
            // when disabled, but the write path must be enforced here regardless.
            if (!_options.Value.EnableFileMessages)
                return Fail<ChatMessageDto>("File and image messages are disabled.", 403);
            if (string.IsNullOrWhiteSpace(input.FileId))
                return Fail<ChatMessageDto>("File reference is required for media messages.", 400);
        }

        var conv = await _conversationRepository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conv == null)
            return Fail<ChatMessageDto>("Conversation not found.", 404);

        // Active recipients (excluding me).
        var others = await _memberRepository.AsQueryable(withTracking: true)
            .Where(m => m.ConversationId == conversationId && m.UserId != me && m.RemovedAt == null)
            .ToListAsync();
        var otherIds = others.Select(o => o.UserId).ToList();

        // Recipients who currently can't use chat (no chat.use) — isolated from this message.
        var disabled = await _access.FilterDisabledAsync(otherIds);

        // Direct chat whose sole recipient is disabled: intercept. Persist nothing and
        // surface a message so the sender's UI can explain the delivery failure. Because
        // the message never lands, the recipient can never see it — even after re-enable.
        if (conv.Type == ConversationType.Direct && otherIds.Count > 0 && otherIds.All(disabled.Contains))
            return Fail<ChatMessageDto>("This user is currently unavailable and can't receive messages.", 403);

        var now = DateTime.UtcNow;
        var preview = ChatPreview.Build(input.ContentType, input.Content);
        // Only deliverable (non-disabled) recipients get unread bumps + realtime pushes.
        var deliveredIds = otherIds.Where(id => !disabled.Contains(id)).ToList();

        var message = await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var msg = new ChatMessage
            {
                ConversationId = conversationId,
                SenderId = me,
                SentAt = now,
                ContentType = input.ContentType,
                Content = input.Content ?? string.Empty,
                FileId = input.FileId,
                FileName = input.FileName,
                FileSize = input.FileSize
            };
            await _messageRepository.InsertAsync(msg, ct);

            conv.LastMessageAt = now;
            conv.LastMessagePreview = preview;
            await _conversationRepository.UpdateAsync(conv, ct);

            // Deliver to able recipients: bump unread + re-surface a hidden conversation.
            var deliveredMembers = others.Where(o => !disabled.Contains(o.UserId)).ToList();
            foreach (var o in deliveredMembers)
            {
                o.UnreadCount += 1;
                o.IsHidden = false;
            }
            if (deliveredMembers.Count > 0) await _memberRepository.UpdateManyAsync(deliveredMembers, ct);

            // Isolation rows for disabled recipients: they will never see this message,
            // even after re-enable. FK filled from the nav property on SaveChanges (the
            // message Id is only generated at commit).
            if (disabled.Count > 0)
            {
                var blocks = otherIds.Where(disabled.Contains)
                    .Select(id => new MessageBlock { Message = msg, UserId = id })
                    .ToList();
                await _messageBlockRepository.InsertManyAsync(blocks, ct);
            }

            // Sending into a conversation I previously hid re-surfaces it for me too.
            if (member.IsHidden)
            {
                var mine = await _memberRepository.AsQueryable(withTracking: true)
                    .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == me && m.RemovedAt == null, ct);
                if (mine != null)
                {
                    mine.IsHidden = false;
                    await _memberRepository.UpdateAsync(mine, ct);
                }
            }

            return (ChatMessage?)msg;
        });

        if (message == null)
            return Fail<ChatMessageDto>("Conversation not found.", 404);

        var dto = message.MapTo<ChatMessageDto>();
        var senderProfiles = await _contactService.ResolveProfilesAsync(new[] { me });
        if (senderProfiles.TryGetValue(me, out var senderProfile))
        {
            dto.SenderName = senderProfile.Name;
            dto.SenderAvatarFileId = senderProfile.AvatarFileId;
        }

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new ConversationMessageSentEvent
            {
                ConversationId = conversationId,
                MessageId = message.Id,
                SenderId = me,
                ContentType = message.ContentType,
                Preview = preview,
                RecipientUserIds = deliveredIds,
                Message = dto
            });
        }

        return Ok(dto, "Message sent");
    }

    public async Task<Result<MessageThreadDto>> GetMessagesAsync(Guid conversationId, MessageThreadQueryDto query)
    {
        Check.NotNull(query);
        var me = GetRequiredCurrentUser().Id!.Value;
        var member = await GetActiveMemberAsync(conversationId, me);
        if (member == null)
            return Fail<MessageThreadDto>("You are not a member of this conversation.", 403);

        var limit = query.Limit <= 0 || query.Limit > 100 ? 30 : query.Limit;

        // NOTE: cursor filters strictly on SentAt. Messages sharing the EXACT same SentAt millisecond
        // as the cursor may be skipped across a page boundary — rare at human message rates.
        // A monotonic sequence column would fix this fully; deferred to a later iteration.
        DateTime? beforeAt = null;
        if (query.Before.HasValue)
        {
            var cursor = await _messageRepository.FindAsync(query.Before.Value);
            beforeAt = cursor?.SentAt;
        }

        // Messages isolated from me (received while I couldn't use chat) stay hidden even
        // after I'm re-enabled. Correlated NOT EXISTS (column-to-column) rather than a
        // materialized `Contains`, which mis-translates for Guid collections on SQLite.
        var myBlocks = _messageBlockRepository.AsQueryable().Where(b => b.UserId == me);

        var q = _messageRepository.AsQueryable()
            .Where(m => m.ConversationId == conversationId);
        if (member.ClearedAt.HasValue)
            q = q.Where(m => m.SentAt > member.ClearedAt.Value);
        q = q.Where(m => !myBlocks.Any(b => b.MessageId == m.Id));
        if (beforeAt.HasValue)
            q = q.Where(m => m.SentAt < beforeAt.Value);

        var page = await q.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(limit + 1).ToListAsync();

        var hasMore = page.Count > limit;
        var slice = page.Take(limit).OrderBy(m => m.SentAt).ThenBy(m => m.Id).ToList();

        var senderIds = slice.Where(m => m.SenderId.HasValue).Select(m => m.SenderId!.Value).Distinct().ToList();
        var profiles = await _contactService.ResolveProfilesAsync(senderIds);
        var dtos = slice.Select(m =>
        {
            var dto = m.MapTo<ChatMessageDto>();
            if (m.SenderId.HasValue && profiles.TryGetValue(m.SenderId.Value, out var p))
            {
                dto.SenderName = p.Name;
                dto.SenderAvatarFileId = p.AvatarFileId;
            }
            return dto;
        }).ToList();

        return Ok(new MessageThreadDto { Messages = dtos, HasMore = hasMore });
    }

    /// <summary>成员展示顺序：群主恒第一，其余按入群顺序（CreationTime，再按 Id 定序）。</summary>
    private static List<ConversationMember> OrderMembers(IEnumerable<ConversationMember> members, Guid? ownerId)
        => members
            .OrderByDescending(m => ownerId.HasValue && m.UserId == ownerId.Value)
            .ThenBy(m => m.CreationTime)
            .ThenBy(m => m.Id)
            .ToList();

    internal async Task<ConversationDto> MapConversationAsync(Conversation conv)
    {
        var me = CurrentUser?.Id;
        var members = OrderMembers(
            await _memberRepository.ToListAsync(m => m.ConversationId == conv.Id && m.RemovedAt == null),
            conv.OwnerId);
        var ids = members.Select(m => m.UserId).ToList();
        var profiles = await _contactService.ResolveProfilesAsync(ids);
        var presenceMap = (await _presence.ResolveEffectiveAsync(ids)).ToDictionary(p => p.UserId);
        var mine = me.HasValue ? members.FirstOrDefault(m => m.UserId == me.Value) : null;

        var dto = new ConversationDto
        {
            Id = conv.Id,
            Type = conv.Type,
            Title = conv.Title ?? string.Empty,
            AvatarFileId = conv.AvatarFileId,
            OwnerId = conv.OwnerId,
            MemberCount = conv.MemberCount,
            LastMessageAt = conv.LastMessageAt,
            Notice = conv.Notice,
            IsSticky = mine?.IsSticky ?? false,
            IsMuted = mine?.IsMuted ?? false,
            MyRemark = mine?.Remark,
            MyAlias = mine?.Alias,
            Members = members.Select(m =>
            {
                profiles.TryGetValue(m.UserId, out var p);
                presenceMap.TryGetValue(m.UserId, out var pr);
                return new ConversationMemberDto
                {
                    UserId = m.UserId,
                    Name = p?.Name ?? string.Empty,
                    AvatarFileId = p?.AvatarFileId,
                    Role = m.Role,
                    Alias = m.Alias,
                    Status = pr?.Status,
                    LastSeenAt = pr?.LastSeenAt
                };
            }).ToList()
        };
        return dto;
    }

    public async Task<Result<IReadOnlyList<ConversationListItemDto>>> GetMyConversationsAsync()
    {
        var me = GetRequiredCurrentUser().Id!.Value;

        // Hidden conversations are excluded; any incoming message flips IsHidden
        // back to false server-side, so they re-surface automatically.
        var members = await _memberRepository.ToListAsync(m => m.UserId == me && m.RemovedAt == null && !m.IsHidden);
        if (members.Count == 0)
            return Ok<IReadOnlyList<ConversationListItemDto>>(new List<ConversationListItemDto>());

        var convIds = members.Select(m => m.ConversationId).ToHashSet();
        var conversations = await _conversationRepository.ToListAsync(c => convIds.Contains(c.Id));
        var convById = conversations.ToDictionary(c => c.Id);

        // Batch-load every active member of these conversations once, then resolve the
        // Direct other-party in memory. This avoids N+1 member queries on the conversation
        // list — a hot path called whenever a user opens the chat window.
        var allMembers = await _memberRepository.ToListAsync(m => convIds.Contains(m.ConversationId) && m.RemovedAt == null);
        var otherIdsByConv = allMembers
            .Where(m => m.UserId != me)
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.UserId).ToList());

        var directOtherById = new Dictionary<Guid, Guid>();
        foreach (var conv in conversations.Where(c => c.Type == ConversationType.Direct))
        {
            if (otherIdsByConv.TryGetValue(conv.Id, out var ids) && ids.Count > 0)
                directOtherById[conv.Id] = ids[0];
        }

        // Group composite avatars: owner always first, then the earliest joined members
        // (join order). Computed from the already-loaded member batch — no extra query.
        var avatarTake = Math.Clamp(_options.Value.GroupAvatarMemberCount, 1, 9);
        var ownerByConv = conversations
            .Where(c => c.Type == ConversationType.Group)
            .ToDictionary(c => c.Id, c => c.OwnerId);
        var groupAvatarIdsByConv = allMembers
            .Where(m => ownerByConv.ContainsKey(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .ToDictionary(
                g => g.Key,
                g => OrderMembers(g, ownerByConv.GetValueOrDefault(g.Key))
                    .Take(avatarTake).Select(m => m.UserId).ToList());

        var profileIds = directOtherById.Values
            .Concat(groupAvatarIdsByConv.Values.SelectMany(ids => ids))
            .Distinct().ToList();
        var profiles = await _contactService.ResolveProfilesAsync(profileIds);

        var items = new List<ConversationListItemDto>();
        foreach (var member in members)
        {
            if (!convById.TryGetValue(member.ConversationId, out var conv)) continue;

            string title;
            string? avatar = conv.AvatarFileId;
            if (conv.Type == ConversationType.System)
            {
                title = "System Notifications";
            }
            else if (conv.Type == ConversationType.Group)
            {
                title = conv.Title ?? "Group";
            }
            else
            {
                var otherId = directOtherById.TryGetValue(conv.Id, out var oid) ? oid : Guid.Empty;
                title = profiles.TryGetValue(otherId, out var p) ? p.Name : string.Empty;
                avatar = profiles.TryGetValue(otherId, out var p2) ? p2.AvatarFileId : null;
            }

            List<ChatContactDto>? memberAvatars = null;
            if (conv.Type == ConversationType.Group && groupAvatarIdsByConv.TryGetValue(conv.Id, out var avatarIds))
            {
                memberAvatars = avatarIds.Select(uid => profiles.TryGetValue(uid, out var mp)
                    ? mp
                    : new ChatContactDto { UserId = uid }).ToList();
            }

            items.Add(new ConversationListItemDto
            {
                Id = conv.Id,
                Type = conv.Type,
                Title = title,
                AvatarFileId = avatar,
                LastMessagePreview = conv.LastMessagePreview,
                LastMessageAt = conv.LastMessageAt,
                UnreadCount = member.UnreadCount,
                IsMuted = member.IsMuted,
                MemberCount = conv.MemberCount,
                IsSticky = member.IsSticky,
                Remark = member.Remark,
                PeerUserId = conv.Type == ConversationType.Direct && directOtherById.TryGetValue(conv.Id, out var pid) ? pid : null,
                MemberAvatars = memberAvatars,
            });
        }

        // 富化 Direct peer 在线状态 + chat.use 禁用标识
        var peerIds = items.Where(i => i.PeerUserId.HasValue).Select(i => i.PeerUserId!.Value).Distinct().ToList();
        if (peerIds.Count > 0)
        {
            var presenceMap = (await _presence.ResolveEffectiveAsync(peerIds)).ToDictionary(p => p.UserId);
            // A peer who lost chat.use gets a distinct "unavailable" marker in the list
            // (the conversation stays, but they can no longer take part). Empty when the
            // gate is inactive → no one flagged.
            var disabledPeers = await _access.FilterDisabledAsync(peerIds);
            foreach (var i in items.Where(i => i.PeerUserId.HasValue))
            {
                if (presenceMap.TryGetValue(i.PeerUserId!.Value, out var pr)) i.PeerStatus = pr.Status;
                i.PeerDisabled = disabledPeers.Contains(i.PeerUserId!.Value);
            }
        }

        var ordered = items
            .OrderByDescending(i => i.IsSticky)
            .ThenByDescending(i => i.LastMessageAt ?? DateTime.MinValue)
            .ToList();
        return Ok<IReadOnlyList<ConversationListItemDto>>(ordered);
    }

    public async Task<Result<ConversationDto>> GetByIdAsync(Guid conversationId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        if (await GetActiveMemberAsync(conversationId, me) == null)
            return Fail<ConversationDto>("You are not a member of this conversation.", 403);

        var conv = await _conversationRepository.FindAsync(conversationId);
        if (conv == null) return Fail<ConversationDto>("Conversation not found.", 404);
        return Ok(await MapConversationAsync(conv));
    }

    public async Task<Result<int>> GetTotalUnreadAsync()
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        // Hidden conversations don't contribute to the badge (they aren't in the
        // list, so a phantom count would be unactionable).
        var members = await _memberRepository.ToListAsync(m => m.UserId == me && m.RemovedAt == null && !m.IsHidden);
        return Ok(members.Sum(m => m.UnreadCount));
    }

    public async Task<Result> MarkReadAsync(Guid conversationId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        // Use tracking load to avoid EF "already tracked" conflict when UpdateAsync is called.
        var member = await _memberRepository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == me && m.RemovedAt == null);
        if (member == null) return Fail("You are not a member of this conversation.", 403);

        // Nothing to do: already read with no unread → skip the write + read-receipt broadcast.
        // (The frontend re-marks-read on every open of an already-read active conversation.)
        if (member.UnreadCount == 0 && member.LastReadAt != null)
            return Ok();

        var now = DateTime.UtcNow;
        var otherMemberIds = new List<Guid>();
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            member.LastReadAt = now;
            member.UnreadCount = 0;
            await _memberRepository.UpdateAsync(member, ct);

            var others = await _memberRepository.ToListAsync(
                m => m.ConversationId == conversationId && m.UserId != me && m.RemovedAt == null, ct);
            otherMemberIds.AddRange(others.Select(o => o.UserId));
        });

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new ConversationReadEvent
            {
                ConversationId = conversationId,
                UserId = me,
                ReadAt = now,
                OtherMemberIds = otherMemberIds
            });
        }
        return Ok();
    }

    public Task<Result> MuteAsync(Guid conversationId, bool muted)
        => UpdateMemberSettingsAsync(conversationId, new ConversationMemberSettingsDto { IsMuted = muted });

    public async Task<Result> UpdateMemberSettingsAsync(Guid conversationId, ConversationMemberSettingsDto settings)
    {
        Check.NotNull(settings);
        var me = GetRequiredCurrentUser().Id!.Value;
        var member = await _memberRepository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == me && m.RemovedAt == null);
        if (member == null) return Fail("You are not a member of this conversation.", 403);

        // Validate length before persist (config caps both at 100) so an over-length value
        // returns a clean 400 instead of surfacing as a DB error.
        if (settings.Remark != null && settings.Remark.Length > 100)
            return Fail("Remark too long (max 100).", 400);
        if (settings.Alias != null && settings.Alias.Length > 100)
            return Fail("Alias too long (max 100).", 400);

        if (settings.IsMuted.HasValue) member.IsMuted = settings.IsMuted.Value;
        if (settings.IsSticky.HasValue) member.IsSticky = settings.IsSticky.Value;
        if (settings.IsHidden.HasValue) member.IsHidden = settings.IsHidden.Value;
        if (settings.Remark != null) member.Remark = settings.Remark.Length == 0 ? null : settings.Remark;
        if (settings.Alias != null) member.Alias = settings.Alias.Length == 0 ? null : settings.Alias;

        await _memberRepository.UpdateAsync(member);
        return Ok();
    }

    public async Task<Result> DeleteForMeAsync(Guid conversationId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var member = await _memberRepository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == me && m.RemovedAt == null);
        if (member == null) return Fail("You are not a member of this conversation.", 403);

        // Per-user delete: wipe MY view of the history (ClearedAt watermark) and
        // drop the conversation from MY list. Other members keep everything. A
        // future message re-surfaces the conversation (IsHidden flips back) with
        // an empty history - the WeChat "delete chat" semantic. Shared rows are
        // never hard-deleted: groups/direct peers own the same data.
        var now = DateTime.UtcNow;
        member.IsHidden = true;
        member.ClearedAt = now;
        member.LastReadAt = now;
        member.UnreadCount = 0;
        await _memberRepository.UpdateAsync(member);
        return Ok();
    }

    public async Task<Result> ClearHistoryAsync(Guid conversationId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var member = await _memberRepository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.UserId == me && m.RemovedAt == null);
        if (member == null) return Fail("You are not a member of this conversation.", 403);

        member.ClearedAt = DateTime.UtcNow;
        await _memberRepository.UpdateAsync(member);
        return Ok();
    }

    public async Task<Result<MessageThreadDto>> SearchMessagesAsync(Guid conversationId, string keyword, MessageThreadQueryDto query)
    {
        Check.NotNullOrWhiteSpace(keyword, nameof(keyword));
        Check.NotNull(query);
        var me = GetRequiredCurrentUser().Id!.Value;
        var member = await GetActiveMemberAsync(conversationId, me);
        if (member == null)
            return Fail<MessageThreadDto>("You are not a member of this conversation.", 403);

        var limit = query.Limit <= 0 || query.Limit > 100 ? 30 : query.Limit;
        var kw = keyword.Trim().ToLower();

        DateTime? beforeAt = null;
        if (query.Before.HasValue)
            beforeAt = (await _messageRepository.FindAsync(query.Before.Value))?.SentAt;

        var myBlocks = _messageBlockRepository.AsQueryable().Where(b => b.UserId == me);

        var q = _messageRepository.AsQueryable()
            .Where(m => m.ConversationId == conversationId
                        && m.ContentType == MessageContentType.Text
                        && m.Content.ToLower().Contains(kw));
        if (member.ClearedAt.HasValue) q = q.Where(m => m.SentAt > member.ClearedAt.Value);
        q = q.Where(m => !myBlocks.Any(b => b.MessageId == m.Id));
        if (beforeAt.HasValue) q = q.Where(m => m.SentAt < beforeAt.Value);

        var page = await q.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(limit + 1).ToListAsync();
        var hasMore = page.Count > limit;
        var slice = page.Take(limit).OrderBy(m => m.SentAt).ThenBy(m => m.Id).ToList();

        var senderIds = slice.Where(m => m.SenderId.HasValue).Select(m => m.SenderId!.Value).Distinct().ToList();
        var profiles = await _contactService.ResolveProfilesAsync(senderIds);
        var dtos = slice.Select(m =>
        {
            var dto = m.MapTo<ChatMessageDto>();
            if (m.SenderId.HasValue && profiles.TryGetValue(m.SenderId.Value, out var p)) dto.SenderName = p.Name;
            return dto;
        }).ToList();
        return Ok(new MessageThreadDto { Messages = dtos, HasMore = hasMore });
    }

    public async Task<Result> DeleteMessageAsync(Guid messageId)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var msg = await _messageRepository.FindAsync(messageId);
        if (msg == null) return Fail("Message not found.", 404);
        if (msg.SenderId != me) return Fail("You can only delete your own messages.", 403);

        await _messageRepository.DeleteAsync(msg); // 软删（FullAudited）
        return Ok();
    }
}
