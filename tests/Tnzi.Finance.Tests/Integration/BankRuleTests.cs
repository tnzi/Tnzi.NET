namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 银行规则：条件求值、首个命中者胜、与匹配引擎的分工、AutoApply 自动入账。
/// </summary>
public class BankRuleTests : FinanceIntegrationTestBase
{
    private Task<Result<BankRuleDto>> RuleAsync(Func<IBankRuleService, Task<Result<BankRuleDto>>> action)
        => InScopeAsync<IBankRuleService, Result<BankRuleDto>>(action);

    private async Task<Guid> BankFundsAccountAsync()
    {
        await SeedCoaAsync();
        return await AccountIdByCodeAsync("1110");
    }

    private static CreateBankRuleDto Rule(
        string name,
        string? descriptionContains = null,
        BankRuleDirection direction = BankRuleDirection.Any,
        Guid? counterAccountId = null,
        bool autoApply = false,
        int? priority = null,
        BankRuleMatchMode mode = BankRuleMatchMode.All,
        List<CreateBankRuleConditionDto>? conditions = null)
        => new()
        {
            Name = name,
            Priority = priority,
            Direction = direction,
            MatchMode = mode,
            DocType = BankFeedDocType.Expense,
            CounterAccountId = counterAccountId,
            AutoApply = autoApply,
            Conditions = conditions ?? (descriptionContains == null
                ? new List<CreateBankRuleConditionDto>()
                : new List<CreateBankRuleConditionDto>
                {
                    new() { Field = BankRuleField.Description, Operator = BankRuleOperator.Contains, Value = descriptionContains }
                }),
        };

    /// <summary>直接建一条待匹配流水（跳过导入解析，本套测试关心的是规则不是解析）。</summary>
    private async Task<Guid> PendingTransactionAsync(
        Guid accountId, decimal amount, string? description = null, string? payee = null, string? reference = null)
    {
        return await InScopeAsync<IServiceProvider, Guid>(async sp =>
        {
            var repo = sp.GetRequiredService<IRepository<BankTransaction, Guid>>();
            var txn = new BankTransaction
            {
                AccountId = accountId,
                ImportBatchId = Guid.NewGuid(),
                TxnDate = DateTime.UtcNow.Date,
                Amount = amount,
                Currency = "USD",
                Description = description,
                Payee = payee,
                Reference = reference,
                ExternalId = $"probe:{Guid.NewGuid():N}",
                Source = BankTransactionSource.Csv,
                Status = BankTransactionStatus.Pending,
            };
            await repo.InsertAsync(txn);
            await repo.SaveChangesAsync();
            return txn.Id;
        });
    }

    private Task<BankRuleMatch?> EvaluateAsync(Guid transactionId)
        => InScopeAsync<IServiceProvider, BankRuleMatch?>(async sp =>
        {
            var txn = await sp.GetRequiredService<IReadOnlyRepository<BankTransaction, Guid>>()
                .FirstOrDefaultAsync(t => t.Id == transactionId);
            return await sp.GetRequiredService<IBankRuleEvaluator>().EvaluateAsync(txn!);
        });

    [Fact]
    public async Task Evaluate_NoRules_ReturnsNull()
    {
        var account = await BankFundsAccountAsync();
        var txn = await PendingTransactionAsync(account, -12.40m, "STARBUCKS #1234");

        (await EvaluateAsync(txn)).ShouldBeNull();
    }

    [Fact]
    public async Task Evaluate_DescriptionContains_MatchesCaseInsensitively()
    {
        var account = await BankFundsAccountAsync();
        var meals = await AccountIdByCodeAsync("5200");
        (await RuleAsync(s => s.CreateAsync(Rule("Coffee", "starbucks", counterAccountId: meals)))).Succeeded.ShouldBeTrue();

        // 对账单上的商户名大小写随银行心情变，让人为此写两条规则是荒谬的。
        var txn = await PendingTransactionAsync(account, -12.40m, "STARBUCKS #1234");
        var match = await EvaluateAsync(txn);

        match.ShouldNotBeNull();
        match!.Value.RuleName.ShouldBe("Coffee");
        match.Value.CounterAccountId.ShouldBe(meals);
    }

