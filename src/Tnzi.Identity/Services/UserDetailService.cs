

namespace Tnzi.Identity.Services;

/// <summary>
/// 用户详情服务实现
/// </summary>
/// <remarks>
/// <see cref="IFileReadAccessProbe"/> 可选注入（契约在核心 <c>Tnzi</c> 程序集，实现随 <c>Tnzi.Storage</c> 注册，
/// 本模块不引用它）：把一个文件 id 写进 <see cref="UserDetail.AvatarId"/> 之前先问「这个人本来就读得到它吗」。
/// 这是 <c>UserDetail</c> 唯一的写入口（自助资料 / 管理端 / OAuth 导入都经这里），门就设在这里。
/// </remarks>
public class UserDetailService : ApplicationService, IUserDetailService
{
    private readonly IRepository<UserDetail, Guid> _repository;
    private readonly UserManager<User> _userManager;
    private readonly IFileReadAccessProbe? _fileAccess;

    public UserDetailService(
        IRepository<UserDetail, Guid> repository,
        UserManager<User> userManager,
        IServiceProvider serviceProvider,
        IFileReadAccessProbe? fileAccess = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _userManager = Check.NotNull(userManager);
        _fileAccess = fileAccess;
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

        var denial = await RejectForeignAvatarAsync(dto.AvatarId, existingDetail?.AvatarId);
        if (denial != null)
            return denial;

        if (existingDetail != null)
        {
            // 更新现有详情
            dto.MapTo(existingDetail);

            await _repository.UpdateAsync(existingDetail);

            var resultDto = existingDetail.MapTo<UserDetailDto>();
            LogInformation("User detail updated for user: {UserId}", userId);
            return Ok(resultDto);
        }
        else
        {
            // 创建新详情
            var userDetail = dto.MapTo<UserDetail>();
            userDetail.UserId = userId;

            await _repository.InsertAsync(userDetail);
            var resultDto = userDetail.MapTo<UserDetailDto>();
            LogInformation("User detail created for user: {UserId}", userId);
            return Ok(resultDto);
        }
    }

    /// <summary>
    /// 这个文件 id 能不能被当前调用者写成头像。<see langword="null"/> = 可以。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <see cref="UserDetail.AvatarId"/> 是 <c>[FileField(Public = true)]</c>：引用登记的同一个事务里
    /// <c>FileReferenceProcessor</c> 会把那份文件翻成 <c>IsPublic = true</c>，之后**匿名** <c>GET files/{id}/download</c>
    /// 直接放行 —— 比 Chat / Finance / Signing 那三处「发布给这条记录的可见者」更强，是发布给全世界。
    /// 而写入口是自助的 <c>PUT users/profile</c>，不要任何权限码；实体 ID 是顺序 GUID。不问一句就写，
    /// 任何登录用户猜一个 id 填进头像，那份 HR 档案 / 合同 / 支票就公开了。
    /// </para>
    /// <para>
    /// ★ 判据是「这个人本来就读得到它吗」（<see cref="IFileReadAccessProbe"/>），不认请求级凭据
    /// （URL 签名令牌 / 分享链接授予）：一条会过期的分享链接不该被换成一份永久公开。
    /// 「不存在」与「读不到」回答同一句话，否则这个端点就成了「这个 id 存不存在」的探针。
    /// </para>
    /// <para>
    /// ★ <b>同一个 id 再存一次不再问</b>：前端按 REPLACE 语义每次保存都把现有 avatarId 原样带回来，
    /// 而发布是写入那一刻的事，重存没有发布任何新东西；此时再问，头像文件一旦被删（引用计数归零后
    /// 被清理、或管理员删掉），用户从此连昵称都改不了。
    /// </para>
    /// <para>
    /// ★ 存储模块缺席时拒绝，不是跳过：「跳过校验」与「校验通过」在接口上完全一致。501 而不是 503：
    /// 这不是暂时性故障，重试永远不会好。不带头像文件的写入（清空 / 只改昵称 / 只给 AvatarUrl）不需要探针。
    /// </para>
    /// </remarks>
    private async Task<Result<UserDetailDto>?> RejectForeignAvatarAsync(Guid? requested, Guid? current)
    {
        if (requested is not { } fileId)
            return null;

        // Guid.Empty 在 [FileField] 那一侧会被静默忽略（不产生引用行）：与其留一个安静的空操作，不如当场说不。
        if (fileId == Guid.Empty)
            return Fail<UserDetailDto>("The avatar file reference is not a valid file id.", 400, ErrorCodes.VALIDATION_ERROR);

        if (fileId == current)
            return null;

        if (_fileAccess == null)
            return Fail<UserDetailDto>("Setting an avatar file needs the storage module. Load Tnzi.Storage, or use AvatarUrl.", 501);

        if (!await _fileAccess.CanReadAsync(fileId))
            return Fail<UserDetailDto>("That file cannot be used as an avatar.", 403, ErrorCodes.FORBIDDEN);

        return null;
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
        LogInformation("User detail deleted for user: {UserId}", userId);
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