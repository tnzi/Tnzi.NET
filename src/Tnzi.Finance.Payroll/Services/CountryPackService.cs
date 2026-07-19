namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// Country pack 收集/触发服务（经 <c>IEnumerable&lt;IPayrollCountryPack&gt;</c> 注入）
/// </summary>
/// <remarks>
/// 框架不内置任何 pack；消费应用注册的 pack 由本服务列出与按 Code 触发幂等播种。
/// </remarks>
public class CountryPackService : ApplicationService, ICountryPackService
{
    private readonly IReadOnlyList<IPayrollCountryPack> _packs;

    public CountryPackService(IServiceProvider serviceProvider, IEnumerable<IPayrollCountryPack> packs)
        : base(serviceProvider)
    {
        _packs = Check.NotNull(packs).ToList();
    }

    public Task<Result<List<CountryPackDto>>> GetRegisteredAsync(CancellationToken cancellationToken = default)
    {
        var list = _packs
            .Select(p => new CountryPackDto { Code = p.Code, DisplayName = p.DisplayName, Description = p.Description })
            .OrderBy(p => p.Code)
            .ToList();
        return Task.FromResult(Ok(list));
    }

    public async Task<Result<CountryPackSeedResult>> SeedAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);

        var pack = _packs.FirstOrDefault(p => string.Equals(p.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
        if (pack == null)
            return Fail<CountryPackSeedResult>($"No country pack is registered for code '{code}'.", 404);

        return await pack.SeedAsync(cancellationToken);
    }
}
