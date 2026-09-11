namespace Tnzi.Authorization.Services;

/// <summary>
/// 「功能当前是否生效」的唯一判据，以及围绕它的两个共用查询。
/// </summary>
/// <remarks>
/// <para>
/// 权限解析只读<b>启用且未退役</b>的功能（<see cref="FunctionAuthorizationService"/> 的
/// <c>GetExplicitUserPermissionNamesAsync</c>）。写路径必须按同一份判据校验，否则就会出现
/// 「写入成功、界面显示已授予、运行时永不生效」：一条授权行落在一个没人读的功能上。
/// </para>
/// <para>
/// 指向不生效功能的既有授权行叫<b>休眠授权</b>。它们不是垃圾：退役是「宿主没加载那个模块」
/// 的正常产物，禁用是管理员的临时开关，两者结束时授权都要原样回来。
/// 因此覆盖写入（set / set-in-scope）要绕开它们 —— 矩阵只渲染生效项，休眠行在矩阵够不到的地方，
/// 顺手删掉等于把 <c>PermissionRetirementMode.Disable</c> 承诺的「保留」偷换成「清空」。
/// </para>
/// </remarks>
internal static class FunctionLiveness
{
    /// <summary>
    /// 校验一批待写入的功能 id 全部存在且当前生效。
    /// </summary>
    /// <returns>
    /// 全部合法时返回 <c>null</c>；否则返回一条英文说明，区分「不存在」（列 id）与
    /// 「存在但未生效」（列<b>码</b> —— 管理员要靠码才知道该去哪里修，一串 guid 修不了）。
    /// 调用方包装成 404。
    /// </returns>
    internal static async Task<string?> DescribeNotGrantableAsync(
        IRepository<ModuleFunction, Guid> functions, IReadOnlyCollection<Guid> functionIds)
    {
        Check.NotNull(functions);
        if (functionIds.Count == 0) return null;

        var idList = functionIds.Distinct().ToList();
        var rows = await functions
            .Where(f => idList.Contains(f.Id))
            .Select(f => new { f.Id, f.Code, IsLive = f.IsEnabled && !f.IsRetired })
            .ToListAsync();

        var known = rows.Select(r => r.Id).ToHashSet();
        var missing = idList.Where(id => !known.Contains(id)).ToList();
        var dormant = rows.Where(r => !r.IsLive).Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal).ToList();

        if (missing.Count == 0 && dormant.Count == 0) return null;

        var parts = new List<string>(2);
        if (missing.Count > 0)
        {
            parts.Add($"Functions not found: {string.Join(", ", missing)}");
        }

        if (dormant.Count > 0)
        {
            parts.Add($"Functions not currently in effect (retired or disabled) cannot be granted: {string.Join(", ", dormant)}");
        }

        return string.Join(". ", parts);
    }

    /// <summary>
    /// 在一批既有授权行里找出<b>休眠</b>的那些（指向已退役或已禁用功能）的功能 id。
    /// </summary>
    /// <typeparam name="TGrant">授权行类型（<see cref="RoleFunction"/> / <see cref="UserFunction"/>）。</typeparam>
    /// <param name="functions">功能仓储。</param>
    /// <param name="grants">已按角色 / 用户过滤好的授权行查询。</param>
    /// <param name="functionId">授权行上的功能 id 选择器。</param>
    /// <remarks>
    /// 单次 join 查询；结果通常为空或个位数，调用方拿它做 <c>!Contains</c> 排除，
    /// 比反向列出全部生效 id 便宜得多（一个角色可能有几百条生效授权）。
    /// </remarks>
    internal static Task<List<Guid>> GetDormantFunctionIdsAsync<TGrant>(
        IRepository<ModuleFunction, Guid> functions,
        IQueryable<TGrant> grants,
        Expression<Func<TGrant, Guid>> functionId)
    {
        Check.NotNull(functions);
        Check.NotNull(grants);
        Check.NotNull(functionId);

        var dormantFunctions = functions.Where(f => f.IsRetired || !f.IsEnabled);
        return grants
            .Join(dormantFunctions, functionId, f => f.Id, (_, f) => f.Id)
            .Distinct()
            .ToListAsync();
    }
}
