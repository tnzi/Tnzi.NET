namespace Tnzi.AI.Services;

/// <summary>
/// 用户 AI 档案服务接口
/// </summary>
public interface IUserProfileService
{
    Task<Result<UserProfileDto>> GetOrCreateAsync(Guid userId, CancellationToken ct = default);
    Task<Result<UserProfileDto>> UpdateAsync(Guid userId, UpdateUserProfileDto input, CancellationToken ct = default);

    /// <summary>按用户 ID 查询（内部使用，不包装 Result）</summary>
    Task<UserProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct = default);
}
