namespace Tnzi.Chat.Services.Interfaces;

public interface IPresenceService
{
    Task<Result> SetStatusAsync(UserPresenceStatus status);
    Task<UserPresenceStatus> GetMyStatusAsync();
    Task<IReadOnlyList<UserPresenceDto>> ResolveEffectiveAsync(IReadOnlyCollection<Guid> userIds);
    Task BroadcastAsync(Guid userId);
    Task MarkOfflineAsync(Guid userId);
}
