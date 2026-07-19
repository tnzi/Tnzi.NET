namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 税级表服务
/// </summary>
/// <remarks>
/// 保存期连续性校验：行按序号升序、首行下界为 0、[lower, upper) 首尾相接不重叠、
/// 仅末行允许开区间上界。同 Code 多版本按 EffectiveFrom 解析（≤ asOf 的最大者）。
/// 编码统一大写规范化——公式 Bracket('code') 的定位不受录入大小写影响。
/// </remarks>
public class BracketTableService : ApplicationService, IBracketTableService
{
    private readonly IRepository<BracketTable, Guid> _tableRepository;
    private readonly IRepository<BracketRow, Guid> _rowRepository;

    public BracketTableService(
        IServiceProvider serviceProvider,
        IRepository<BracketTable, Guid> tableRepository,
        IRepository<BracketRow, Guid> rowRepository) : base(serviceProvider)
    {
        _tableRepository = Check.NotNull(tableRepository);
        _rowRepository = Check.NotNull(rowRepository);
    }

    public async Task<Result<IPagedList<BracketTableListDto>>> GetPagedAsync(BracketTableQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _tableRepository.AsNoTracking()
            .Filter(query)
            .OrderBy(t => t.Code).ThenByDescending(t => t.EffectiveFrom)
            .ProjectTo<BracketTable, BracketTableListDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<BracketTableDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var table = await _tableRepository.AsNoTracking()
            .Include(t => t.Rows)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (table == null)
            return Fail<BracketTableDto>("Bracket table not found.", 404);

        return Ok(ToDto(table));
    }

    public async Task<Result<BracketTableDto>> CreateAsync(CreateBracketTableDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = Validate(input);
        if (!validation.Succeeded)
            return Fail<BracketTableDto>(validation.Message ?? "Invalid bracket table.", validation.Code ?? 400);

        var code = input.Code.Trim().ToUpperInvariant();
        var effectiveFrom = input.EffectiveFrom.ToUtcDate();
        if (await _tableRepository.AnyAsync(t => t.Code == code && t.EffectiveFrom == effectiveFrom, cancellationToken))
            return Fail<BracketTableDto>($"Bracket table '{code}' already has a version effective from {effectiveFrom:yyyy-MM-dd}.", 409);

        var table = new BracketTable
        {
            Code = code,
            Name = input.Name.Trim(),
            Description = input.Description,
            EffectiveFrom = effectiveFrom
        };
        AppendRows(table, input.Rows);

        try
        {
            await _tableRepository.InsertAsync(table, cancellationToken);
            await _tableRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<BracketTableDto>($"Bracket table '{code}' already has a version effective from {effectiveFrom:yyyy-MM-dd}.", 409);
        }

        return Ok(ToDto(table));
    }

    public async Task<Result<BracketTableDto>> UpdateAsync(Guid id, UpdateBracketTableDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var table = await _tableRepository.AsQueryable(true)
            .Include(t => t.Rows)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (table == null)
            return Fail<BracketTableDto>("Bracket table not found.", 404);

        var validation = Validate(input);
        if (!validation.Succeeded)
            return Fail<BracketTableDto>(validation.Message ?? "Invalid bracket table.", validation.Code ?? 400);

        var code = input.Code.Trim().ToUpperInvariant();
        var effectiveFrom = input.EffectiveFrom.ToUtcDate();
        if (await _tableRepository.AnyAsync(t => t.Code == code && t.EffectiveFrom == effectiveFrom && t.Id != id, cancellationToken))
            return Fail<BracketTableDto>($"Bracket table '{code}' already has a version effective from {effectiveFrom:yyyy-MM-dd}.", 409);

        table.Code = code;
        table.Name = input.Name.Trim();
        table.Description = input.Description;
        table.EffectiveFrom = effectiveFrom;
        table.IsActive = input.IsActive;

        try
        {
            // 行硬删 + 重建 + 头更新须原子（无环境事务时仓储逐调用立即提交）；
            // 并发的同版本创建由唯一索引兜底，UoW 提交冲突整体回滚后翻译 409
            await ExecuteInUnitOfWorkAsync<Result>(async ct =>
            {
                if (table.Rows.Count > 0)
                    await _rowRepository.DeleteManyAsync(table.Rows.ToList(), ct);
                table.Rows.Clear();
                AppendRows(table, input.Rows);

                await _tableRepository.UpdateAsync(table, ct);
                await _tableRepository.SaveChangesAsync(ct);
                return Result.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<BracketTableDto>($"Bracket table '{code}' already has a version effective from {effectiveFrom:yyyy-MM-dd}.", 409);
        }

        return Ok(ToDto(table));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var table = await _tableRepository.AsQueryable(true)
            .Include(t => t.Rows)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (table == null)
            return Fail("Bracket table not found.", 404);

        // 行无软删除，随头一并物理删除（软删的头不再引用行）；两次写入须原子
        await ExecuteInUnitOfWorkAsync<Result>(async ct =>
        {
            if (table.Rows.Count > 0)
                await _rowRepository.DeleteManyAsync(table.Rows.ToList(), ct);
            await _tableRepository.DeleteAsync(table, ct);
            return Result.Success();
        }, cancellationToken);

        return Ok();
    }

    public async Task<Result<BracketTableDto>> ResolveAsync(string code, DateTime asOf, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);

        var normalized = code.Trim().ToUpperInvariant();
        var cutoff = asOf.ToUtcDate();

        var table = await _tableRepository.AsNoTracking()
            .Include(t => t.Rows)
            .Where(t => t.Code == normalized && t.IsActive && t.EffectiveFrom <= cutoff)
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (table == null)
            return Fail<BracketTableDto>($"No active bracket table '{normalized}' is effective on {cutoff:yyyy-MM-dd}.", 404);

        return Ok(ToDto(table));
    }

    private Result Validate(CreateBracketTableDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Code))
            return Fail("Table code is required.");
        if (input.Code.Trim().Length > 64)
            return Fail("Table code must not exceed 64 characters.");
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Table name is required.");
        if (input.Rows == null || input.Rows.Count == 0)
            return Fail("A bracket table requires at least one row.");

