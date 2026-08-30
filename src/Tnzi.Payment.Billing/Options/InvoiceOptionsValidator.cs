namespace Tnzi.Payment.Billing.Options;

/// <summary>
/// 发票配置验证器。
/// </summary>
/// <remarks>
/// 这一条断言拆分前长在 <c>PaymentOptionsValidator</c> 里（<c>options.Invoice.Enabled &amp;&amp; …</c>），
/// 靠的是 <c>InvoiceOptions</c> 当时还是 <c>PaymentOptions</c> 的嵌套属性。搬到这里之后
/// 它校验的仍是同一个 <c>Payment:Invoice</c> 节、同一句错误文案，只是**改在启动期由本模块自己检**。
///
/// 为什么这一条必须跟着搬而不能留在父模块：留下就意味着父模块继续引用 <c>InvoiceOptions</c> 这个
/// 类型，那是一条父 → 子的编译期依赖，父模块就再也不可能在不加载本包时构建。
/// </remarks>
public class InvoiceOptionsValidator : OptionsValidatorBase<InvoiceOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(InvoiceOptions options, List<string> errors)
    {
        // 开着发票却没有模板名 = 每一次开票都落到内置 fallback HTML，
        // 而使用者以为自己配的模板生效了。启动期说清楚，比在第一封发票邮件里发现要早得多。
        if (options.Enabled && string.IsNullOrWhiteSpace(options.DefaultTemplate))
            errors.Add("Invoice:DefaultTemplate is required when invoice is enabled.");
    }
}
