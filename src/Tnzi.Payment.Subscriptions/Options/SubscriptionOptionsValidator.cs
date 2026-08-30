namespace Tnzi.Payment.Subscriptions.Options;

/// <summary>
/// 订阅配置验证器。
/// </summary>
/// <remarks>
/// 这五条断言拆分前长在 <c>PaymentOptionsValidator</c> 里（<c>options.Subscription.…</c>），
/// 靠的是 <see cref="SubscriptionOptions"/> 当时还是 <c>PaymentOptions</c> 的嵌套属性。
/// 搬到这里之后它们校验的仍是同一个 <c>Payment:Subscription</c> 节、同一批错误文案，
/// 只是**改在启动期由本模块自己检**。
///
/// 为什么必须跟着搬而不能留在父模块：留下就意味着父模块继续引用 <see cref="SubscriptionOptions"/>
/// 这个类型，那是一条父 → 子的编译期依赖，父模块就再也不可能在不加载本包时构建。
/// 顺带一个好处：不做续费的宿主不再校验一段没人读的配置。
/// </remarks>
public class SubscriptionOptionsValidator : OptionsValidatorBase<SubscriptionOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(SubscriptionOptions options, List<string> errors)
    {
        // 错误文案保持 "Subscription:Xxx" 的前缀不变：拆分前它是嵌套属性路径，
        // 拆分后它正好也是 Payment:Subscription 节内的键名，运维看到的字符串一字未改。
        if (options.AutoRenewalReminderDays < 0)
            errors.Add("Subscription:AutoRenewalReminderDays cannot be negative.");

        if (options.GracePeriodDays < 0)
            errors.Add("Subscription:GracePeriodDays cannot be negative.");

        if (options.MaxRetryCount < 0)
            errors.Add("Subscription:MaxRetryCount cannot be negative.");

        if (options.DefaultTrialDays < 0)
            errors.Add("Subscription:DefaultTrialDays cannot be negative.");

        if (options.MaxPauseDays < 0)
            errors.Add("Subscription:MaxPauseDays cannot be negative.");
    }
}