    /// <summary>
    /// ★首个命中者胜（QuickBooks 语义）：不合并、不投票，顺序是操作员看得见的。
    /// </summary>
    [Fact]
    public async Task Evaluate_FirstMatchWins_ByPriority()
    {
        var account = await BankFundsAccountAsync();
        var meals = await AccountIdByCodeAsync("5200");
        var cogs = await AccountIdByCodeAsync("5100");

        (await RuleAsync(s => s.CreateAsync(Rule("Broad", "starbucks", counterAccountId: cogs, priority: 20)))).Succeeded.ShouldBeTrue();
        (await RuleAsync(s => s.CreateAsync(Rule("Specific", "starbucks #1234", counterAccountId: meals, priority: 10)))).Succeeded.ShouldBeTrue();

        var txn = await PendingTransactionAsync(account, -12.40m, "STARBUCKS #1234");
        var match = await EvaluateAsync(txn);

        match!.Value.RuleName.ShouldBe("Specific");
        match.Value.CounterAccountId.ShouldBe(meals);
    }

    /// <summary>
    /// ★方向是独立维度：退款进来时，"星巴克 → 餐饮费" 不该把它记成一笔餐饮费。
    /// </summary>
    [Fact]
    public async Task Evaluate_DirectionGate_ExcludesTheOppositeSign()
    {
        var account = await BankFundsAccountAsync();
        var meals = await AccountIdByCodeAsync("5200");
        (await RuleAsync(s => s.CreateAsync(
            Rule("Coffee out", "starbucks", direction: BankRuleDirection.MoneyOut, counterAccountId: meals)))).Succeeded.ShouldBeTrue();

        var spend = await PendingTransactionAsync(account, -12.40m, "STARBUCKS #1234");
        var refund = await PendingTransactionAsync(account, 12.40m, "STARBUCKS #1234 REFUND");

        (await EvaluateAsync(spend)).ShouldNotBeNull();
        (await EvaluateAsync(refund)).ShouldBeNull();
    }

    [Fact]
    public async Task Evaluate_AllVersusAny()
    {
        var account = await BankFundsAccountAsync();
        var conditions = new List<CreateBankRuleConditionDto>
        {
            new() { Field = BankRuleField.Description, Operator = BankRuleOperator.Contains, Value = "uber" },
            new() { Field = BankRuleField.Amount, Operator = BankRuleOperator.GreaterThan, Value = "50" },
        };

        (await RuleAsync(s => s.CreateAsync(Rule("All", conditions: conditions, mode: BankRuleMatchMode.All, priority: 1)))).Succeeded.ShouldBeTrue();

        var small = await PendingTransactionAsync(account, -18m, "UBER TRIP");
        (await EvaluateAsync(small)).ShouldBeNull();

        var big = await PendingTransactionAsync(account, -74m, "UBER TRIP");
        (await EvaluateAsync(big))!.Value.RuleName.ShouldBe("All");

        // 同样两个条件改成 Any，小额那笔就该命中。
        var anyRule = await RuleAsync(s => s.CreateAsync(Rule("Any", conditions: conditions, mode: BankRuleMatchMode.Any, priority: 2)));
        anyRule.Succeeded.ShouldBeTrue();
        await RuleAsync(s => s.UpdateAsync(anyRule.Data!.Id, Rule("Any", conditions: conditions, mode: BankRuleMatchMode.Any, priority: 0)));

        (await EvaluateAsync(small))!.Value.RuleName.ShouldBe("Any");
    }

