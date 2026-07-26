namespace Tnzi.Finance.Banking.Services.Interfaces;

/// <summary>
/// 一条流水命中的规则及其动作
/// </summary>
/// <param name="RuleId">命中的规则</param>
/// <param name="RuleName">规则名（供界面说明"为什么这样归类"）</param>
/// <param name="DocType">建议创建的单据类型</param>
/// <param name="CounterAccountId">对方科目</param>
/// <param name="PartyId">往来方</param>
/// <param name="PaymentMethod">结算方式</param>
/// <param name="AutoApply">是否自动建单+过账+确认匹配</param>
public readonly record struct BankRuleMatch(
    Guid RuleId,
    string RuleName,
    BankFeedDocType DocType,
    Guid? CounterAccountId,
    Guid? PartyId,
    string? PaymentMethod,
    bool AutoApply);

/// <summary>
/// 银行规则求值器（<c>TryAddScoped</c> 默认实现，消费应用可整体替换）
/// </summary>
/// <remarks>
/// 这是留给消费应用的扩展点：框架内置的是"字段 + 运算符 + 首个命中者胜"这套
/// 可解释、可审计的规则，够用且操作员看得懂。想要按往来方历史学习、按 MCC 分类、
/// 或接一个模型来判断，替换本接口即可——规则的存储、管理界面与接线都不必重做。
///
/// 求值必须**无副作用**：它只回答"这笔流水像什么"，写库由调用方在自己的工作单元
/// 里做。试跑功能正建立在这一点上。
/// </remarks>
public interface IBankRuleEvaluator
{
    /// <summary>
    /// 对单条流水求值；无命中返回 null。
    /// </summary>
    Task<BankRuleMatch?> EvaluateAsync(BankTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量求值（同一批流水共用一次规则加载）。
    /// </summary>
    /// <remarks>
    /// 默认实现逐条调用 <see cref="EvaluateAsync"/>，既有实现无需改动；内置实现
    /// 覆盖它以避免对每条流水都重新查一遍规则表。
    /// </remarks>
    async Task<IReadOnlyDictionary<Guid, BankRuleMatch>> EvaluateManyAsync(
        IReadOnlyCollection<BankTransaction> transactions, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, BankRuleMatch>();
        foreach (var txn in transactions)
        {
            var match = await EvaluateAsync(txn, cancellationToken);
            if (match.HasValue)
                result[txn.Id] = match.Value;
        }

        return result;
    }
}
