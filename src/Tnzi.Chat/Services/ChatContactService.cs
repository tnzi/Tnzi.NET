namespace Tnzi.Chat.Services;

public class ChatContactService : ApplicationService, IChatContactService
{
    /// <summary>
    /// 候选窗倍数：SQL 侧取 <c>ContactSearchLimit × 本值</c> 个候选，再剔除没有 <c>chat.use</c> 的，
    /// 最后截到 <c>ContactSearchLimit</c>。窗比页大，是为了让页面在少数候选被剔掉后仍然填满；
    /// 窗有上界，是为了让一次搜索的代价与目录规模无关。
    /// </summary>
    internal const int CandidateWindowFactor = 3;

    private readonly IRepository<User, Guid> _userRepository;
    private readonly IRepository<UserDetail, Guid> _userDetailRepository;
    private readonly IPresenceService _presence;
    private readonly IOptionsSnapshot<ChatOptions> _options;
    private readonly IFunctionAuthorizationService? _functionAuthorization;
    private readonly IChatAccessService? _chatAccess;

    public ChatContactService(
        IServiceProvider serviceProvider,
        IRepository<User, Guid> userRepository,
        IRepository<UserDetail, Guid> userDetailRepository,
        IPresenceService presence,
        IOptionsSnapshot<ChatOptions> options,
        IFunctionAuthorizationService? functionAuthorization = null,
        IChatAccessService? chatAccess = null)
        : base(serviceProvider)
    {
        _userRepository = Check.NotNull(userRepository);
        _userDetailRepository = Check.NotNull(userDetailRepository);
        _presence = Check.NotNull(presence);
        _options = Check.NotNull(options);
        // Optional: only present when Authorization is loaded. Null → no super-admin
        // concept → the directory hides no one (see GetSuperAdminUserIdsAsync).
        _functionAuthorization = functionAuthorization;
        // Optional: gate contact results by `chat.use`. Null / no-gate → fail-open
        // (nobody hidden), so standalone Chat keeps working without Authorization.
        _chatAccess = chatAccess;
    }

    public async Task<Result<IReadOnlyList<ChatContactDto>>> SearchUsersAsync(string? keyword)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var kw = keyword?.Trim().ToLower();

        // Super admins are system-maintenance/operations accounts that don't take part in
        // business activity - they must never surface in the business-facing contact
        // directory. Strip them from the candidate set (empty when Authorization isn't
        // loaded, i.e. no super-admin concept → nothing hidden).
        var hiddenIds = _functionAuthorization == null
            ? new List<Guid>()
            : (await _functionAuthorization.GetSuperAdminUserIdsAsync()).ToList();

        // Blank keyword → first page of the directory (excluding self) so the picker can
        // show a starting contact list. A keyword narrows by username (case-insensitive).
        //
        // ★ Paging happens in SQL (ORDER BY + TAKE), never by materialising the whole
        // directory: the previous shape loaded every User row into memory and ran one
        // permission check per row before taking the first 20 - on a 100k-user
        // deployment a single "new chat" click was 100k entities plus 100k+ DB
        // round-trips, with no privilege required. The window is a bounded multiple of
        // the page so the chat.use filter below still has room to fill the page.
        var limit = Math.Max(1, _options.Value.ContactSearchLimit);
        var candidates = _userRepository.AsQueryable()
            .Where(u => u.Id != me && u.UserName != null && !hiddenIds.Contains(u.Id));
        if (!string.IsNullOrEmpty(kw))
            candidates = candidates.Where(u => u.UserName!.ToLower().Contains(kw));

        var users = await candidates
            .OrderBy(u => u.UserName)
            .Take(limit * CandidateWindowFactor)
            .ToListAsync();

