namespace Tnzi.AI.Services;

/// <summary>
/// 用户 AI 档案服务实现
/// </summary>
public class UserProfileService : ApplicationService, IUserProfileService
{
    private readonly IRepository<UserProfile, Guid> _repository;

    public UserProfileService(IServiceProvider serviceProvider, IRepository<UserProfile, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Result<UserProfileDto>> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await FindByUserIdAsync(userId, ct);
        if (profile == null)
        {
            profile = new UserProfile { UserId = userId };
            await _repository.InsertAsync(profile, ct);
        }

        return Ok(profile.MapTo<UserProfileDto>());
    }

    public async Task<Result<UserProfileDto>> UpdateAsync(Guid userId, UpdateUserProfileDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var profile = await FindByUserIdAsync(userId, ct);
        if (profile == null)
        {
            profile = new UserProfile { UserId = userId };
            input.MapTo(profile);
            await _repository.InsertAsync(profile, ct);
        }
        else
        {
            input.MapTo(profile);
            await _repository.UpdateAsync(profile, ct);
        }

        return Ok(profile.MapTo<UserProfileDto>());
    }

    public async Task<UserProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _repository.FirstOrDefaultAsync(e => e.UserId == userId, ct);
    }
}
