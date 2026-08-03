namespace Tnzi.Authorization.Services;

/// <summary>
/// 用户-功能直接授权服务接口
/// 提供不经角色、直接把功能授予（或拒绝）单个用户的管理操作
/// </summary>
/// <remarks>
/// 权限解析为 <c>(角色授权 ∪ 用户直授) − 用户拒绝</c>（用户级优先）；
/// 本服务管理用户直授的 allow 行与 deny 行，角色授权走
/// <see cref="IRoleFunctionService"/>。所有写操作服从与角色路径相同的
/// 委托授权护栏：非超管授权者仅能授出/拒绝自己持有的权限码，且不能
/// 操作"直授配置不被自己权限集包含"的用户。
/// </remarks>
public interface IUserFunctionService
{
    /// <summary>
    /// 获取用户直接授权的功能列表（仅启用的直授行 × 启用的功能）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能列表</returns>
    Task<Result<IEnumerable<ModuleFunction>>> GetUserFunctionsAsync(Guid userId);

    /// <summary>
    /// 获取用户直接授权的功能ID列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能ID列表</returns>
    Task<Result<IEnumerable<Guid>>> GetUserFunctionIdsAsync(Guid userId);

    /// <summary>
    /// 直接授予功能给用户（增量，已存在的关联跳过）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> AssignFunctionsToUserAsync(Guid userId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 移除用户的直接授权（不影响其经角色获得的权限）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> RemoveFunctionsFromUserAsync(Guid userId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 设置用户的直接授权（覆盖原有直授集；落入新集的 deny 行被翻转删除）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> SetUserFunctionsAsync(Guid userId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 在给定切片内设置用户的直接授权（allow 集）——切片外的直授行原样保留
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="scopeFunctionIds">切片：本次写入允许触碰的功能ID全集</param>
    /// <param name="functionIds">切片内的新 allow 集（必须是切片的子集）</param>
    /// <remarks>
    /// <para>
    /// <see cref="SetUserFunctionsAsync"/> 覆盖的是用户的<b>整个</b> allow 集，只有
    /// "自己掌握全部功能目录"的调用方才用得对。只拥有目录一个子集的消费方
    /// （例如某业务应用只渲染自己那几个 <c>xxx.*</c> 码的权限矩阵）拿它保存子集，
    /// 会把子集之外的直授行连带删光——不报错、不失败，授权就是没了。本方法把
    /// 写入边界显式化：删除只发生在 <paramref name="scopeFunctionIds"/> 之内。
    /// </para>
    /// <para>
    /// 语义与 <see cref="SetUserFunctionsAsync"/> 一致，只是把"全集"换成"切片"：
    /// 切片内不在 <paramref name="functionIds"/> 里的 allow 行被删除；
    /// <paramref name="functionIds"/> 命中的 deny 行被翻转删除（显式授予=后写者赢）；
    /// 切片内其余 deny 行、以及切片外的一切行都不受影响。
    /// </para>
    /// <para>
    /// <paramref name="functionIds"/> 必须是 <paramref name="scopeFunctionIds"/> 的子集，
    /// 否则返回 400——<b>边界由框架强制，而不是由调用方自证</b>。
    /// </para>
    /// <para>
    /// 默认实现是"读-改-写"回退（把切片外的既有 allow 并回来后走整集覆盖），语义正确
    /// 但非原子；<see cref="UserFunctionService"/> 在单个 UnitOfWork 内完成，无窗口期。
    /// </para>
    /// </remarks>
    async Task<Result> SetUserFunctionsInScopeAsync(
        Guid userId, IEnumerable<Guid> scopeFunctionIds, IEnumerable<Guid> functionIds)
    {
        var invalid = UserFunctionScope.Normalize(scopeFunctionIds, functionIds, out var scope, out var ids);
        if (invalid != null) return invalid;

        var current = await GetUserFunctionIdsAsync(userId);
        if (!current.Succeeded) return UserFunctionScope.ToFailure(current);

        return await SetUserFunctionsAsync(userId, UserFunctionScope.Merge(current.Data, scope, ids));
    }

    /// <summary>
    /// 获取用户被否定（deny）的功能ID列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>功能ID列表</returns>
    Task<Result<IEnumerable<Guid>>> GetUserDeniedFunctionIdsAsync(Guid userId);

    /// <summary>
    /// 设置用户的否定权限集（覆盖原有 deny 集；落入新集的 allow 行被翻转
    /// 删除）。deny 行使该用户失去对应权限码——无论哪个角色授予过
    /// （用户级优先，超管不受影响）。传空列表即清空 deny 集。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="functionIds">功能ID列表</param>
    Task<Result> SetUserDeniedFunctionsAsync(Guid userId, IEnumerable<Guid> functionIds);

    /// <summary>
    /// 在给定切片内设置用户的否定权限集（deny 集）——切片外的 deny 行原样保留
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="scopeFunctionIds">切片：本次写入允许触碰的功能ID全集</param>
    /// <param name="functionIds">切片内的新 deny 集（必须是切片的子集）</param>
    /// <remarks>
    /// <see cref="SetUserDeniedFunctionsAsync"/> 的有界版本，与
    /// <see cref="SetUserFunctionsInScopeAsync"/> 对称：切片内不在
    /// <paramref name="functionIds"/> 里的 deny 行被删除；<paramref name="functionIds"/>
    /// 命中的 allow 行被翻转删除（显式拒绝=后写者赢）；切片外的一切行不受影响。
    /// 子集约束、默认实现的非原子性同 <see cref="SetUserFunctionsInScopeAsync"/>。
    /// </remarks>
    async Task<Result> SetUserDeniedFunctionsInScopeAsync(
        Guid userId, IEnumerable<Guid> scopeFunctionIds, IEnumerable<Guid> functionIds)
    {
        var invalid = UserFunctionScope.Normalize(scopeFunctionIds, functionIds, out var scope, out var ids);
        if (invalid != null) return invalid;

        var current = await GetUserDeniedFunctionIdsAsync(userId);
        if (!current.Succeeded) return UserFunctionScope.ToFailure(current);

        return await SetUserDeniedFunctionsAsync(userId, UserFunctionScope.Merge(current.Data, scope, ids));
    }

    /// <summary>
    /// 清空用户的所有直接授权（仅 allow 集；deny 集经
    /// <see cref="SetUserDeniedFunctionsAsync"/> 传空列表清空）
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> ClearUserFunctionsAsync(Guid userId);
}
