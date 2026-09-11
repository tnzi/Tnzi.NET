namespace Tnzi.Authorization.Services;

/// <summary>
/// 用户-功能直授服务实现
/// </summary>
/// <remarks>
/// 同时管理 allow 直授行（IsGranted = true）与 deny 否定行（IsGranted =
/// false）。解析语义为 <c>(角色允许 ∪ 用户允许) − 用户拒绝</c>（用户级优先）。
/// 唯一索引 (UserId, FunctionId) 保证同一功能对同一用户只有一行——allow 与
/// deny 互斥，写路径以"后写者赢"翻转既有行。
/// </remarks>
public class UserFunctionService : ApplicationService, IUserFunctionService
{
    private readonly IRepository<UserFunction, Guid> _userFunctionRepository;
    private readonly IRepository<ModuleFunction, Guid> _moduleFunctionRepository;
    private readonly IFunctionAuthorizationService _functionAuthorizationService;
    private readonly FunctionAuthCache? _functionAuthCache;

    /// <summary>
    /// 初始化一个<see cref="UserFunctionService"/>类型的新实例
    /// </summary>
    public UserFunctionService(
        IServiceProvider serviceProvider,
        IRepository<UserFunction, Guid> userFunctionRepository,
        IRepository<ModuleFunction, Guid> moduleFunctionRepository,
        IFunctionAuthorizationService functionAuthorizationService,
        FunctionAuthCache? functionAuthCache = null)
        : base(serviceProvider)
    {
        _userFunctionRepository = Check.NotNull(userFunctionRepository);
        _moduleFunctionRepository = Check.NotNull(moduleFunctionRepository);
        _functionAuthorizationService = Check.NotNull(functionAuthorizationService);
        _functionAuthCache = functionAuthCache;
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<ModuleFunction>>> GetUserFunctionsAsync(Guid userId)
    {
        var functionIds = await _userFunctionRepository
            .Where(uf => uf.UserId == userId && uf.IsEnabled && uf.IsGranted)
            .Select(uf => uf.FunctionId)
            .ToListAsync();

        if (functionIds.Count == 0)
        {
            return Ok(Enumerable.Empty<ModuleFunction>());
        }

        var functions = await _moduleFunctionRepository
            .Where(f => functionIds.Contains(f.Id) && f.IsEnabled && !f.IsRetired)
            .OrderBy(f => f.Order)
            .ToListAsync();
        return Ok((IEnumerable<ModuleFunction>)functions);
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<Guid>>> GetUserFunctionIdsAsync(Guid userId)
    {
        // 与角色侧同规：只返回指向生效功能的行。休眠行（退役 / 禁用功能）不进矩阵，
        // 也就不能出现在矩阵回写的集合里，否则同一判据的写路径会把整次保存拒掉。
        var functionIds = await _userFunctionRepository
            .Where(uf => uf.UserId == userId && uf.IsEnabled && uf.IsGranted)
            .Join(LiveFunctions(), uf => uf.FunctionId, f => f.Id, (uf, _) => uf.FunctionId)
            .ToListAsync();
        return Ok((IEnumerable<Guid>)functionIds);
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<Guid>>> GetUserDeniedFunctionIdsAsync(Guid userId)
    {
        var functionIds = await _userFunctionRepository
            .Where(uf => uf.UserId == userId && uf.IsEnabled && !uf.IsGranted)
            .Join(LiveFunctions(), uf => uf.FunctionId, f => f.Id, (uf, _) => uf.FunctionId)
            .ToListAsync();
        return Ok((IEnumerable<Guid>)functionIds);
    }

    /// <inheritdoc />
    public async Task<Result> AssignFunctionsToUserAsync(Guid userId, IEnumerable<Guid> functionIds)
    {
        var functionIdList = functionIds.ToList();
        if (functionIdList.Count == 0)
        {
            return Ok("No functions to assign");
        }

        var violation = await GetUserGrantViolationAsync(userId, functionIdList);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        var missing = await FindMissingFunctionsAsync(functionIdList);
        if (missing != null) return missing;

        // 已有 allow 行跳过；既有 deny 行被显式授予翻转（后写者赢——
        // 唯一索引 (UserId, FunctionId) 不允许 allow/deny 并存）。
        var existingRows = await _userFunctionRepository
            .Where(uf => uf.UserId == userId && functionIdList.Contains(uf.FunctionId))
            .Select(uf => new { uf.FunctionId, uf.IsGranted })
            .ToListAsync();
        var existingAllowIds = existingRows.Where(r => r.IsGranted).Select(r => r.FunctionId).ToList();
        var denyIdsToFlip = existingRows.Where(r => !r.IsGranted).Select(r => r.FunctionId).ToList();

        var newFunctionIds = functionIdList.Except(existingAllowIds).ToList();

        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            if (denyIdsToFlip.Count > 0)
            {
                await _userFunctionRepository.DeleteAsync(uf =>
                    uf.UserId == userId && !uf.IsGranted && denyIdsToFlip.Contains(uf.FunctionId));
            }

            if (newFunctionIds.Count > 0)
            {
                var userFunctions = newFunctionIds.Select(functionId => new UserFunction
                {
                    UserId = userId,
                    FunctionId = functionId,
                    IsGranted = true,
                    IsEnabled = true
                }).ToList();
                await _userFunctionRepository.InsertManyAsync(userFunctions);
            }

            return Ok($"Assigned {newFunctionIds.Count} functions to user");
        });

        if (result.Succeeded)
        {
            await InvalidateUserCacheAsync(userId);
            await PublishUserFunctionsChangedAsync(userId, PermissionChangeType.Assigned, newFunctionIds);
            LogInformation("Assigned {Count} functions directly to user: {UserId}", newFunctionIds.Count, userId);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result> RemoveFunctionsFromUserAsync(Guid userId, IEnumerable<Guid> functionIds)
    {
        var functionIdList = functionIds.ToList();
        if (functionIdList.Count == 0)
        {
            return Ok("No functions to remove");
        }

        // 回收虽不授出新权限,但支配约束仍适用——弱管理员不得削减强用户的直授集。
        var violation = await GetUserGrantViolationAsync(userId);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        await _userFunctionRepository.DeleteAsync(uf =>
            uf.UserId == userId && uf.IsGranted && functionIdList.Contains(uf.FunctionId));

        await InvalidateUserCacheAsync(userId);
        await PublishUserFunctionsChangedAsync(userId, PermissionChangeType.Removed, functionIdList);
        LogInformation("Removed direct functions from user: {UserId}", userId);
        return Ok("Functions removed from user");
    }

    /// <inheritdoc />
    public async Task<Result> SetUserFunctionsAsync(Guid userId, IEnumerable<Guid> functionIds)
    {
        var functionIdList = functionIds.ToList();

        var violation = await GetUserGrantViolationAsync(userId, functionIdList);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        var missing = await FindMissingFunctionsAsync(functionIdList);
        if (missing != null) return missing;

        // 休眠 allow 行（退役 / 禁用功能）在覆盖范围之外：矩阵渲染不到它们，也就没有资格删它们。
        var dormantIds = await GetDormantFunctionIdsAsync(userId);

        // 原子操作：同一 UnitOfWork 中覆盖 allow 集，避免权限窗口期。
        // deny 行不整体清除（deny 集由 SetUserDeniedFunctionsAsync 管理），
        // 但落在新 allow 集内的 deny 行被翻转删除（显式授予 = 后写者赢）。
        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            await _userFunctionRepository.DeleteAsync(uf =>
                uf.UserId == userId && uf.IsGranted && !dormantIds.Contains(uf.FunctionId));

            if (functionIdList.Count > 0)
            {
                await _userFunctionRepository.DeleteAsync(uf =>
                    uf.UserId == userId && !uf.IsGranted && functionIdList.Contains(uf.FunctionId));

                var userFunctions = functionIdList.Select(functionId => new UserFunction
                {
                    UserId = userId,
                    FunctionId = functionId,
                    IsGranted = true,
                    IsEnabled = true
                }).ToList();
                await _userFunctionRepository.InsertManyAsync(userFunctions);
            }

            LogInformation("Set {Count} direct functions for user: {UserId}", functionIdList.Count, userId);
            return Ok($"Set {functionIdList.Count} functions for user");
        });

        if (result.Succeeded)
        {
            await InvalidateUserCacheAsync(userId);
            await PublishUserFunctionsChangedAsync(userId, PermissionChangeType.Reset, functionIdList);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result> SetUserFunctionsInScopeAsync(
        Guid userId, IEnumerable<Guid> scopeFunctionIds, IEnumerable<Guid> functionIds)
    {
        var invalid = UserFunctionScope.Normalize(scopeFunctionIds, functionIds, out var scope, out var functionIdList);
        if (invalid != null) return invalid;

        var violation = await GetUserGrantViolationAsync(userId, functionIdList);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        var missing = await FindMissingFunctionsAsync(functionIdList);
        if (missing != null) return missing;

        // 切片声明的是「消费方看得见的范围」，而休眠行在任何矩阵里都不可见 ——
        // 即便切片把那个 id 报了进来，也不能借这次写入把它删掉。
        var dormantIds = await GetDormantFunctionIdsAsync(userId);

        // 与 SetUserFunctionsAsync 逐字同构,唯一差别是 allow 的删除被 scope 夹住——
        // 切片外的 allow 行与 deny 行都碰不到,这正是本方法存在的理由。
        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            await _userFunctionRepository.DeleteAsync(uf =>
                uf.UserId == userId && uf.IsGranted && scope.Contains(uf.FunctionId) && !dormantIds.Contains(uf.FunctionId));

            if (functionIdList.Count > 0)
            {
                await _userFunctionRepository.DeleteAsync(uf =>
                    uf.UserId == userId && !uf.IsGranted && functionIdList.Contains(uf.FunctionId));

                var userFunctions = functionIdList.Select(functionId => new UserFunction
                {
                    UserId = userId,
                    FunctionId = functionId,
                    IsGranted = true,
                    IsEnabled = true
                }).ToList();
                await _userFunctionRepository.InsertManyAsync(userFunctions);
            }

            LogInformation("Set {Count} direct functions for user {UserId} within a scope of {ScopeCount}",
                functionIdList.Count, userId, scope.Count);
            return Ok($"Set {functionIdList.Count} functions for user within the given scope");
        });

        if (result.Succeeded)
        {
            await InvalidateUserCacheAsync(userId);
            // 受影响面 = 整个切片(切片内被删掉的行也变了),而不只是新集。
            await PublishUserFunctionsChangedAsync(userId, PermissionChangeType.Reset, scope);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result> SetUserDeniedFunctionsInScopeAsync(
        Guid userId, IEnumerable<Guid> scopeFunctionIds, IEnumerable<Guid> functionIds)
    {
        var invalid = UserFunctionScope.Normalize(scopeFunctionIds, functionIds, out var scope, out var functionIdList);
        if (invalid != null) return invalid;

        var violation = await GetUserGrantViolationAsync(userId, functionIdList, action: "deny");
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        var missing = await FindMissingFunctionsAsync(functionIdList);
        if (missing != null) return missing;

        var dormantIds = await GetDormantFunctionIdsAsync(userId);

        // 与 SetUserDeniedFunctionsAsync 同构,deny 的删除被 scope 夹住（休眠行同样除外）。
        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            await _userFunctionRepository.DeleteAsync(uf =>
                uf.UserId == userId && !uf.IsGranted && scope.Contains(uf.FunctionId) && !dormantIds.Contains(uf.FunctionId));

            if (functionIdList.Count > 0)
            {
                await _userFunctionRepository.DeleteAsync(uf =>
                    uf.UserId == userId && uf.IsGranted && functionIdList.Contains(uf.FunctionId));

                var userFunctions = functionIdList.Select(functionId => new UserFunction
                {
                    UserId = userId,
                    FunctionId = functionId,
                    IsGranted = false,
                    IsEnabled = true
                }).ToList();
                await _userFunctionRepository.InsertManyAsync(userFunctions);
            }

            LogInformation("Set {Count} denied functions for user {UserId} within a scope of {ScopeCount}",
                functionIdList.Count, userId, scope.Count);
            return Ok($"Set {functionIdList.Count} denied functions for user within the given scope");
        });

        if (result.Succeeded)
        {
            await InvalidateUserCacheAsync(userId);
            await PublishUserFunctionsChangedAsync(userId, PermissionChangeType.Reset, scope);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result> SetUserDeniedFunctionsAsync(Guid userId, IEnumerable<Guid> functionIds)
    {
        var functionIdList = functionIds.ToList();

        // deny 是削权而非授权,但支配约束与"只能触碰自己持有的码"同样适用
        // （不能替权限高于自己的用户做减法,也不能对自己够不着的面做减法）。
        var violation = await GetUserGrantViolationAsync(userId, functionIdList, action: "deny");
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        var missing = await FindMissingFunctionsAsync(functionIdList);
        if (missing != null) return missing;

        var dormantIds = await GetDormantFunctionIdsAsync(userId);

        // 原子操作：覆盖 deny 集（休眠 deny 行除外）。落在新 deny 集内的 allow 行被翻转删除
        // （显式拒绝 = 后写者赢）；其余 allow 行不受影响。
        var result = await ExecuteInUnitOfWorkAsync(async _ =>
        {
            await _userFunctionRepository.DeleteAsync(uf =>
                uf.UserId == userId && !uf.IsGranted && !dormantIds.Contains(uf.FunctionId));

            if (functionIdList.Count > 0)
            {
                await _userFunctionRepository.DeleteAsync(uf =>
                    uf.UserId == userId && uf.IsGranted && functionIdList.Contains(uf.FunctionId));

                var userFunctions = functionIdList.Select(functionId => new UserFunction
                {
                    UserId = userId,
                    FunctionId = functionId,
                    IsGranted = false,
                    IsEnabled = true
                }).ToList();
                await _userFunctionRepository.InsertManyAsync(userFunctions);
            }

            LogInformation("Set {Count} denied functions for user: {UserId}", functionIdList.Count, userId);
            return Ok($"Set {functionIdList.Count} denied functions for user");
        });

        if (result.Succeeded)
        {
            await InvalidateUserCacheAsync(userId);
            await PublishUserFunctionsChangedAsync(userId, PermissionChangeType.Reset, functionIdList);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result> ClearUserFunctionsAsync(Guid userId)
    {
        // 清空同样受支配约束——弱管理员不得清空强用户的直授集。
        var violation = await GetUserGrantViolationAsync(userId);
        if (violation != null)
        {
            return Fail(violation, 403, ErrorCodes.FORBIDDEN);
        }

        // 只清 allow 集;deny 集经 SetUserDeniedFunctionsAsync(userId, []) 清空。
        await _userFunctionRepository.DeleteAsync(uf => uf.UserId == userId && uf.IsGranted);

        await InvalidateUserCacheAsync(userId);
        await PublishUserFunctionsChangedAsync(userId, PermissionChangeType.Cleared, []);
        LogInformation("Cleared all direct functions for user: {UserId}", userId);
        return Ok("Cleared all functions for user");
    }

    /// <summary>
    /// 校验功能 ID 均存在且<b>当前生效</b>（启用且未退役）；越界时返回 404 Result，否则 null。
    /// </summary>
    /// <remarks>
    /// 与权限解析同一判据。此前只查 <c>IsEnabled</c>：给用户直授一个退役码会 200、行落库、
    /// 读回可见、矩阵显示已勾选，而 <c>CheckPermissionAsync</c> 永远答 false —— 写了没人读。
    /// </remarks>
    private async Task<Result?> FindMissingFunctionsAsync(IReadOnlyCollection<Guid> functionIdList)
    {
        var notGrantable = await FunctionLiveness.DescribeNotGrantableAsync(_moduleFunctionRepository, functionIdList);
        return notGrantable != null
            ? Fail(notGrantable, 404, ErrorCodes.RESOURCE_NOT_FOUND)
            : null;
    }

    /// <summary>当前生效的功能（启用且未退役）—— 读路径与写路径共用的那一份判据。</summary>
    private IQueryable<ModuleFunction> LiveFunctions()
        => _moduleFunctionRepository.Where(f => f.IsEnabled && !f.IsRetired);

    /// <summary>该用户的直授行里，指向退役 / 已禁用功能的那些 —— 覆盖写入的禁区。</summary>
    private Task<List<Guid>> GetDormantFunctionIdsAsync(Guid userId)
        => FunctionLiveness.GetDormantFunctionIdsAsync(
            _moduleFunctionRepository, _userFunctionRepository.Where(uf => uf.UserId == userId), uf => uf.FunctionId);

    /// <summary>
    /// 用户直授写路径的委托护栏，与角色路径同构（权限集包含支配）。
    /// 非超管授权者：①不能操作超管用户的直授行（对超管无效但会造成
    /// "看似可削权"的误导，且只有超管才应触碰超管的配置）；②仅能操作
    /// "直授配置涉及的码（allow 与 deny）⊆ 自己有效权限集" 的用户；
    /// ③仅能授出/拒绝自己持有的权限码。
    /// 允许时返回 null，越界时返回英文错误消息（调用方包装为 403）。
    /// 无用户上下文（系统/播种/内部路径与单元测试）时整体跳过。
    /// </summary>
    private async Task<string?> GetUserGrantViolationAsync(
        Guid targetUserId, IReadOnlyCollection<Guid>? functionIdsToTouch = null, string action = "grant")
    {
        var grantorId = CurrentUser?.Id;
        if (grantorId == null || grantorId == Guid.Empty) return null;
        if (await _functionAuthorizationService.IsSuperAdminAsync(grantorId.Value)) return null;

        if (await _functionAuthorizationService.IsSuperAdminAsync(targetUserId))
        {
            return "You cannot manage direct grants of a super administrator.";
        }

        var grantorCodes = new HashSet<string>(
            await _functionAuthorizationService.GetUserPermissionNamesAsync(grantorId.Value),
            StringComparer.OrdinalIgnoreCase);

        // 支配集只算生效的码：退役码在谁的有效集里都没有，算进来会让目标身上一条休眠直授
        // 把每个非超管授权者都锁在外面（角色侧 GetRoleGrantedCodesAsync 同规）。
        var targetCodes = await _userFunctionRepository
            .Where(uf => uf.UserId == targetUserId && uf.IsEnabled)
            .Join(LiveFunctions(), uf => uf.FunctionId, f => f.Id, (uf, f) => f.Code)
            .Distinct()
            .ToListAsync();
        if (!targetCodes.All(grantorCodes.Contains))
        {
            return "You cannot manage this user's direct grants: their granted set is not contained in yours.";
        }

        if (functionIdsToTouch is { Count: > 0 })
        {
            var idList = functionIdsToTouch.ToList();
            var requestedCodes = await _moduleFunctionRepository
                .Where(f => idList.Contains(f.Id))
                .Select(f => f.Code)
                .ToListAsync();
            var exceeded = requestedCodes
                .Where(c => !grantorCodes.Contains(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (exceeded.Count > 0)
            {
                return $"You cannot {action} permissions you do not hold: {string.Join(", ", exceeded)}";
            }
        }

        return null;
    }

    /// <summary>
    /// 用户直授变更只影响单个用户——精确失效其权限缓存即可，
    /// 比角色路径（失效角色全体成员）成本更低。
    /// </summary>
    private async Task InvalidateUserCacheAsync(Guid userId)
    {
        if (_functionAuthCache == null) return;
        try
        {
            await _functionAuthCache.RemoveUserPermissionNamesAsync(userId);
        }
        catch (Exception ex)
        {
            // 缓存失效失败不应影响主业务流程，权限缓存会在过期后自动刷新
            Logger.LogWarning(ex, "Failed to invalidate permission cache for user {UserId}", userId);
        }
    }

    /// <summary>
    /// 发布用户直授变更事件
    /// </summary>
    private async Task PublishUserFunctionsChangedAsync(Guid userId, PermissionChangeType changeType, List<Guid> functionIds)
    {
        if (EventBus == null) return;
        try
        {
            await EventBus.PublishAsync(new UserFunctionsChangedEvent
            {
                UserId = userId,
                ChangeType = changeType,
                AffectedFunctionIds = functionIds
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to publish UserFunctionsChangedEvent for user {UserId}", userId);
        }
    }
}
