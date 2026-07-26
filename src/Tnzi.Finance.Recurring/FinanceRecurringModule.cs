namespace Tnzi.Finance.Recurring;

/// <summary>
/// Finance 周期性单据子模块：模板 + 排期 + 到期生成
/// </summary>
/// <remarks>
/// <b>为什么独立</b>：它带着一条后台循环、一套排期语义和三张表，而**不按周期开票
/// 的消费方一样都不需要**。会计内核不知道它存在。
/// <br/><br/>
/// <b>依赖方向</b>：单向依赖 Finance 核心，且**只经公开的单据服务建单**
/// （<c>IInvoiceService</c> / <c>IBillService</c> / <c>IExpenseService</c> 的
/// <c>CreateDraftAsync</c>），从不自己拼凭证 —— 税、汇率、账期、科目回退这些规则
/// 已经在那里，重写一遍等于让同一张发票按谁生成的而算出两个金额。
/// <br/><br/>
/// <b>表前缀沿用 <c>Finance_</c></b>：与 Banking / Payroll 同一处理，拆的是程序集不是 schema。
/// <br/><br/>
/// <b>两个可替换契约</b>：<see cref="IRecurrenceSchedule"/>（默认公历；跳周末、
/// 4-4-5 会计周历这类规则由消费方注册自己的实现整体替换）与
/// <see cref="IRecurringTenantSource"/>（后台扫描覆盖哪些租户；未注册时只扫环境
/// 上下文 —— 缺省方向必须是"少生成"）。
/// </remarks>
[DependsOn(typeof(FinanceModule))]
public class FinanceRecurringModule : TnziApplicationModule
{
    /// <inheritdoc />
    /// <remarks>与核心共享前缀：拆程序集不改表名的一贯做法。</remarks>
    public override string? TableNamePrefix => "Finance";

    /// <inheritdoc />
    /// <remarks>59：Finance(55) / Payroll·Ai(56) / Banking(57) / Documents(58) 之后。</remarks>
    public override int LoadOrder => 59;

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<RecurringOptions, RecurringOptionsValidator>(context.Configuration);
        return base.PreConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 权限码随模块走：不按周期开票的宿主不会 seed 这 5 个码。
        context.Services.AddTransient<IPermissionDefinitionProvider, FinanceRecurringPermissions>();

        // 排期是可整体替换的：跳周末、避法定假日、4-4-5 会计周历都是真实存在的
        // 需求，而把它们塞进默认实现只会让每个部署先想办法把它关掉。
        context.Services.TryAddScoped<IRecurrenceSchedule, CalendarRecurrenceSchedule>();

        context.Services.AddScoped<RecurringDocumentBuilder>();
        context.Services.AddScoped<IRecurringDocumentService, RecurringDocumentService>();
        context.Services.AddScoped<IRecurringGeneratorService, RecurringGeneratorService>();

        context.Services.AddHostedService<RecurringGenerationBackgroundService>();

        return Task.CompletedTask;
    }
}
