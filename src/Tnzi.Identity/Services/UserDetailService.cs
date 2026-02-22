

namespace Tnzi.Identity.Services;

/// <summary>
/// 用户详情服务实现
/// </summary>
public class UserDetailService : ApplicationService, IUserDetailService
{
    private readonly IRepository<UserDetail, Guid> _repository;
    private readonly UserManager<User> _userManager;

    public UserDetailService(
        IRepository<UserDetail, Guid> repository,
        UserManager<User> userManager,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _userManager = Check.NotNull(userManager);
    }

    public async Task<Result<UserDetailDto>> GetByUserIdAsync(Guid userId)
    {
        // 不使用 AsNoTracking，以便在同一个请求中能读取到 ChangeTracker 中尚未提交的变更
        var userDetail = await _repository
            .Where(ud => ud.UserId == userId)
            .FirstOrDefaultAsync();

        if (userDetail == null)
        {
            return Fail<UserDetailDto>("User detail not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var dto = userDetail.MapTo<UserDetailDto>();
        return Ok(dto);
    }

    public async Task<Result<IDictionary<Guid, UserDetailDto>>> GetDetailsByUserIdsAsync(IEnumerable<Guid> userIds)
    {
        var idList = userIds.Distinct().ToList();
        if (!idList.Any())
        {
            return Ok((IDictionary<Guid, UserDetailDto>)new Dictionary<Guid, UserDetailDto>());
        }

        var details = await _repository
            .Where(ud => idList.Contains(ud.UserId))
            .ToListAsync();

        var dict = details.ToDictionary(ud => ud.UserId, ud => ud.MapTo<UserDetailDto>());
        return Ok((IDictionary<Guid, UserDetailDto>)dict);
    }

    public async Task<Result<UserDetailDto>> CreateOrUpdateAsync(Guid userId, CreateUserDetailDto dto)
    {
        // 验证用户是否存在
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<UserDetailDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 查找现有详情
        var existingDetail = await _repository
            .Where(ud => ud.UserId == userId)
            .FirstOrDefaultAsync();

        if (existingDetail != null)
        {
            // 更新现有详情
            dto.MapTo(existingDetail);

            await _repository.UpdateAsync(existingDetail);

            var resultDto = existingDetail.MapTo<UserDetailDto>();
            LogInformation($"User detail updated for user: {userId}");
            return Ok(resultDto);
        }
        else
        {
            // 创建新详情
            var userDetail = dto.MapTo<UserDetail>();
            userDetail.UserId = userId;

            await _repository.InsertAsync(userDetail);
            var resultDto = userDetail.MapTo<UserDetailDto>();
            LogInformation($"User detail created for user: {userId}");
            return Ok(resultDto);
        }
    }

    public async Task<Result> DeleteAsync(Guid userId)
    {
        var userDetail = await _repository
            .Where(ud => ud.UserId == userId)
            .FirstOrDefaultAsync();

        if (userDetail == null)
        {
            return Fail("User detail not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        await _repository.DeleteAsync(userDetail);
        LogInformation($"User detail deleted for user: {userId}");
        return Ok();
    }

    public async Task<Result<UserDetailDto>> GetCurrentAsync()
    {
        if (CurrentUser?.Id == null)
        {
            return Fail<UserDetailDto>("User not authenticated", 401, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 尝试获取详情，如果不存在则自动创建一个基础的
        var userDetail = await _repository.FirstOrDefaultAsync(x => x.UserId == CurrentUser.Id.Value);
        if (userDetail == null)
        {
            var user = await _userManager.FindByGuidAsync(CurrentUser.Id.Value);
            userDetail = new UserDetail
            {
                UserId = CurrentUser.Id.Value,
                // 使用用户名作为默认昵称
                Nickname = user?.UserName
            };

            try
            {
                await _repository.InsertAsync(userDetail);
            }
            catch (DbUpdateException ex)
            {
                // 处理并发创建冲突：如果两个请求同时检测到不存在并尝试创建
                // 数据库的唯一约束会阻止第二个插入，此时重新查询即可
                if (ex.IsUniqueConstraintViolation())
                {
                    // 并发创建冲突，重新查询
                    userDetail = await _repository.FirstOrDefaultAsync(x => x.UserId == CurrentUser.Id.Value);
                    if (userDetail == null)
                    {
                        // 如果重新查询仍然为空，说明是其他错误，重新抛出
                        throw;
                    }
                }
                else
                {
                    // 其他数据库错误，重新抛出
                    throw;
                }
            }
        }

        return Ok(userDetail.MapTo<UserDetailDto>());
    }

    public async Task<Result<UserDetailDto>> UpdateCurrentAsync(CreateUserDetailDto dto)
    {
        if (CurrentUser?.Id == null)
        {
            return Fail<UserDetailDto>("User not authenticated", 401, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        return await CreateOrUpdateAsync(CurrentUser.Id.Value, dto);
    }

}