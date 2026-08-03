namespace Tnzi.Authorization.Services;

/// <summary>
/// 用户直授"有界写入"的共用规则：切片规范化、子集强制、切片外合并。
/// </summary>
/// <remarks>
/// 由 <see cref="IUserFunctionService"/> 的默认实现（读-改-写回退）与
/// <see cref="UserFunctionService"/> 的原子实现共同使用，保证两条路径对
/// "什么算越界"的判定逐字一致。
/// </remarks>
internal static class UserFunctionScope
{
    /// <summary>
    /// 规范化切片与写入集（去重、物化），并强制"写入集 ⊆ 切片"。
    /// </summary>
    /// <param name="scopeFunctionIds">切片：本次写入允许触碰的功能ID全集</param>
    /// <param name="functionIds">切片内的新集合</param>
    /// <param name="scope">输出：去重后的切片</param>
    /// <param name="ids">输出：去重后的写入集</param>
    /// <returns>越界时返回 400 Result（列出越界 id），合法时返回 null</returns>
    internal static Result? Normalize(
        IEnumerable<Guid> scopeFunctionIds,
        IEnumerable<Guid> functionIds,
        out List<Guid> scope,
        out List<Guid> ids)
    {
        scope = Check.NotNull(scopeFunctionIds).Distinct().ToList();
        ids = Check.NotNull(functionIds).Distinct().ToList();

        var scopeSet = scope.ToHashSet();
        var outside = ids.Where(id => !scopeSet.Contains(id)).ToList();
        return outside.Count > 0
            ? Result.Failure(
                $"Function ids outside the declared scope cannot be written: {string.Join(", ", outside)}",
                400, ErrorCodes.VALIDATION_ERROR)
            : null;
    }

    /// <summary>
    /// 把切片外的既有集合并回写入集——供无法做有界删除的回退实现凑出整集。
    /// </summary>
    internal static IEnumerable<Guid> Merge(IEnumerable<Guid>? current, IReadOnlyCollection<Guid> scope, IReadOnlyCollection<Guid> ids)
    {
        var scopeSet = scope.ToHashSet();
        return (current ?? []).Where(id => !scopeSet.Contains(id)).Concat(ids).Distinct();
    }

    /// <summary>
    /// 把读操作的失败原样降级为无数据的失败（保留 Message/Code/ErrorCode）。
    /// </summary>
    internal static Result ToFailure<T>(Result<T> failed) =>
        Result.Failure(failed.Message ?? "Failed to read the user's current direct grants",
            failed.Code ?? 400, failed.ErrorCode);
}
