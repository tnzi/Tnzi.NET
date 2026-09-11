namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 建议匹配的候选取回必须是批量的：逐条流水问一次会把一次导入变成 N 次三层反连接查询，
/// 而那 N 次原本还串在同一个物理事务里、全程持有写锁。
/// </summary>
/// <remarks>
/// 这个类替换 DI 里的 <see cref="BankMatchEngine"/> 为一个只做计数的子类 —— 断言「结果对」
/// 挡不住这条回归：逐条查与批量查的<b>结果逐字相同</b>，差别只在往返次数。
/// </remarks>
public class BankMatchBatchingTests : FinanceIntegrationTestBase
{
    private readonly CandidateLoadCounter _counter = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_counter);
        // 后注册者胜：BankFeedService 拿到的是这个计数子类。
        services.AddScoped<BankMatchEngine, CountingBankMatchEngine>();
    }

    private async Task<Guid> BankAsync() => await AccountIdByCodeAsync("1120");

    /// <summary>过账一笔银行分录（净额 &gt; 0 = 存入借记）。</summary>
    private async Task SeedBankLineAsync(Guid bank, decimal net, DateTime date)
    {
        var posted = await PostLedgerAsync(new LedgerPostingRequest
        {
            PostingDate = date,
            SourceType = "Test.Bank",
            SourceId = Guid.NewGuid().ToString("N"),
            Lines =
            [
                new LedgerPostingLine { AccountId = bank, Debit = net },
                new LedgerPostingLine { AccountCode = "3100", Credit = net }
            ]
        });
        posted.Succeeded.ShouldBeTrue(posted.Message);
    }

    private async Task SeedPendingAsync(Guid bank, params decimal[] amounts)
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<BankTransaction, Guid>>();
        var batchId = Guid.NewGuid();
        var rows = amounts.Select((amount, index) => new BankTransaction
        {
            AccountId = bank,
            ImportBatchId = batchId,
            TxnDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            Amount = amount,
            Currency = "USD",
            Description = $"Row {index}",
            ExternalId = $"EXT-{index}-{amount}",
            Source = BankTransactionSource.Csv,
            Status = BankTransactionStatus.Pending
        }).ToList();
        await repo.InsertManyAsync(rows);
        await repo.SaveChangesAsync();
    }

    /// <summary>
    /// ★ 六条待匹配流水 → 候选集只取回<b>一次</b>，不是六次。
    /// </summary>
    [Fact]
    public async Task SuggestMatches_LoadsCandidatesOnce_NotOncePerTransaction()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var date = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        decimal[] amounts = [101m, 102m, 103m, 104m, 105m, 106m];
        foreach (var amount in amounts)
            await SeedBankLineAsync(bank, amount, date);
        await SeedPendingAsync(bank, amounts);

        var result = await InScopeAsync<IBankFeedService, Result<BankSuggestResultDto>>(s => s.SuggestMatchesAsync(bank));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Evaluated.ShouldBe(6);
        result.Data.Suggested.ShouldBe(6); // 每条都有唯一的等额候选

        _counter.BatchLoads.ShouldBe(1, "六条流水的候选集必须一次取回");
        _counter.SingleAmountLoads.ShouldBe(0, "建议匹配不得再走逐条取回的那条路径");
    }

    /// <summary>
    /// 批量取回与逐条取回必须给出<b>逐字相同</b>的候选集（含组内顺序）。
    /// </summary>
    /// <remarks>
    /// 两条匹配规则都以「候选唯一」为判据，所以少取或多取一条都会改变结论；
    /// 顺序变了结论不变，但差异极难查，故一并锁住。
    /// </remarks>
    [Fact]
    public async Task BatchedCandidates_MatchThePerAmountQuery()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var date = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        await SeedBankLineAsync(bank, 200m, date);
        await SeedBankLineAsync(bank, 200m, date.AddDays(1)); // 同额两条 → 组内有序
        await SeedBankLineAsync(bank, 300m, date);

        using var scope = ServiceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<BankMatchEngine>();

        decimal[] amounts = [200m, 300m, 999m]; // 999 无候选
        var batched = await engine.GetCandidatesByAmountAsync(bank, amounts, CancellationToken.None);

        foreach (var amount in amounts)
        {
            var single = await engine.GetCandidatesAsync(bank, amount, CancellationToken.None);
            var fromBatch = batched.TryGetValue(amount, out var list) ? list : [];
            fromBatch.Select(c => c.JournalLineId).ShouldBe(single.Select(c => c.JournalLineId));
        }

        batched[200m].Count.ShouldBe(2);
        batched.ContainsKey(999m).ShouldBeFalse("没有候选的金额不该在字典里留一个空条目");
    }

    private sealed class CandidateLoadCounter
    {
        public int SingleAmountLoads;
        public int BatchLoads;
    }

    private sealed class CountingBankMatchEngine : BankMatchEngine
    {
        private readonly CandidateLoadCounter _counter;

        public CountingBankMatchEngine(
            IReadOnlyRepository<JournalLine, Guid> journalLineRepository,
            IReadOnlyRepository<ReconciliationLine, Guid> reconLineRepository,
            IReadOnlyRepository<BankTransaction, Guid> bankTxnRepository,
            IOptionsSnapshot<FinanceOptions> options,
            CandidateLoadCounter counter)
            : base(journalLineRepository, reconLineRepository, bankTxnRepository, options)
        {
            _counter = counter;
        }

        public override Task<List<BankMatchCandidate>> GetCandidatesAsync(Guid accountId, decimal amount, CancellationToken cancellationToken)
        {
            _counter.SingleAmountLoads++;
            return base.GetCandidatesAsync(accountId, amount, cancellationToken);
        }

        public override Task<Dictionary<decimal, List<BankMatchCandidate>>> GetCandidatesByAmountAsync(
            Guid accountId, IReadOnlyCollection<decimal> amounts, CancellationToken cancellationToken)
        {
            _counter.BatchLoads++;
            return base.GetCandidatesByAmountAsync(accountId, amounts, cancellationToken);
        }
    }
}
