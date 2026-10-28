namespace Tnzi.Chat.Services;

public class ChatContactService : ApplicationService, IChatContactService
{
    /// <summary>
    /// 候选窗倍数：SQL 侧每次取 <c>ContactSearchLimit × 本值</c> 个候选，再剔除没有 <c>chat.use</c> 的。
    /// 窗比页大，是为了让页面在少数候选被剔掉后一个窗就能填满。
    /// </summary>
    internal const int CandidateWindowFactor = 3;

    /// <summary>
    /// 一次搜索最多翻几个候选窗。聊天是白名单制，持 <c>chat.use</c> 的人是少数时一个窗里只剩几个可用的人，
    /// 页面没填满就接着翻下一个窗；有上界，是为了让一次搜索的代价与目录规模无关
    /// （最多 <c>ContactSearchLimit × CandidateWindowFactor × 本值</c> 个候选）。
    /// 持有者稀疏到翻满上限仍填不满时，页面就少几个，换个更具体的关键字即可。
    /// </summary>
    internal const int MaxCandidateWindows = 10;

    private readonly IRepository<User, Guid> _userRepository;
    private readonly IRepository<UserDetail, Guid> _userDetailRepository;
    private readonly IPresenceService _presence;
    private readonly IOptionsSnapshot<ChatOptions> _options;
    private readonly IFunctionAuthorizationService? _functionAuthorization;
    private readonly IChatAccessService? _chatAccess;

    /// <summary>
    /// 目录的租户边界。★ <c>User</c> 刻意不实现 <c>IMultiTenant</c>（登录链路要跨租户找人），没有全局过滤器替它把关：
    /// 不经它裁剪，多租户部署里任何一个持 <c>chat.use</c> 的人换着关键字搜就能把全部租户的用户名单拖出来，
    /// 再按 id 打开资料卡读到别家租户员工的邮箱与电话。多租户未开启或没有租户上下文时它不裁剪，单租户逐字不变。
    /// </summary>
    private readonly IUserTenantScopeProvider _userScope;

    public ChatContactService(
        IServiceProvider serviceProvider,
        IRepository<User, Guid> userRepository,
        IRepository<UserDetail, Guid> userDetailRepository,
        IPresenceService presence,
        IOptionsSnapshot<ChatOptions> options,
        IUserTenantScopeProvider userScope,
        IFunctionAuthorizationService? functionAuthorization = null,
        IChatAccessService? chatAccess = null)
        : base(serviceProvider)
    {
        _userRepository = Check.NotNull(userRepository);
        _userDetailRepository = Check.NotNull(userDetailRepository);
        _presence = Check.NotNull(presence);
        _options = Check.NotNull(options);
        _userScope = Check.NotNull(userScope);
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
        // ★ Paging happens in SQL (ORDER BY + SKIP/TAKE), never by materialising the whole
        // directory: the previous shape loaded every User row into memory and ran one
        // permission check per row before taking the first 20 - on a 100k-user
        // deployment a single "new chat" click was 100k entities plus 100k+ DB
        // round-trips, with no privilege required. Candidates are read in windows that
        // are a bounded multiple of the page, and at most MaxCandidateWindows of them.
        var limit = Math.Max(1, _options.Value.ContactSearchLimit);
        // 租户边界在 SQL 侧加（与分页同一条查询），不是取回来再筛：后者会让页面被别家租户的候选占满。
        var candidates = _userScope.Current.Apply(_userRepository.AsQueryable())
            .Where(u => u.Id != me && u.UserName != null && !hiddenIds.Contains(u.Id));
        if (!string.IsNullOrEmpty(kw))
            candidates = candidates.Where(u => u.UserName!.ToLower().Contains(kw));

        // Id 作次序键：分窗靠 Skip，同名（不同大小写）的两行在窗与窗之间不能换位。
        var ordered = candidates.OrderBy(u => u.UserName).ThenBy(u => u.Id);
        var window = limit * CandidateWindowFactor;

        // Users without `chat.use` can't take part in chat (their inbound is blocked /
        // isolated), so they must not appear in the new-chat / add-member picker.
        // FilterDisabledAsync returns the subset lacking the grant (empty when the
        // gate is inactive → nothing hidden), one batch call per (bounded) window.
        // When grants are sparse a window may hold only a few usable contacts, so keep
        // paging windows until the page fills, the directory ends, or the cap is hit.
        var taken = new List<User>(limit);
        for (var page = 0; page < MaxCandidateWindows && taken.Count < limit; page++)
        {
            var batch = await ordered.Skip(page * window).Take(window).ToListAsync();
            if (batch.Count == 0)
                break;

            var usable = batch;
            if (_chatAccess != null)
            {
                var disabled = await _chatAccess.FilterDisabledAsync(batch.Select(u => u.Id));
                if (disabled.Count > 0)
                    usable = batch.Where(u => !disabled.Contains(u.Id)).ToList();
            }

            taken.AddRange(usable.Take(limit - taken.Count));

            if (batch.Count < window)
                break;
        }

        var detailByUserId = await LoadDetailsAsync(taken.Select(u => u.Id).ToList());

        var list = taken.Select(u => ToContactDto(u, detailByUserId)).ToList();

        return Ok<IReadOnlyList<ChatContactDto>>(list);
    }

    public async Task<IReadOnlyDictionary<Guid, ChatContactDto>> ResolveProfilesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds == null || userIds.Count == 0)
            return new Dictionary<Guid, ChatContactDto>();

        // 越界的 id 省略（与「不存在」相同）：消息发送人 / 会话成员的资料都经这里补齐，
        // 别家租户的账号不该借一条历史会话把名字与头像带出来。不裁剪时原样返回、不查库。
        var idSet = (await _userScope.FilterAsync(userIds, cancellationToken)).ToHashSet();
        if (idSet.Count == 0)
            return new Dictionary<Guid, ChatContactDto>();

        var users = await _userRepository.ToListAsync(u => idSet.Contains(u.Id), cancellationToken);
        var detailByUserId = await LoadDetailsAsync(idSet.ToList(), cancellationToken);

        return users.ToDictionary(u => u.Id, u => ToContactDto(u, detailByUserId));
    }

    public async Task<Result<ChatContactProfileDto>> GetProfileAsync(Guid userId)
    {
        // 别家租户的账号与不存在的账号答同一个 404：403 会告诉对方「这个 id 存在，只是不归你」。
        if (!await _userScope.ContainsAsync(userId))
            return Fail<ChatContactProfileDto>("User not found.", 404);

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
