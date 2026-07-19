namespace Tnzi.Finance.Payroll.Services.Interfaces;

/// <summary>
/// 国家/地区薪酬包契约（法定组件模板 + 税级表结构的幂等播种入口）
/// </summary>
/// <remarks>
/// 消费应用实现并注册；<see cref="ICountryPackService"/> 经 <c>IEnumerable</c> 收集、按 Code 触发。
/// **框架永不内置 pack 实现**——税级表数值属申报口径，随法规变动，只由消费方提供或管理员手录。
/// pack 自行注入 <c>ISalaryComponentService</c>/<c>IBracketTableService</c> 完成 upsert。
/// </remarks>
public interface IPayrollCountryPack
{
    /// <summary>国家/地区代码（如 "US" / "CA" / "CN"；大小写不敏感匹配）</summary>
    string Code { get; }

    /// <summary>显示名</summary>
    string DisplayName { get; }

    /// <summary>说明</summary>
    string? Description => null;

    /// <summary>幂等播种（按 Code upsert 法定组件模板与税级表）</summary>
    Task<Result<CountryPackSeedResult>> SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Country pack 收集/触发服务
/// </summary>
public interface ICountryPackService
{
    /// <summary>列出已注册的 country pack</summary>
    Task<Result<List<CountryPackDto>>> GetRegisteredAsync(CancellationToken cancellationToken = default);

    /// <summary>按 Code 触发某 pack 的幂等播种（未注册返回 404）</summary>
    Task<Result<CountryPackSeedResult>> SeedAsync(string code, CancellationToken cancellationToken = default);
}
