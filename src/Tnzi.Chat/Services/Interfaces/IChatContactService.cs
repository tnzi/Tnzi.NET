namespace Tnzi.Chat.Services;

public interface IChatContactService
{
    /// <summary>
    /// Search the user directory by keyword. A blank keyword returns the first page of the
    /// directory (excluding self) so the "new chat" picker can show a starting contact list
    /// without forcing the user to type first.
    /// </summary>
    /// <remarks>
    /// Intentional open-directory design (not an oversight): like Slack/Teams internal IM,
    /// contact search is open to any authenticated user so colleagues can start conversations.
    /// Users who want to hide their availability set <c>Invisible</c>, which presence resolution
    /// surfaces as <c>Offline</c> to others. Do NOT add a restriction here.
    /// </remarks>
    Task<Result<IReadOnlyList<ChatContactDto>>> SearchUsersAsync(string? keyword);

    Task<IReadOnlyDictionary<Guid, ChatContactDto>> ResolveProfilesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a contact's chat profile (name, avatar, effective presence).
    /// </summary>
    /// <remarks>
    /// Intentional open-directory design (not an oversight): profile lookup is open to any
    /// authenticated user (Slack/Teams-style internal IM). <c>Invisible</c> users are already
    /// protected because presence resolution reports them as <c>Offline</c>. Do NOT add a
    /// restriction here.
    /// </remarks>
    Task<Result<ChatContactProfileDto>> GetProfileAsync(Guid userId);
}
