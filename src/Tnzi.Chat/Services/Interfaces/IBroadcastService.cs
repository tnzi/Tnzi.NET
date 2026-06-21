namespace Tnzi.Chat.Services.Interfaces;

public interface IBroadcastService
{
    Task<Result<int>> BroadcastToUsersAsync(IEnumerable<Guid> userIds, string content);
    Task<Result<int>> BroadcastToRoleAsync(Guid roleId, string content);
    Task<Result<int>> BroadcastAsync(BroadcastDto input);
}
