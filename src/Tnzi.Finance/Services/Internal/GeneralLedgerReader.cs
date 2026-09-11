namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 总账明细（General Ledger）的读取器：单科目分页、稳定全序、跨页连续运行余额、
/// 关键字/来源类型筛选与倒序，以及全量 CSV 导出。
/// </summary>
/// <remarks>
/// 从 <see cref="FinancialReportService"/> 拆出：其余七张报表都是「按科目聚合出一组数」，
/// 而总账明细是「按确定性行序翻一本流水账」——它独占了排序/分页/前缀和/筛选下推这一整套
/// 机制（本类九个成员里有八个只服务于它），与聚合类报表没有共享代码。<br/>
/// **行序是本类的核心不变量**：`PostingDate → 凭证号 → 行号` 的稳定全序既是运行余额的
/// 前提，也是分页与 CSV 导出一致的前提；倒序是它的精确反向。<br/>
/// public 因为经 DI 注入 public 服务的构造函数（沿 <c>BalanceSummaryReader</c> 先例）。
/// </remarks>
public class GeneralLedgerReader
{
    private readonly IReadOnlyRepository<JournalLine, Guid> _lineRepository;
    /// <summary>
    /// 只用于把命中的来源单据 <c>(SourceType, SourceId)</c> 解析成凭证 Id
    /// （见 <see cref="MatchingSourceEntryIdsAsync"/>），行明细本身始终读 <see cref="_lineRepository"/>
    /// </summary>
    private readonly IReadOnlyRepository<JournalEntry, Guid> _entryRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;
    private readonly IReadOnlyRepository<Vendor, Guid> _vendorRepository;
    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    /// <summary>
    /// 关键字搜索的额外贡献者（支票号等）。未注册即只搜内核自带的那几项 ——
    /// 只会少搜到，绝不会多返回不该出现的行。
    /// </summary>
    private readonly IEnumerable<IGeneralLedgerSearchContributor> _searchContributors;
    private readonly BalanceSummaryReader _reader;
    private readonly FinanceOptions _options;

