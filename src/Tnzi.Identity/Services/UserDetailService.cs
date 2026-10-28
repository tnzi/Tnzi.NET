

namespace Tnzi.Identity.Services;

/// <summary>
/// 用户详情服务实现
/// </summary>
/// <remarks>
/// <see cref="IFileReadAccessProbe"/> 可选注入（契约在核心 <c>Tnzi</c> 程序集，实现随 <c>Tnzi.Storage</c> 注册，
/// 本模块不引用它）：把一个文件 id 写进 <see cref="UserDetail.AvatarId"/> 之前先问「这个人本来就读得到它吗」。
/// 这是 <c>UserDetail</c> 唯一的写入口（自助资料 / 管理端 / OAuth 导入都经这里），门就设在这里。
/// <para>
/// 同一个理由，「这个账号归不归调用者管」也问在这里（<see cref="RejectForeignTargetAsync"/>）：
/// 管理端 <c>admin/user-details</c> 直接调本服务，不经过 <c>UserService</c> 的租户裁剪与超管护栏。
/// </para>
/// <para>
/// ★★★ <b>详情行归账号，不归请求的租户上下文</b>（<see cref="LiveDetails"/>、<see cref="ClaimForAccount"/>）。
/// </para>
/// </remarks>
public class UserDetailService : ApplicationService, IUserDetailService
{
    private readonly IRepository<UserDetail, Guid> _repository;
    private readonly UserManager<User> _userManager;
    private readonly IUserTenantScopeProvider _scope;
    private readonly IFileReadAccessProbe? _fileAccess;
    private readonly IFunctionAuthorizationService? _functionAuthorization;

    public UserDetailService(
        IRepository<UserDetail, Guid> repository,
        UserManager<User> userManager,
        IServiceProvider serviceProvider,
        IUserTenantScopeProvider scope,
        IFileReadAccessProbe? fileAccess = null,
        IFunctionAuthorizationService? functionAuthorization = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _userManager = Check.NotNull(userManager);
        _scope = Check.NotNull(scope);
        _fileAccess = fileAccess;
        _functionAuthorization = functionAuthorization;
    }