        // Users without `chat.use` can't take part in chat (their inbound is blocked /
        // isolated), so they must not appear in the new-chat / add-member picker.
        // FilterDisabledAsync returns the subset lacking the grant (empty when the
        // gate is inactive → nothing hidden). Applied BEFORE Take so the page still
        // fills up to the limit with usable contacts; the check is one batch call over
        // the (bounded) candidate window.
        if (_chatAccess != null && users.Count > 0)
        {
            var disabled = await _chatAccess.FilterDisabledAsync(users.Select(u => u.Id));
            if (disabled.Count > 0)
                users = users.Where(u => !disabled.Contains(u.Id)).ToList();
        }

        var taken = users.Take(limit).ToList();
        var detailByUserId = await LoadDetailsAsync(taken.Select(u => u.Id).ToList());

        var list = taken.Select(u => ToContactDto(u, detailByUserId)).ToList();

        return Ok<IReadOnlyList<ChatContactDto>>(list);
    }

    public async Task<IReadOnlyDictionary<Guid, ChatContactDto>> ResolveProfilesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds == null || userIds.Count == 0)
            return new Dictionary<Guid, ChatContactDto>();

        var idSet = userIds.Distinct().ToHashSet();
        var users = await _userRepository.ToListAsync(u => idSet.Contains(u.Id), cancellationToken);
        var detailByUserId = await LoadDetailsAsync(idSet.ToList(), cancellationToken);

        return users.ToDictionary(u => u.Id, u => ToContactDto(u, detailByUserId));
    }

    public async Task<Result<ChatContactProfileDto>> GetProfileAsync(Guid userId)
    {
        var user = await _userRepository.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return Fail<ChatContactProfileDto>("User not found.", 404);

        var detail = (await LoadDetailsAsync(new[] { userId })).GetValueOrDefault(userId);
        var pr = (await _presence.ResolveEffectiveAsync(new[] { userId })).FirstOrDefault();
        // The profile is an open directory (anyone with chat.use may look up anyone), so
        // whether a colleague's e-mail and phone number belong on it is a deployment
        // decision - ChatOptions.ExposeContactDetails, on by default. Bio is the user's
        // own blurb and stays regardless.
        var exposeDetails = _options.Value.ExposeContactDetails;
        return Ok(new ChatContactProfileDto
        {
            UserId = userId,
            Name = ResolveDisplayName(user, detail),
            AvatarFileId = detail?.AvatarId?.ToString(),
            // Email/Phone live on the Identity User; Bio on UserDetail. All optional -
            // the profile card only renders the rows that carry a value.
            Email = exposeDetails ? user.Email : null,
            Phone = exposeDetails ? user.PhoneNumber : null,
            Bio = detail?.Bio,
            Status = pr?.Status ?? UserPresenceStatus.Offline,
            LastSeenAt = pr?.LastSeenAt
        });
    }

    private async Task<Dictionary<Guid, UserDetail>> LoadDetailsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, UserDetail>();
        var idSet = userIds.ToHashSet();
        var details = await _userDetailRepository.ToListAsync(d => idSet.Contains(d.UserId), cancellationToken);
        // One UserDetail per user; guard against accidental duplicates by keeping the first.
        var map = new Dictionary<Guid, UserDetail>();
        foreach (var d in details) map.TryAdd(d.UserId, d);
        return map;
    }

    private static ChatContactDto ToContactDto(User user, IReadOnlyDictionary<Guid, UserDetail> detailByUserId)
    {
        detailByUserId.TryGetValue(user.Id, out var detail);
        return new ChatContactDto
        {
            UserId = user.Id,
            Name = ResolveDisplayName(user, detail),
            AvatarFileId = detail?.AvatarId?.ToString()
        };
    }

    /// <summary>
    /// Display-name resolution shared across chat: Nickname → real name (FirstName/LastName) → UserName.
    /// </summary>
    internal static string ResolveDisplayName(User user, UserDetail? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail?.Nickname)) return detail!.Nickname!;
        if (!string.IsNullOrWhiteSpace(detail?.FullName)) return detail!.FullName!;
        return user.UserName ?? string.Empty;
    }
}