    /// <summary>金额比较取绝对值：方向由 Direction 表达，不用在阈值里想符号。</summary>
    [Fact]
    public async Task Evaluate_AmountComparesAbsoluteValue()
    {
        var account = await BankFundsAccountAsync();
        (await RuleAsync(s => s.CreateAsync(Rule("Large", conditions: new List<CreateBankRuleConditionDto>
        {
            new() { Field = BankRuleField.Amount, Operator = BankRuleOperator.GreaterThan, Value = "1000" },
        })))).Succeeded.ShouldBeTrue();

        var bigSpend = await PendingTransactionAsync(account, -2500m, "WIRE OUT");
        (await EvaluateAsync(bigSpend)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Evaluate_DisabledRuleIsIgnored()
    {
        var account = await BankFundsAccountAsync();
        var created = await RuleAsync(s => s.CreateAsync(Rule("Coffee", "starbucks")));
        var input = Rule("Coffee", "starbucks");
        input.IsEnabled = false;
        await RuleAsync(s => s.UpdateAsync(created.Data!.Id, input));

        var txn = await PendingTransactionAsync(account, -12.40m, "STARBUCKS");
        (await EvaluateAsync(txn)).ShouldBeNull();
    }

    [Fact]
    public async Task Evaluate_AccountScopedRuleDoesNotLeakToOtherAccounts()
    {
        var account = await BankFundsAccountAsync();
        var other = await AccountIdByCodeAsync("1120");
        var scoped = Rule("Scoped", "starbucks");
        scoped.AccountId = other;
        (await RuleAsync(s => s.CreateAsync(scoped))).Succeeded.ShouldBeTrue();

        var txn = await PendingTransactionAsync(account, -12.40m, "STARBUCKS");
        (await EvaluateAsync(txn)).ShouldBeNull();
    }

    // ── 校验 ────────────────────────────────────────────────

    [Fact]
    public async Task Create_MismatchedOperatorAndField_Rejected400()
    {
        await SeedCoaAsync();

        // "大于" 用在摘要上会静默地永不命中——比直接拒绝难查得多。
        var bad = Rule("Bad", conditions: new List<CreateBankRuleConditionDto>
        {
            new() { Field = BankRuleField.Description, Operator = BankRuleOperator.GreaterThan, Value = "10" },
        });
        var result = await RuleAsync(s => s.CreateAsync(bad));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_AmountConditionWithNonNumericValue_Rejected400()
    {
        await SeedCoaAsync();
        var bad = Rule("Bad", conditions: new List<CreateBankRuleConditionDto>
        {
            new() { Field = BankRuleField.Amount, Operator = BankRuleOperator.GreaterThan, Value = "lots" },
        });

        (await RuleAsync(s => s.CreateAsync(bad))).Code.ShouldBe(400);
    }

    /// <summary>自动入账的规则必须自己说得清钱记到哪儿。</summary>
    [Fact]
    public async Task Create_AutoApplyWithoutAnAccount_Rejected400()
    {
        await SeedCoaAsync();
        var bad = Rule("Auto", "rent", autoApply: true);

        var result = await RuleAsync(s => s.CreateAsync(bad));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_AppendsToTheEndOfThePriorityOrder()
    {
        await SeedCoaAsync();
        var first = await RuleAsync(s => s.CreateAsync(Rule("First", "a")));
        var second = await RuleAsync(s => s.CreateAsync(Rule("Second", "b")));

        // 新规则排到末尾：它不该悄悄抢走既有规则的流水。
        second.Data!.Priority.ShouldBeGreaterThan(first.Data!.Priority);
    }

    [Fact]
    public async Task Reorder_RewritesPrioritiesInTheGivenOrder()
    {
        await SeedCoaAsync();
        var a = await RuleAsync(s => s.CreateAsync(Rule("A", "a")));
        var b = await RuleAsync(s => s.CreateAsync(Rule("B", "b")));

        var reordered = await InScopeAsync<IBankRuleService, Result>(
            s => s.ReorderAsync(new ReorderBankRulesDto { RuleIds = [b.Data!.Id, a.Data!.Id] }));
        reordered.Succeeded.ShouldBeTrue(reordered.Message);

        var page = await InScopeAsync<IBankRuleService, Result<IPagedList<BankRuleDto>>>(
            s => s.GetPagedAsync(new BankRuleQueryDto { PageIndex = 1, PageSize = 50 }));

        page.Data!.Items[0].Name.ShouldBe("B");
        page.Data.Items[1].Name.ShouldBe("A");
    }

    /// <summary>
    /// 只提交其中几条时，其余规则的位置不许被踩。
    /// </summary>
    /// <remarks>
    /// ★界面上的上移/下移只看得见当前这一页，所以提交上来的往往是子集。
    /// 把子集重编成 1..N 会与页外规则的既有号相撞；而求值是"按 Priority 再按创建时间、
    /// 首个命中者胜"，打平之后谁先谁后由创建时间决定 —— 一条规则就此悄悄抢走另一条的
    /// 流水，而操作员看到的是自己刚拖出来的顺序。
    /// </remarks>
    [Fact]
    public async Task Reorder_WithASubset_LeavesTheOtherRulesWhereTheyWere()
    {
        await SeedCoaAsync();
        var a = await RuleAsync(s => s.CreateAsync(Rule("A", "a")));
        await RuleAsync(s => s.CreateAsync(Rule("B", "b")));
        var c = await RuleAsync(s => s.CreateAsync(Rule("C", "c")));
        await RuleAsync(s => s.CreateAsync(Rule("D", "d")));

        // 只把 C 和 A 的相对次序调过来（C 原在第 3 位、A 原在第 1 位）。
        var reordered = await InScopeAsync<IBankRuleService, Result>(
            s => s.ReorderAsync(new ReorderBankRulesDto { RuleIds = [c.Data!.Id, a.Data!.Id] }));
        reordered.Succeeded.ShouldBeTrue(reordered.Message);

        var page = await InScopeAsync<IBankRuleService, Result<IPagedList<BankRuleDto>>>(
            s => s.GetPagedAsync(new BankRuleQueryDto { PageIndex = 1, PageSize = 50 }));

        // C 顶到 A 原来的位置，A 落到 C 原来的位置；B / D 原地不动。
        page.Data!.Items.Select(i => i.Name).ShouldBe(["C", "B", "A", "D"]);

        // 而且没有任何两条规则并列 —— 打平就等于把顺序交给创建时间。
        var priorities = page.Data.Items.Select(i => i.Priority).ToList();
        priorities.Distinct().Count().ShouldBe(priorities.Count);
    }

    /// <summary>
    /// ★试跑报的是"这些流水最终归谁"，不是"我这条规则能匹配什么"。
    /// </summary>
    /// <remarks>
    /// 首个命中者胜意味着一条更高优先级的规则可能把它们全都抢走——那正是操作员
    /// 在保存之前需要看见的事实。
    /// </remarks>
    [Fact]
    public async Task Test_ReportsTheRuleThatActuallyWins()
    {
        var account = await BankFundsAccountAsync();
        var broad = await RuleAsync(s => s.CreateAsync(Rule("Broad", "starbucks", priority: 20)));
        await RuleAsync(s => s.CreateAsync(Rule("Specific", "starbucks", priority: 10)));
        await PendingTransactionAsync(account, -12.40m, "STARBUCKS #1234");

        var test = await InScopeAsync<IBankRuleService, Result<BankRuleTestResultDto>>(
            s => s.TestAsync(broad.Data!.Id, new TestBankRuleDto()));

        test.Succeeded.ShouldBeTrue(test.Message);
        test.Data!.Matched.ShouldBe(1);
        test.Data.Rows[0].WinningRuleName.ShouldBe("Specific");
    }

    [Fact]
    public async Task Delete_RemovesTheRuleAndItsConditions()
    {
        var account = await BankFundsAccountAsync();
        var created = await RuleAsync(s => s.CreateAsync(Rule("Coffee", "starbucks")));

        (await InScopeAsync<IBankRuleService, Result>(s => s.DeleteAsync(created.Data!.Id))).Succeeded.ShouldBeTrue();

        var txn = await PendingTransactionAsync(account, -12.40m, "STARBUCKS");
        (await EvaluateAsync(txn)).ShouldBeNull();
    }
}
