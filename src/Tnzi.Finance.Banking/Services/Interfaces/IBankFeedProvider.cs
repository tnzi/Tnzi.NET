namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 银行 feed 提供者契约（框架不内置实现；应用按 <see cref="Key"/> 注册后可拉取流水）
/// </summary>
/// <remarks>
/// 对齐 <see cref="IExchangeRateProvider"/>：经 <c>IEnumerable&lt;IBankFeedProvider&gt;</c> 注入，
/// 服务按银行账户档案的 <c>FeedProviderKey</c> 查找匹配的 <see cref="Key"/>，未注册返回 400。
/// </remarks>
public interface IBankFeedProvider
{
    /// <summary>提供者标识（与 BankAccount.FeedProviderKey 匹配）</summary>
    string Key { get; }

    /// <summary>拉取自 <c>Cursor</c>/<c>Since</c> 以来的流水</summary>
    Task<BankFeedPullResult> PullAsync(BankFeedPullRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 银行 feed 拉取请求
/// </summary>
public record BankFeedPullRequest(Guid BankAccountId, string? ExternalAccountId, string? Cursor, DateTime? Since);

/// <summary>
/// 银行 feed 拉取结果
/// </summary>
public record BankFeedPullResult(IReadOnlyList<BankFeedTransaction> Transactions, string? NextCursor, decimal? LedgerBalance);

/// <summary>
/// 银行 feed 单条流水
/// </summary>
public record BankFeedTransaction(DateTime PostedDate, decimal Amount, string Currency, string ExternalId,
    string? Description = null, string? Payee = null, string? Reference = null);
