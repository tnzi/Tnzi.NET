namespace Tnzi.Chat.Services;

public class ChatContactService : ApplicationService, IChatContactService
{
    private readonly IRepository<User, Guid> _userRepository;
    private readonly IRepository<UserDetail, Guid> _userDetailRepository;
    private readonly IPresenceService _presence;
    private const int SearchLimit = 20;

    public ChatContactService(
        IServiceProvider serviceProvider,
        IRepository<User, Guid> userRepository,
        IRepository<UserDetail, Guid> userDetailRepository,
        IPresenceService presence)
        : base(serviceProvider)
    {
        _userRepository = Check.NotNull(userRepository);
        _userDetailRepository = Check.NotNull(userDetailRepository);
        _presence = Check.NotNull(presence);
    }

    public async Task<Result<IReadOnlyList<ChatContactDto>>> SearchUsersAsync(string? keyword)
    {
        var me = GetRequiredCurrentUser().Id!.Value;
        var kw = keyword?.Trim().ToLower();

        // Blank keyword → first page of the directory (excluding self) so the picker can
        // show a starting contact list. A keyword narrows by username (case-insensitive).
        var users = string.IsNullOrEmpty(kw)
            ? await _userRepository.ToListAsync(u => u.Id != me && u.UserName != null)
            : await _userRepository.ToListAsync(u => u.Id != me && u.UserName != null && u.UserName.ToLower().Contains(kw));

        var taken = users.Take(SearchLimit).ToList();
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
        return Ok(new ChatContactProfileDto
        {
            UserId = userId,
            Name = ResolveDisplayName(user, detail),
            AvatarFileId = detail?.AvatarId?.ToString(),
            // Email/Phone live on the Identity User; Bio on UserDetail. All optional —
            // the profile card only renders the rows that carry a value.
            Email = user.Email,
            Phone = user.PhoneNumber,
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
