namespace Tnzi.Payment.Subscriptions.Mappings;

/// <summary>
/// 订阅模块映射配置。
/// </summary>
/// <remarks>
/// 规则与拆分前<b>一字不变</b>，只是从父模块的 <c>PaymentMappingConfig</c> 里挪了出来 ——
/// 它引用 <see cref="Subscription"/> 与 <see cref="SubscriptionDto"/> 两个类型，
/// 留在父模块就是一条父 → 子的编译期依赖。
/// <see cref="IMappingConfig"/> 是按已加载程序集发现的，因此不加载本包时这条规则一并不存在。
/// </remarks>
public class SubscriptionMappingConfig : IMappingConfig
{
    /// <summary>
    /// 配置映射
    /// </summary>
    public void Configure(IMappingConfigContext context)
    {
        // 订阅实体映射：映射关联的 PlanName；
        // HasPaymentMethod 由 token 是否存在推导（可翻译成 SQL，列表查询走 ProjectTo 也成立），
        // 前端据此提示"未绑卡将无法自动续费"。
        context.NewConfig<Subscription, SubscriptionDto>()
            .Map(dest => dest.PlanName, src => src.Plan != null ? src.Plan.PlanName : null)
            .Map(dest => dest.HasPaymentMethod, src => src.PaymentMethodToken != null);
    }
}