    public GeneralLedgerReader(
        IReadOnlyRepository<JournalLine, Guid> lineRepository,
        IReadOnlyRepository<JournalEntry, Guid> entryRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository,
        IReadOnlyRepository<Vendor, Guid> vendorRepository,
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IEnumerable<IGeneralLedgerSearchContributor>? searchContributors,
        BalanceSummaryReader reader,
        IOptionsSnapshot<FinanceOptions> options)
    {
        _lineRepository = Check.NotNull(lineRepository);
        _entryRepository = Check.NotNull(entryRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _customerRepository = Check.NotNull(customerRepository);
        _vendorRepository = Check.NotNull(vendorRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _searchContributors = searchContributors ?? Enumerable.Empty<IGeneralLedgerSearchContributor>();
        _reader = Check.NotNull(reader);
        _options = Check.NotNull(options).Value;
    }

    private IQueryable<JournalLine> PostedLines => _lineRepository.AsNoTracking().Where(l => l.IsPosted);

    public async Task<Result<GeneralLedgerReportDto>> GetGeneralLedgerAsync(
        Guid accountId, DateTime from, DateTime to, PagedQueryDto paging, GeneralLedgerFilterDto? filter,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(paging);

        if (to.Date < from.Date)
            return Result<GeneralLedgerReportDto>.Failure("The 'to' date must not be earlier than the 'from' date.");

        var account = await _accountRepository.GetAsync(accountId, cancellationToken);
        if (account == null)
            return Result<GeneralLedgerReportDto>.Failure("Account not found.", 404);

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        var keyword = filter?.Keyword?.Trim();
        var sourceType = filter?.SourceType?.Trim();
        var isFiltered = !string.IsNullOrEmpty(keyword) || !string.IsNullOrEmpty(sourceType);
        // 倒序开关独立于筛选：只有 Keyword/SourceType 空但 Descending=true 时，isFiltered 为 false 仍要倒序
        var descending = filter?.Descending ?? false;

        if (isFiltered)
        {
            // 筛选后的结果集是全序里被抽稀的一个子集，累计余额链条已断：期初/期末/运行余额全部不适用。
            // 与其返回一个"看起来像余额"的错数，不如置 0 并由 IsFiltered 声明不适用（DTO 契约）。
            // 顺带：期初/期间聚合与分页前缀和两次查询在此路径下完全不需要发出
            var filtered = await ApplyLedgerFilterAsync(
                PeriodLines(accountId, fromDate, toExclusive), keyword, sourceType, cancellationToken);

            // 倒序时套用与正序稳定全序精确相反的行序；余额仍按契约置 0（IsFiltered=true）
            var filteredLines = await ProjectLedgerLines(descending ? OrderedDescending(filtered) : Ordered(filtered))
                .CreateAsync(paging.PageIndex, paging.PageSize, cancellationToken);

            return Result<GeneralLedgerReportDto>.Success(new GeneralLedgerReportDto
            {
                AccountId = account.Id,
                Code = account.Code,
                Name = account.Name,
                From = fromDate,
                To = to.Date,
                BaseCurrency = _options.BaseCurrency,
                IsFiltered = true,
                OpeningBalance = 0m,
                ClosingBalance = 0m,
                Lines = filteredLines
            });
        }

        // 期初/期间借贷四项聚合（读路径按开关走汇总桶或明细条件求和）；行明细始终走明细（行序依赖）
        var sums = await _reader.SumOpeningAndPeriodForAccountAsync(accountId, fromDate, toExclusive, cancellationToken);

        var openingDebit = sums.OpeningDebit;
        var openingCredit = sums.OpeningCredit;
        var periodDebit = sums.PeriodDebit;
        var periodCredit = sums.PeriodCredit;

        var openingBalance = openingDebit - openingCredit;

        // 倒序与正序每行的“值”完全相同（都是按时间的该笔交易后的余额），只是显示顺序与落在哪一页不同，
        // 故两条路径分别成页
        IPagedList<GeneralLedgerLineDto> lines;
        if (descending)
        {
            lines = await DescendingLedgerPageAsync(accountId, fromDate, toExclusive, openingBalance, paging, cancellationToken);
        }
        else
        {
            lines = await ProjectLedgerLines(OrderedPeriodLines(accountId, fromDate, toExclusive))
                .CreateAsync(paging.PageIndex, paging.PageSize, cancellationToken);

            // 第 N 页起点余额 = 期初余额 + 页首之前行的净额（单次聚合，首页零额外查询）。
            // 页行与前缀和是两次独立查询：并发过账落在两查询之间时本页余额可能整体偏移
            // （报表为近似快照，非可串行化读；刷新即自愈）
            var running = openingBalance;
            if (paging.Skip > 0)
            {
                running += await OrderedPeriodLines(accountId, fromDate, toExclusive)
                    .Take(paging.Skip)
                    .SumAsync(l => l.Debit - l.Credit, cancellationToken);
            }

            foreach (var line in lines.Items)
            {
                running += line.Debit - line.Credit;
                line.RunningBalance = running;
            }
        }

        var report = new GeneralLedgerReportDto
        {
            AccountId = account.Id,
            Code = account.Code,
            Name = account.Name,
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency,
            OpeningBalance = openingBalance,
            ClosingBalance = openingBalance + periodDebit - periodCredit,
            Lines = lines
        };

        return Result<GeneralLedgerReportDto>.Success(report);
    }

    /// <summary>期间内该科目的已过账行（无序；排序与筛选各自组合）</summary>
    private IQueryable<JournalLine> PeriodLines(Guid accountId, DateTime fromDate, DateTime toExclusive)
        => PostedLines.Where(l => l.AccountId == accountId && l.PostingDate >= fromDate && l.PostingDate < toExclusive);

    /// <summary>
    /// 期间内已过账行的稳定全序：同日跨凭证按凭证号（过账时顺序分配、补零后字符串可排序，
    /// 凭证号唯一故构成全序；顺序 GUID 的字符串序不保证时序）、凭证内按行号。
    /// 运行余额与分页导出都依赖此确定性顺序
    /// </summary>
    private static IQueryable<JournalLine> Ordered(IQueryable<JournalLine> lines)
        => lines
            .OrderBy(l => l.PostingDate)
            .ThenBy(l => l.JournalEntry!.Number)
            .ThenBy(l => l.LineNumber);

    /// <summary>期间内该科目已过账行的稳定全序（无筛选路径的行来源）</summary>
    private IQueryable<JournalLine> OrderedPeriodLines(Guid accountId, DateTime fromDate, DateTime toExclusive)
        => Ordered(PeriodLines(accountId, fromDate, toExclusive));

    /// <summary>
    /// 稳定全序的精确反向（<c>PostingDate → 凭证号 → 行号</c> 全部降序）：最新在最上，供网银式倒序呈现。
    /// 供筛选路径直接分页；无筛选路径的运行余额仍在正序上累加后反转（见 <see cref="DescendingLedgerPageAsync"/>）
    /// </summary>
    private static IQueryable<JournalLine> OrderedDescending(IQueryable<JournalLine> lines)
        => lines
            .OrderByDescending(l => l.PostingDate)
            .ThenByDescending(l => l.JournalEntry!.Number)
            .ThenByDescending(l => l.LineNumber);

    /// <summary>
    /// 倒序分页（无筛选路径，运行余额正确且不把整个期间载入内存）。
    /// 设 T = 期间总行数、s = 页大小、p = 从 1 起的页号：倒序第 p 页对应的“按时间”行是区间
    /// <c>[max(0, T − p·s), T − (p−1)·s)</c>——即把正序稳定全序的末尾 s 行放到第 1 页。
    /// </summary>
    /// <remarks>
    /// 先算区间之前所有“按时间在先”行的净额前缀（单次聚合，与正序 Skip&gt;0 前缀同一手法），
    /// 把该区间按正序取出并逐行累加运行余额（每行值 = 该笔交易后的余额，与正序视图对应行逐字相同），
    /// 最后把结果列表反转返回“最新在最上”。TotalCount 仍是整个期间的总行数。
    /// 与正序路径同样是近似快照：区间行、前缀和、计数分别是独立查询，并发过账落在其间时刷新即自愈。
    /// </remarks>
    private async Task<IPagedList<GeneralLedgerLineDto>> DescendingLedgerPageAsync(
        Guid accountId, DateTime fromDate, DateTime toExclusive, decimal openingBalance,
        PagedQueryDto paging, CancellationToken cancellationToken)
    {
        var total = await PeriodLines(accountId, fromDate, toExclusive).CountAsync(cancellationToken);

        var upper = Math.Max(0, total - (paging.PageIndex - 1) * paging.PageSize); // 区间上界（不含）
        var lower = Math.Max(0, total - paging.PageIndex * paging.PageSize);       // 区间下界（含）
        var take = upper - lower;

        // 页号超过总页数（末页之后）：空页，但 TotalCount 仍为整个期间的总行数
        if (take <= 0)
            return new PagedList<GeneralLedgerLineDto>([], paging.PageIndex, paging.PageSize, total);

        // 区间首行之前的运行余额 = 期初 + 区间前所有按时间在先行的净额（lower==0 即区间从期首开始，零额外查询）
        var running = openingBalance;
        if (lower > 0)
        {
            running += await OrderedPeriodLines(accountId, fromDate, toExclusive)
                .Take(lower)
                .SumAsync(l => l.Debit - l.Credit, cancellationToken);
        }

        // 区间本身按正序取出，逐行累加运行余额（区间末行=期间最新一行时其值即 ClosingBalance）
        var window = await ProjectLedgerLines(OrderedPeriodLines(accountId, fromDate, toExclusive))
            .Skip(lower)
            .Take(take)
            .ToListAsync(cancellationToken);

        foreach (var line in window)
        {
            running += line.Debit - line.Credit;
            line.RunningBalance = running;
        }

        // 反转为“最新在最上”返回；每行运行余额已算好，反转只改顺序不改其值
        window.Reverse();
        return new PagedList<GeneralLedgerLineDto>(window, paging.PageIndex, paging.PageSize, total);
    }

    /// <summary>
    /// 把总账筛选条件叠加到行查询上。<b>全部谓词都留在 IQueryable 上下推数据库</b>——
    /// 分页发生在数据库侧，任何一步内存过滤都会让本页行数与 TotalCount 同时失真
    /// </summary>
    private async Task<IQueryable<JournalLine>> ApplyLedgerFilterAsync(
        IQueryable<JournalLine> lines, string? keyword, string? sourceType, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(sourceType))
            lines = lines.Where(l => l.JournalEntry!.SourceType == sourceType);

        if (string.IsNullOrEmpty(keyword))
            return lines;

        var kw = keyword.ToLower();

        // 命中的来源单据（付款域自带的三项 + 贡献者贡献的任意来源类型）先解析成**凭证 Id 集合**，
        // 再以一个 IN 回到主查询。走 Id 而不是把 (SourceType, SourceId) 拼回谓词，是因为这两列
        // 必须**成对**生效，而来源类型有几种只有运行期才知道——成对下推只能展开成一条 OR 链
        // （要动态拼表达式树），或把两列拼成一个字符串键（引入分隔符碰撞与各库 concat/collation 差异）。
        // 集合本身经参数化数组下推（EF 10 对参数集合走 json_each/OPENJSON/= ANY，
        // 不再展开成一个个参数），主查询仍是单条 SQL，分页与计数都在数据库侧完成
        var matchedEntryIds = await MatchingSourceEntryIdsAsync(kw, cancellationToken);

        return matchedEntryIds.Count == 0
            ? lines.Where(l =>
                (l.Memo != null && l.Memo.ToLower().Contains(kw)) ||
                (l.JournalEntry!.Memo != null && l.JournalEntry.Memo.ToLower().Contains(kw)) ||
                (l.JournalEntry!.Number != null && l.JournalEntry.Number.ToLower().Contains(kw)))
            : lines.Where(l =>
                (l.Memo != null && l.Memo.ToLower().Contains(kw)) ||
                (l.JournalEntry!.Memo != null && l.JournalEntry.Memo.ToLower().Contains(kw)) ||
                (l.JournalEntry!.Number != null && l.JournalEntry.Number.ToLower().Contains(kw)) ||
                matchedEntryIds.Contains(l.JournalEntryId));
    }

    /// <summary>
    /// 关键字命中的**来源单据**所对应的凭证 Id。
    /// 命中口径：付款参考号 / 往来方（客户或供应商）名称 / 各 <see cref="IGeneralLedgerSearchContributor"/>
    /// 贡献的任意 <c>(SourceType, SourceId)</c> 组合（银行域的已开具支票号即其一）
    /// </summary>
    /// <remarks>
    /// <b>成对匹配</b>：凭证以 <c>SourceType</c> + <c>SourceId</c>(string) 多态回链业务单据，
    /// 两者必须成对比较——只按 <c>SourceId</c> 匹配会把另一个来源类型下**同号**的单据一起捞出来。
    /// <br/><br/>
    /// Guid → string 的转换刻意放在 .NET 侧而非 SQL 侧：各数据库对 uuid 的文本化格式与大小写
    /// 并不一致，交给 SQL 转换会静默匹配不上（<c>SourceId</c> 写入时用的是 .NET 的 "D" 格式）。
    /// 同理，成对比较用 <see cref="StringComparer.OrdinalIgnoreCase"/>：候选行由数据库按其自身
    /// collation 取出，再在内存里收窄成对；用大小写敏感的比较会在不区分大小写的库上**少匹配**。
    /// </remarks>
    private async Task<List<Guid>> MatchingSourceEntryIdsAsync(string keyword, CancellationToken cancellationToken)
    {
        // SourceType → 该类型下命中的 SourceId 集合
        var matches = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void AddMatch(string sourceType, string sourceId)
        {
            if (!matches.TryGetValue(sourceType, out var ids))
                matches[sourceType] = ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ids.Add(sourceId);
        }

        foreach (var paymentId in await MatchingPaymentIdsAsync(keyword, cancellationToken))
            AddMatch(FinanceSourceTypes.PaymentEntry, paymentId.ToString());

        // 内核之外的可搜项（支票号来自银行域的登记簿、消费应用自己的单据号……）经贡献者并入，
        // 使报表内核不必认识 BankCheck，也不必认识消费应用的任何单据类型。
        foreach (var contributor in _searchContributors)
        {
            foreach (var match in await contributor.MatchAsync(keyword, cancellationToken))
            {
                if (!string.IsNullOrEmpty(match.SourceType) && !string.IsNullOrEmpty(match.SourceId))
                    AddMatch(match.SourceType, match.SourceId);
            }
        }

        if (matches.Count == 0)
            return [];

        // 候选先按 SourceId 在数据库侧收窄（有界：命中的单据数），再在内存里做成对判定。
        // 这一步不参与分页，故不违反"筛选全部下推"——分页仍发生在主查询的 IN 之上
        var sourceIds = matches.Values.SelectMany(ids => ids).Distinct(StringComparer.Ordinal).ToList();

        var candidates = await _entryRepository.AsNoTracking()
            .Where(e => e.SourceId != null && sourceIds.Contains(e.SourceId))
            .Select(e => new { e.Id, e.SourceType, e.SourceId })
            .ToListAsync(cancellationToken);

        return
        [
            .. candidates
                .Where(c => c.SourceType != null && c.SourceId != null
                    && matches.TryGetValue(c.SourceType, out var ids) && ids.Contains(c.SourceId))
                .Select(c => c.Id)
        ];
    }

    /// <summary>
    /// 关键字命中的收付款单 Id。命中口径：付款参考号 / 往来方（客户或供应商）名称；
    /// 两个来源都以子查询留在数据库侧，只有最终的 Id 列表回到内存
    /// </summary>
    private Task<List<Guid>> MatchingPaymentIdsAsync(string keyword, CancellationToken cancellationToken)
    {
        var vendorIds = _vendorRepository.AsNoTracking()
            .Where(v => v.Name.ToLower().Contains(keyword))
            .Select(v => v.Id);

        var customerIds = _customerRepository.AsNoTracking()
            .Where(c => c.Name.ToLower().Contains(keyword))
            .Select(c => c.Id);

        return _paymentRepository.AsNoTracking()
            .Where(p =>
                (p.Reference != null && p.Reference.ToLower().Contains(keyword)) ||
                (p.PartyType == FinancePartyType.Vendor && vendorIds.Contains(p.PartyId)) ||
                (p.PartyType == FinancePartyType.Customer && customerIds.Contains(p.PartyId)))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<GeneralLedgerLineDto> ProjectLedgerLines(IQueryable<JournalLine> query)
        => query.Select(l => new GeneralLedgerLineDto
        {
            JournalEntryId = l.JournalEntryId,
            EntryNumber = l.JournalEntry!.Number,
            PostingDate = l.PostingDate,
            Memo = l.Memo ?? l.JournalEntry.Memo,
            Debit = l.Debit,
            Credit = l.Credit,
            PartyType = l.PartyType,
            PartyId = l.PartyId,
            SourceType = l.JournalEntry.SourceType,
            SourceId = l.JournalEntry.SourceId
        });

    public async Task<Result<string>> ExportGeneralLedgerCsvAsync(Guid accountId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
            return Result<string>.Failure("The 'to' date must not be earlier than the 'from' date.");

        var account = await _accountRepository.GetAsync(accountId, cancellationToken);
        if (account == null)
            return Result<string>.Failure("Account not found.", 404);

        var fromDate = from.ToUtcDate();
        var toExclusive = to.ToUtcDate().AddDays(1);

        // 期初余额走读路径（汇总桶或明细）；行明细全量始终读明细（运行余额行序依赖）
        var openingSums = await _reader.SumOpeningAndPeriodForAccountAsync(accountId, fromDate, toExclusive, cancellationToken);

        var openingBalance = openingSums.OpeningDebit - openingSums.OpeningCredit;

        // 成功路径单次扫描（多取一行探测超限）；拒绝超限而非静默截断：
        // 截断的运行余额会误导对账。精确行数仅在拒绝路径补一次未排序计数
        var lines = await ProjectLedgerLines(OrderedPeriodLines(accountId, fromDate, toExclusive))
            .Take(_options.ReportExportMaxRows + 1)
            .ToListAsync(cancellationToken);
        if (lines.Count > _options.ReportExportMaxRows)
        {
            var count = await PostedLines
                .CountAsync(l => l.AccountId == accountId && l.PostingDate >= fromDate && l.PostingDate < toExclusive, cancellationToken);
            return Result<string>.Failure($"The export would contain {count} rows, exceeding the limit of {_options.ReportExportMaxRows}. Narrow the date range.", 400);
        }

        var running = openingBalance;
        foreach (var line in lines)
        {
            running += line.Debit - line.Credit;
            line.RunningBalance = running;
        }

        var header = new GeneralLedgerReportDto
        {
            AccountId = account.Id,
            Code = account.Code,
            Name = account.Name,
            From = fromDate,
            To = to.Date,
            BaseCurrency = _options.BaseCurrency,
            OpeningBalance = openingBalance,
            ClosingBalance = running
        };

        return Result<string>.Success(ReportCsvWriter.GeneralLedger(header, lines));
    }
}
