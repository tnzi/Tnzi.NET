namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 内置银行规则求值器：字段 + 运算符 + 首个命中者胜
/// </summary>
/// <remarks>
/// 规则在内存里求值而不是下推 SQL：条件是"摘要包含 X 且金额 &gt; Y"这种跨行的
/// 组合谓词，翻成 SQL 要么拼字符串、要么对每条规则发一次查询；而规则表是**每租户
/// 几十条**量级的运维数据，一次性读出来在内存里跑，既简单又可解释。
///
/// 无副作用（见 <see cref="IBankRuleEvaluator"/>）：只回答"这笔流水像什么"。
/// </remarks>
public class BankRuleEvaluator : IBankRuleEvaluator
{
    private readonly IReadOnlyRepository<BankRule, Guid> _repository;

    public BankRuleEvaluator(IReadOnlyRepository<BankRule, Guid> repository)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<BankRuleMatch?> EvaluateAsync(BankTransaction transaction, CancellationToken cancellationToken = default)
    {
        Check.NotNull(transaction);

        var rules = await LoadAsync(transaction.AccountId, cancellationToken);
        return FirstMatch(rules, transaction);
    }

    public async Task<IReadOnlyDictionary<Guid, BankRuleMatch>> EvaluateManyAsync(
        IReadOnlyCollection<BankTransaction> transactions, CancellationToken cancellationToken = default)
    {
        Check.NotNull(transactions);
        if (transactions.Count == 0)
            return new Dictionary<Guid, BankRuleMatch>();

        // 一次读全部相关规则：批量求值的常见入口是"整个账户重跑建议"，
        // 逐条查规则表会把 N 条流水变成 N 次往返。
        var accountIds = transactions.Select(t => t.AccountId).Distinct().ToList();
        var rules = await LoadAsync(accountIds, cancellationToken);

        var result = new Dictionary<Guid, BankRuleMatch>();
        foreach (var txn in transactions)
        {
            var applicable = rules.Where(r => r.AccountId == null || r.AccountId == txn.AccountId).ToList();
            var match = FirstMatch(applicable, txn);
            if (match.HasValue)
                result[txn.Id] = match.Value;
        }

        return result;
    }

    private Task<List<BankRule>> LoadAsync(Guid accountId, CancellationToken cancellationToken)
        => _repository.AsNoTracking()
            .Include(r => r.Conditions)
            .Where(r => r.IsEnabled && (r.AccountId == null || r.AccountId == accountId))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreationTime)
            .ToListAsync(cancellationToken);

    private Task<List<BankRule>> LoadAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
        => _repository.AsNoTracking()
            .Include(r => r.Conditions)
            .Where(r => r.IsEnabled && (r.AccountId == null || accountIds.Contains(r.AccountId.Value)))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreationTime)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// 首个命中者胜（QuickBooks 语义）。
    /// </summary>
    /// <remarks>
    /// 不做多规则合并：两条规则给出不同科目时，合并出来的结果是谁也说不清钱去了
    /// 哪儿；而顺序至少是操作员看得见、调得动的。
    /// </remarks>
    internal static BankRuleMatch? FirstMatch(IReadOnlyList<BankRule> rules, BankTransaction txn)
    {
        foreach (var rule in rules)
        {
            if (!DirectionMatches(rule.Direction, txn.Amount))
                continue;
            if (!ConditionsMatch(rule, txn))
                continue;

            return new BankRuleMatch(
                rule.Id, rule.Name, rule.DocType, rule.CounterAccountId, rule.PartyId, rule.PaymentMethod, rule.AutoApply);
        }

        return null;
    }

    private static bool DirectionMatches(BankRuleDirection direction, decimal amount) => direction switch
    {
        BankRuleDirection.MoneyIn => amount > 0,
        BankRuleDirection.MoneyOut => amount < 0,
        _ => true,
    };

    private static bool ConditionsMatch(BankRule rule, BankTransaction txn)
    {
        // 无条件的规则只靠方向/账户限定生效——那是合法的"兜底规则"，
        // 但它必须真的命中，而不是被当成"永远不匹配"悄悄跳过。
        if (rule.Conditions.Count == 0)
            return true;

        var ordered = rule.Conditions.OrderBy(c => c.LineNumber);
        return rule.MatchMode == BankRuleMatchMode.Any
            ? ordered.Any(c => Matches(c, txn))
            : ordered.All(c => Matches(c, txn));
    }

    private static bool Matches(BankRuleCondition condition, BankTransaction txn)
    {
        if (condition.Field == BankRuleField.Amount)
            return MatchesAmount(condition, txn.Amount);

        var actual = condition.Field switch
        {
            BankRuleField.Description => txn.Description,
            BankRuleField.Payee => txn.Payee,
            BankRuleField.Reference => txn.Reference,
            _ => null,
        };

        return MatchesText(condition, actual);
    }

    /// <summary>
    /// 文本比较一律不分大小写：对账单上的商户名大小写随银行心情变，
    /// 让操作员为此写两条规则是荒谬的。
    /// </summary>
    private static bool MatchesText(BankRuleCondition condition, string? actual)
    {
        var expected = condition.Value ?? string.Empty;

        // 字段为空时，只有 NotContains 成立——"没有摘要"确实不包含任何东西。
        if (string.IsNullOrEmpty(actual))
            return condition.Operator == BankRuleOperator.NotContains;

        return condition.Operator switch
        {
            BankRuleOperator.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            BankRuleOperator.NotContains => !actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            BankRuleOperator.Equals => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            BankRuleOperator.StartsWith => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            BankRuleOperator.EndsWith => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            // 数值运算符用在文本字段上无意义：判否而不是抛错——一条配错的规则
            // 不该让整批流水的建议计算失败。
            _ => false,
        };
    }

    /// <summary>
    /// 金额比较取**绝对值**：方向由 <see cref="BankRuleDirection"/> 表达，
    /// 让操作员在"大于 50"里再去想符号是自找的麻烦。
    /// </summary>
    private static bool MatchesAmount(BankRuleCondition condition, decimal amount)
    {
        if (!decimal.TryParse(condition.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var expected))
            return false;

        var actual = Math.Abs(amount);
        var target = Math.Abs(expected);

        return condition.Operator switch
        {
            BankRuleOperator.Equals => actual == target,
            BankRuleOperator.GreaterThan => actual > target,
            BankRuleOperator.LessThan => actual < target,
            _ => false,
        };
    }
}
