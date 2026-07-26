namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 申报表的一行
/// </summary>
/// <param name="Line">表上的行号（如 CRA GST34 的 "101"）</param>
/// <param name="Label">行名</param>
/// <param name="Amount">金额</param>
/// <param name="IsCalculated">是否由其它行推导而来（呈现端据此区分"填的"与"算的"）</param>
public readonly record struct TaxReturnLine(string Line, string Label, decimal Amount, bool IsCalculated = false);

/// <summary>
/// 一张申报表
/// </summary>
public class TaxReturnDto
{
    /// <summary>表标识（如 "GST34"）</summary>
    public string FormCode { get; set; } = string.Empty;

    /// <summary>表名</summary>
    public string FormName { get; set; } = string.Empty;

    /// <summary>国家/地区（ISO 3166-1 alpha-2）</summary>
    public string Country { get; set; } = string.Empty;

    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public string Currency { get; set; } = string.Empty;

    public List<TaxReturnLine> Lines { get; set; } = new();

    /// <summary>净应缴（正）/ 应退（负）</summary>
    public decimal NetTax { get; set; }
}

/// <summary>
/// 把税务汇总映射成某国的申报表行（**框架永不内置某一国的税表内容**）
/// </summary>
/// <remarks>
/// 与 Payroll 路线图「country pack 插件契约，永不内置税表内容」同一铁律：税表的
/// 行号、口径、四舍五入规则每国不同且**会变**，把它们钉进框架，等于让每次税改都
/// 变成一次框架升级。
///
/// 核心只定义契约与端点；实现按国家装（`Tnzi.Finance.Tax.Ca` 提供加拿大 GST34）。
/// 未加载任何实现时端点返回 501 引导，其余税务功能（税码、税率、税务汇总报表）
/// 照常可用。
///
/// <see cref="CountryCode"/> 是路由键：一个部署可以同时装多个国家包。
/// </remarks>
public interface ITaxReturnMapper
{
    /// <summary>ISO 3166-1 alpha-2 国家码（如 "CA"）</summary>
    string CountryCode { get; }

    /// <summary>表标识（如 "GST34"）</summary>
    string FormCode { get; }

    /// <summary>按期间生成申报表行</summary>
    Task<Result<TaxReturnDto>> MapAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