        var rows = input.Rows.OrderBy(r => r.Sequence).ToList();
        if (rows.Select(r => r.Sequence).Distinct().Count() != rows.Count)
            return Fail("Row sequences must be unique.");
        if (rows[0].LowerBound != 0)
            return Fail("The first bracket row must start at 0.");

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Rate < 0 || row.Rate > 1)
                return Fail($"Row {row.Sequence}: Rate must be between 0 and 1 (a fraction, e.g. 0.25 for 25%).");
            if (row.QuickDeduction is < 0)
                return Fail($"Row {row.Sequence}: QuickDeduction cannot be negative.");

            var isLast = i == rows.Count - 1;
            if (!isLast && !row.UpperBound.HasValue)
                return Fail($"Row {row.Sequence}: only the last row may have an open upper bound.");
            if (row.UpperBound.HasValue && row.UpperBound.Value <= row.LowerBound)
                return Fail($"Row {row.Sequence}: UpperBound must be greater than LowerBound.");
            if (!isLast && rows[i + 1].LowerBound != row.UpperBound!.Value)
                return Fail($"Row {rows[i + 1].Sequence}: LowerBound must equal the previous row's UpperBound (brackets must be contiguous).");
        }

        // 速算扣除数一致性：BracketMath 命中带 QuickDeduction 的行会直接算 amount×Rate−QD（不再查税率表），
        // 若 QD 与累进不等价会静默算错税（顶档误填 QD=0 即退化成全额累进多扣税）。要求全表 all-or-nothing +
        // 每行 QD 精确等于其累进等价值 QDᵢ = LowerBoundᵢ×Rateᵢ − 到下界为止的累进税额。
        var withQd = rows.Count(r => r.QuickDeduction.HasValue);
        if (withQd > 0 && withQd < rows.Count)
            return Fail("QuickDeduction must be set on all rows or none: a bracket table is either quick-deduction (every row) or purely progressive (no row).");

        if (withQd == rows.Count)
        {
            var cumulative = 0m; // 到本行下界为止的累进税额
            for (var i = 0; i < rows.Count; i++)
            {
                if (i > 0)
                    cumulative += (rows[i].LowerBound - rows[i - 1].LowerBound) * rows[i - 1].Rate;

                var expected = rows[i].LowerBound * rows[i].Rate - cumulative;
                if (Math.Abs(rows[i].QuickDeduction!.Value - expected) > 0.01m)
                    return Fail($"Row {rows[i].Sequence}: QuickDeduction {rows[i].QuickDeduction.Value} is inconsistent with the rate schedule (expected {expected:0.####} to match progressive tax). Correct the value, or clear QuickDeduction on all rows to use pure progressive calculation.");
            }
        }

        return Ok();
    }

    /// <summary>
    /// 追加税级行（经导航属性挂载）。创建时头 ID 尚未生成（SaveChanges 才分配），
    /// 此处赋值等价于默认值，实际 FK 由 EF 沿导航属性在提交时回填；更新时头 ID 已存在，即显式赋值
    /// </summary>
    private static void AppendRows(BracketTable table, List<BracketRowInputDto> rows)
    {
        foreach (var row in rows.OrderBy(r => r.Sequence))
        {
            table.Rows.Add(new BracketRow
            {
                TableId = table.Id,
                Sequence = row.Sequence,
                LowerBound = row.LowerBound,
                UpperBound = row.UpperBound,
                Rate = row.Rate,
                QuickDeduction = row.QuickDeduction
            });
        }
    }

    private static BracketTableDto ToDto(BracketTable table)
    {
        var dto = table.MapTo<BracketTableDto>();
        dto.Rows = table.Rows
            .OrderBy(r => r.Sequence)
            .Select(r => r.MapTo<BracketRowDto>())
            .ToList();
        return dto;
    }
}
