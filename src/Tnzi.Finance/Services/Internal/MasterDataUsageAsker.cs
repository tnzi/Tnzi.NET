namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 把"还有别人在用这条主数据吗"这个问题依次问给每个 <see cref="IMasterDataUsageProvider"/>。
/// </summary>
/// <remarks>
/// 三个删除守卫（客户 / 供应商 / 目录项）逐字相同的那几行，抽在这里免得各自漂移。
/// 纯静态、无状态，不进 DI。
/// </remarks>
internal static class MasterDataUsageAsker
{
    /// <summary>
    /// 返回第一个"在用"的答案；全都说没有（或一个实现都没注册）时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 短路在第一个肯定答案上：操作员需要的是"去处理掉那个引用"，而不是一份完整清单。
    /// 一个实现都没注册时循环体一次都不进，行为与引入本契约之前逐字一致。
    /// </remarks>
    public static async Task<MasterDataUsage?> AskAsync(
        IEnumerable<IMasterDataUsageProvider> providers,
        FinanceMasterDataKind kind,
        Guid id,
        CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            var usage = await provider.FindUsageAsync(kind, id, cancellationToken);
            if (usage != null)
                return usage;
        }

        return null;
    }
}
