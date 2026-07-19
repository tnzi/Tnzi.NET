namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// 税级表：连续性校验、版本唯一 409、EffectiveFrom 版本解析、行重建、与求值器闭包集成
/// </summary>
public class BracketTableServiceTests : PayrollIntegrationTestBase
{
    private static readonly DateTime Y2025 = new(2025, 1, 1);
    private static readonly DateTime Y2026 = new(2026, 1, 1);

    [Fact]
    public async Task Create_ValidTable_ReturnsOrderedRows()
    {
        var created = await CreateBracketTableAsync("CN_IIT", Y2026, StandardQuickRows());
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Code.ShouldBe("CN_IIT");
        created.Data.Rows.Count.ShouldBe(4);
        created.Data.Rows.Select(r => r.Sequence).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task Create_CodeIsNormalizedToUpperCase()
    {
        var created = await CreateBracketTableAsync("us_fed", Y2026, StandardProgressiveRows());
        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Code.ShouldBe("US_FED");
    }

    [Fact]
    public async Task Create_FirstRowNotZero_Rejected()
    {
        var result = await CreateBracketTableAsync("BAD_START", Y2026,
            new BracketRowInputDto { Sequence = 1, LowerBound = 100, UpperBound = null, Rate = 0.1m });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_GapBetweenRows_Rejected()
    {
        var result = await CreateBracketTableAsync("BAD_GAP", Y2026,
            new BracketRowInputDto { Sequence = 1, LowerBound = 0, UpperBound = 1000, Rate = 0.1m },
            new BracketRowInputDto { Sequence = 2, LowerBound = 2000, UpperBound = null, Rate = 0.2m });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_OverlappingRows_Rejected()
    {
        var result = await CreateBracketTableAsync("BAD_OVERLAP", Y2026,
            new BracketRowInputDto { Sequence = 1, LowerBound = 0, UpperBound = 1000, Rate = 0.1m },
            new BracketRowInputDto { Sequence = 2, LowerBound = 500, UpperBound = null, Rate = 0.2m });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_OpenUpperBoundNotLast_Rejected()
    {
        var result = await CreateBracketTableAsync("BAD_OPEN", Y2026,
            new BracketRowInputDto { Sequence = 1, LowerBound = 0, UpperBound = null, Rate = 0.1m },
            new BracketRowInputDto { Sequence = 2, LowerBound = 1000, UpperBound = null, Rate = 0.2m });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_UpperNotAboveLower_Rejected()
    {
        var result = await CreateBracketTableAsync("BAD_RANGE", Y2026,
            new BracketRowInputDto { Sequence = 1, LowerBound = 0, UpperBound = 0, Rate = 0.1m });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_InconsistentQuickDeduction_Rejected()
    {
        // 顶档误填 QuickDeduction=0（一致值应为 2660），与累进不等价 → 400，防止静默全额累进多扣税
        var result = await CreateBracketTableAsync("BAD_QD", Y2026,
            new BracketRowInputDto { Sequence = 1, LowerBound = 0, UpperBound = 3000, Rate = 0.03m, QuickDeduction = 0 },
            new BracketRowInputDto { Sequence = 2, LowerBound = 3000, UpperBound = 12000, Rate = 0.10m, QuickDeduction = 210 },
            new BracketRowInputDto { Sequence = 3, LowerBound = 12000, UpperBound = 25000, Rate = 0.20m, QuickDeduction = 1410 },
            new BracketRowInputDto { Sequence = 4, LowerBound = 25000, UpperBound = null, Rate = 0.25m, QuickDeduction = 0 });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_PartialQuickDeduction_Rejected()
    {
        // 部分行带速算扣除数、部分留 null（BracketMath 会逐行混用两种算法）→ all-or-nothing 拒绝
        var result = await CreateBracketTableAsync("MIX_QD", Y2026,
            new BracketRowInputDto { Sequence = 1, LowerBound = 0, UpperBound = 3000, Rate = 0.03m, QuickDeduction = 0 },
            new BracketRowInputDto { Sequence = 2, LowerBound = 3000, UpperBound = null, Rate = 0.10m });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_RateAboveOne_Rejected()
    {
        // Rate 是分数（0.25=25%），>1 几乎必然是把百分数当分数录错 → 拒绝
        var result = await CreateBracketTableAsync("BAD_RATE", Y2026,
            new BracketRowInputDto { Sequence = 1, LowerBound = 0, UpperBound = null, Rate = 10m });
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Create_DuplicateCodeAndEffectiveFrom_Rejected()
    {
        (await CreateBracketTableAsync("DUP_VER", Y2026, StandardProgressiveRows())).Succeeded.ShouldBeTrue();

        var duplicate = await CreateBracketTableAsync("dup_ver", Y2026, StandardProgressiveRows());
        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Code.ShouldBe(409);
    }

    [Fact]
    public async Task Resolve_PicksLatestVersionOnOrBeforeAsOf()
    {
        (await CreateBracketTableAsync("VER", Y2025, StandardProgressiveRows())).Succeeded.ShouldBeTrue();
        (await CreateBracketTableAsync("VER", Y2026, StandardQuickRows())).Succeeded.ShouldBeTrue();

        // 2025 期间 → 2025 版（无速算扣除数）
        var v2025 = await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.ResolveAsync("VER", new DateTime(2025, 6, 30)));
        v2025.Succeeded.ShouldBeTrue(v2025.Message);
        v2025.Data!.EffectiveFrom.Date.ShouldBe(Y2025);
        v2025.Data.Rows.All(r => r.QuickDeduction == null).ShouldBeTrue();

        // 生效日当天即切换到 2026 版
        var v2026 = await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.ResolveAsync("ver", Y2026));
        v2026.Succeeded.ShouldBeTrue(v2026.Message);
        v2026.Data!.EffectiveFrom.Date.ShouldBe(Y2026);

        // 首版之前无表可用
        var tooEarly = await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.ResolveAsync("VER", new DateTime(2024, 12, 31)));
        tooEarly.Succeeded.ShouldBeFalse();
        tooEarly.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Resolve_IgnoresInactiveVersions()
    {
        var created = await CreateBracketTableAsync("INACTIVE", Y2026, StandardProgressiveRows());
        created.Succeeded.ShouldBeTrue(created.Message);

        var updated = await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.UpdateAsync(created.Data!.Id, new UpdateBracketTableDto
        {
            Code = "INACTIVE",
            Name = "Inactive",
            EffectiveFrom = Y2026,
            Rows = StandardProgressiveRows().ToList(),
            IsActive = false
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);

        var resolved = await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.ResolveAsync("INACTIVE", new DateTime(2026, 6, 1)));
        resolved.Succeeded.ShouldBeFalse();
        resolved.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Update_RebuildsRows_HardDeletingOldOnes()
    {
        var created = await CreateBracketTableAsync("REBUILD", Y2026, StandardQuickRows());
        created.Succeeded.ShouldBeTrue(created.Message);

        var updated = await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.UpdateAsync(created.Data!.Id, new UpdateBracketTableDto
        {
            Code = "REBUILD",
            Name = "Rebuilt",
            EffectiveFrom = Y2026,
            Rows =
            [
                new BracketRowInputDto { Sequence = 1, LowerBound = 0, UpperBound = 5000, Rate = 0.05m },
                new BracketRowInputDto { Sequence = 2, LowerBound = 5000, UpperBound = null, Rate = 0.15m }
            ]
        }));
        updated.Succeeded.ShouldBeTrue(updated.Message);
        updated.Data!.Rows.Count.ShouldBe(2);

        // 旧 4 行必须物理消失（行无软删除）
        (await CountAsync<BracketRow>(r => r.TableId == created.Data!.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Delete_CascadesRowsPhysically()
    {
        var created = await CreateBracketTableAsync("DEL_TAB", Y2026, StandardQuickRows());
        created.Succeeded.ShouldBeTrue(created.Message);

        var deleted = await InScopeAsync<IBracketTableService, Result>(s => s.DeleteAsync(created.Data!.Id));
        deleted.Succeeded.ShouldBeTrue(deleted.Message);

        (await CountAsync<BracketRow>(r => r.TableId == created.Data!.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task ResolvedTable_FeedsEvaluatorBracketFunction()
    {
        // P4c PayslipCalculator 的接线方式：ResolveAsync 预取表 → BracketMath 闭包喂给求值器
        (await CreateBracketTableAsync("IIT", Y2026, StandardQuickRows())).Succeeded.ShouldBeTrue();

        var resolved = await InScopeAsync<IBracketTableService, Result<BracketTableDto>>(s => s.ResolveAsync("IIT", new DateTime(2026, 7, 1)));
        resolved.Succeeded.ShouldBeTrue(resolved.Message);
        var rows = resolved.Data!.Rows;

        var evaluated = await InScopeAsync<ISalaryFormulaEvaluator, Result<decimal>>(evaluator => Task.FromResult(
            evaluator.Evaluate("Bracket('IIT', GROSS)", new SalaryFormulaContext
            {
                Variables = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["GROSS"] = 20000m },
                BracketResolver = (_, amount) => BracketMath.Calculate(rows, amount)
            })));

        evaluated.Succeeded.ShouldBeTrue(evaluated.Message);
        evaluated.Data.ShouldBe(2590m);
    }
}
