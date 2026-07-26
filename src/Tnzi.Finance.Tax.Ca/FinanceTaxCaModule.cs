namespace Tnzi.Finance.Tax.Ca;

/// <summary>
/// 加拿大税务国家包
/// </summary>
/// <remarks>
/// **框架永不内置某一国的税表内容**——税表的行号、口径与四舍五入规则每国不同
/// 且会变，钉进框架等于让每次税改都变成一次框架升级。所以它是一个可选装的包：
/// 在加拿大报税的部署装它，其它人根本不会看见 GST34 这几个字。
///
/// 无实体、无表、无迁移：它只把既有的税务汇总（<c>IFinancialReportService</c>
/// 已经按税率维度聚合好的销项/进项）翻译成 CRA 的行号。
/// </remarks>
[DependsOn(typeof(FinanceModule))]
public class FinanceTaxCaModule : TnziCustomModule
{
    /// <summary>Recurring(59) 之后，作为国家包排在最末。</summary>
    public override int LoadOrder => 60;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // IEnumerable 注入：一个部署可以同时装多个国家包，控制器按
        // CountryCode + FormCode 查找。
        context.Services.AddScoped<ITaxReturnMapper, CraGstHstReturnMapper>();
        return Task.CompletedTask;
    }
}