    public async Task<Result<UserDetailDto>> GetByUserIdAsync(Guid userId)
    {
        // 查找不看租户过滤器（见 LiveDetails），归属改由范围判定把关；越界与没有这一行回答相同。
        if (!await _scope.ContainsAsync(userId))
        {
            return Fail<UserDetailDto>("User detail not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 不使用 AsNoTracking，以便在同一个请求中能读取到 ChangeTracker 中尚未提交的变更
        var userDetail = await LiveDetails()
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

        // 与 GetByUserIdAsync 同一口径：先收窄到范围内的账号，再不看租户过滤器去取它们的行。
        var inScope = (await _scope.FilterAsync(idList)).ToList();
        if (inScope.Count == 0)
        {
            return Ok((IDictionary<Guid, UserDetailDto>)new Dictionary<Guid, UserDetailDto>());
        }

        var details = await LiveDetails()
            .Where(ud => inScope.Contains(ud.UserId))
            .ToListAsync();

        var dict = details.ToDictionary(ud => ud.UserId, ud => ud.MapTo<UserDetailDto>());
        return Ok((IDictionary<Guid, UserDetailDto>)dict);
    }

    public async Task<Result<UserDetailDto>> CreateOrUpdateAsync(Guid userId, CreateUserDetailDto dto)
    {
        var rejected = await RejectForeignTargetAsync(userId);
        if (rejected != null)
        {
            return Fail<UserDetailDto>(rejected.Message!, rejected.Code ?? 403, rejected.ErrorCode);
        }

        // 验证用户是否存在
        var user = await _userManager.FindByGuidAsync(userId);
        if (user == null)
        {
            return Fail<UserDetailDto>("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        // 查找现有详情（不看租户过滤器，理由见 LiveDetails）
        var existingDetail = await LiveDetails()
            .Where(ud => ud.UserId == userId)
            .FirstOrDefaultAsync();

        var denial = await RejectForeignAvatarAsync(dto.AvatarId, existingDetail?.AvatarId);
        if (denial != null)
            return denial;

        // ★ 生日是纯日期：客户端发来的 YYYY-MM-DD 反序列化成 Kind=Unspecified，
        //   PostgreSQL 的 timestamptz 列拒写（保存以 500 结束），按框架 date-only 口径落成那一天的 UTC 零点。
        var birthday = dto.Birthday?.ToUtcDate();

        if (existingDetail != null)
        {
            // 更新现有详情
            dto.MapTo(existingDetail);
            existingDetail.Birthday = birthday;
            ClaimForAccount(existingDetail, user);

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
            userDetail.Birthday = birthday;
            ClaimForAccount(userDetail, user);

            await _repository.InsertAsync(userDetail);
            var resultDto = userDetail.MapTo<UserDetailDto>();
            LogInformation("User detail created for user: {UserId}", userId);
            return Ok(resultDto);
        }
    }

    /// <summary>
    /// 全部未删除的详情行，<b>不按环境租户过滤</b>。按账号取详情一律从这里出发。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ 账号表不是 <c>IMultiTenant</c>，而全局管理员（当前租户为空）按 <see cref="UserTenantScope"/> 本来就管得到
    /// 每个租户的账号；详情却被严格等值的租户过滤器管着。按环境租户去找，全局管理员看到的是空资料，
    /// 保存时找不到既有行、再插一行，撞上 <c>UserId</c> 唯一索引（500，而账号表那一半已经写下）；
    /// 对方还没有详情时则种下一行 <c>TenantId = null</c>，对方在自己租户里从此读不到，自助资料每次 500。
    /// </para>
    /// <para>
    /// 不看过滤器是安全的：<c>UserId</c> 上的唯一索引不分租户，一个账号至多一行；谁能碰这个账号由调用前的范围判定决定
    /// （<see cref="RejectForeignTargetAsync"/> / <see cref="GetByUserIdAsync"/> / <see cref="GetDetailsByUserIdsAsync"/>，
    /// 本人恒在范围内）。软删条件手工补回：<c>IgnoreQueryFilters</c> 是整体摘除。刻意不用
    /// <c>IDataFilterManager.Disable</c>：它每次调用记一条跨租户 Warning，而这里每次读写资料都要走一遍。
    /// </para>
    /// </remarks>
    private IQueryable<UserDetail> LiveDetails() => _repository.IgnoreQueryFilters().Where(ud => !ud.IsDeleted);

    /// <summary>
    /// 把详情行的租户号对齐到它所属的账号。账号没有租户（全局账号）时不动，交给审计填充按环境租户补齐，与此前相同。
    /// </summary>
    /// <remarks>
    /// 新行不再打上「恰好在操作它的那个请求」的租户号；既有行若打错了（修复之前种下的），写一次就改回来，
    /// 按租户过滤的其它读取方（聊天联系人、<c>IUserDisplayNameProvider</c> 的显示名）才看得见它。
    /// </remarks>
    private static void ClaimForAccount(UserDetail detail, User user)
    {
        if (user.TenantId is { } owner)
        {
            detail.TenantId = owner;
        }
    }

    /// <summary>
    /// 调用者能不能写这个账号的详情。<see langword="null"/> = 可以。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <c>admin/user-details</c> 的写端点直接调本服务，而 <c>PUT admin/users/{id}</c> 写的是同一组字段
    /// （姓名 / 昵称 / 头像）、却在 <c>UserService</c> 里过了租户裁剪与超管护栏：同一件事两条路，只有一条有门。
    /// </para>
    /// <para>
    /// ① <b>租户范围</b>：账号表不是 <c>IMultiTenant</c>，按 id 找人跨租户可达；<c>UserDetail</c> 却被严格等值的租户过滤器管着。
    /// 外租户管理员写进来时看不见对方已有的那一行，于是插入一行打着自己租户号的详情：撞上 <c>UserId</c> 唯一索引（500），
    /// 或者种下一行对方自己永远读不到的数据。越界与不存在同样答 404（理由见 <see cref="UserTenantScope"/>）。
    /// </para>
    /// <para>
    /// ② <b>超管护栏</b>（<see cref="SuperAdminTargetGuard"/>）：普通管理员不能替超管改资料。
    /// </para>
    /// <para>
    /// 两道都对其它调用方透明：本人恒在范围内、改自己不受护栏影响（自助资料）；没有已认证主体的调用不裁剪、
    /// 没有调用者就没有护栏（OAuth / 注册时给新账号建详情）；<c>UserService.UpdateAsync</c> 已经各问过一次，这里再问只多一次查询。
    /// </para>
    /// </remarks>
    private async Task<Result?> RejectForeignTargetAsync(Guid userId)
    {
        if (!await _scope.ContainsAsync(userId))
        {
            return Fail("User not found", 404, ErrorCodes.IDENTITY_USER_NOT_FOUND);
        }

        if (await SuperAdminTargetGuard.IsForbiddenAsync(_functionAuthorization, CurrentUser?.Id, userId))
        {
            return Fail(SuperAdminTargetGuard.Message, 403, ErrorCodes.FORBIDDEN);
        }

        return null;
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
        var rejected = await RejectForeignTargetAsync(userId);
        if (rejected != null)
        {
            return rejected;
        }

        var userDetail = await LiveDetails()
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

        // 尝试获取详情，如果不存在则自动创建一个基础的（查找不看租户过滤器，理由见 LiveDetails）
        var currentUserId = CurrentUser.Id.Value;
        var userDetail = await LiveDetails().Where(x => x.UserId == currentUserId).FirstOrDefaultAsync();
        if (userDetail == null)
        {
            var user = await _userManager.FindByGuidAsync(currentUserId);
            userDetail = new UserDetail
            {
                UserId = currentUserId,
                // 使用用户名作为默认昵称
                Nickname = user?.UserName
            };
            if (user != null)
            {
                ClaimForAccount(userDetail, user);
            }

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
                    userDetail = await LiveDetails().Where(x => x.UserId == currentUserId).FirstOrDefaultAsync();
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