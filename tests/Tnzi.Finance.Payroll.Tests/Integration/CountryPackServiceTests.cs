namespace Tnzi.Finance.Payroll.Tests.Integration;

/// <summary>
/// Country pack 契约：收集已注册 pack、按 Code 触发幂等播种、未注册 Code 拒绝。
/// 框架不内置 pack——此处用测试桩 pack 验证收集/触发机制。
/// </summary>
public class CountryPackServiceTests : PayrollIntegrationTestBase
{
    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddScoped<IPayrollCountryPack, TestCountryPack>();
    }

    [Fact]
    public async Task GetRegistered_ListsRegisteredPacks()
    {
        var result = await InScopeAsync<ICountryPackService, Result<List<CountryPackDto>>>(s => s.GetRegisteredAsync());
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ShouldContain(p => p.Code == "XX");
    }

    [Fact]
    public async Task Seed_UnknownCode_Returns404()
    {
        var result = await InScopeAsync<ICountryPackService, Result<CountryPackSeedResult>>(s => s.SeedAsync("ZZ"));
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Seed_IsIdempotent()
    {
        await SeedCoaAsync();

        var first = await InScopeAsync<ICountryPackService, Result<CountryPackSeedResult>>(s => s.SeedAsync("XX"));
        first.Succeeded.ShouldBeTrue(first.Message);
        first.Data!.ComponentsSeeded.ShouldBe(1);
        first.Data.BracketTablesSeeded.ShouldBe(1);

        var second = await InScopeAsync<ICountryPackService, Result<CountryPackSeedResult>>(s => s.SeedAsync("XX"));
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.ComponentsSeeded.ShouldBe(0); // 已存在，不重复播种
        second.Data.BracketTablesSeeded.ShouldBe(0);

        // 最终各仅一条
        (await CountAsync<SalaryComponent>(c => c.Code == "XXTAX")).ShouldBe(1);
        (await CountAsync<BracketTable>(t => t.Code == "XXINCOME")).ShouldBe(1);
    }

    /// <summary>测试桩 country pack：幂等播种一个组件 + 一张税级表（已存在则跳过）</summary>
    private sealed class TestCountryPack : IPayrollCountryPack
    {
        private readonly ISalaryComponentService _components;
        private readonly IBracketTableService _brackets;

        public TestCountryPack(ISalaryComponentService components, IBracketTableService brackets)
        {
            _components = components;
            _brackets = brackets;
        }

        public string Code => "XX";
        public string DisplayName => "Testland";

        public async Task<Result<CountryPackSeedResult>> SeedAsync(CancellationToken cancellationToken = default)
        {
            var componentsSeeded = 0;
            var tablesSeeded = 0;

            var comp = await _components.CreateAsync(new CreateSalaryComponentDto
            {
                Code = "XXTAX",
                Name = "XX Tax",
                Type = SalaryComponentType.Deduction,
                Formula = "Bracket('XXINCOME', GROSS)"
            }, cancellationToken);
            if (comp.Succeeded)
                componentsSeeded++;
            else if (comp.Code != 409)
                return Result.Failure<CountryPackSeedResult>(comp.Message ?? "Component seed failed.", comp.Code ?? 400);

            var table = await _brackets.CreateAsync(new CreateBracketTableDto
            {
                Code = "XXINCOME",
                Name = "XX Income Tax",
                EffectiveFrom = new DateTime(2026, 1, 1),
                Rows = new List<BracketRowInputDto>
                {
                    new() { Sequence = 1, LowerBound = 0, UpperBound = null, Rate = 0.10m }
                }
            }, cancellationToken);
            if (table.Succeeded)
                tablesSeeded++;
            else if (table.Code != 409)
                return Result.Failure<CountryPackSeedResult>(table.Message ?? "Bracket seed failed.", table.Code ?? 400);

            return Result.Success(new CountryPackSeedResult
            {
                ComponentsSeeded = componentsSeeded,
                BracketTablesSeeded = tablesSeeded
            });
        }
    }
}
