﻿using System.Collections.Concurrent;

namespace Tnzi.Payment.Stripe.Providers;

/// <summary>
/// 由 <see cref="StripeOptions"/> 构造 <see cref="StripeClient"/> 的单一出口。
/// </summary>
/// <remarks>
/// 拆分之前，需要 Stripe 客户端的第二处（促销同步）是从 <c>StripeProvider</c> 的 internal 访问器
/// 拿的 —— 那条路跨不过程序集边界，也把「优惠券同步」绑在了「支付渠道实例」上。
/// 现在两处各自从配置构造，「哪个配置项是 Stripe 凭据」这个问题只有这一个答案。
/// </remarks>
internal static class StripeClientFactory
{
    /// <summary>
    /// 按密钥缓存的客户端。<c>StripeClient</c> 是线程安全的、设计上就该长期复用 ——
    /// 它内部持有连接池；而 <c>StripeProvider</c> 是 Scoped，缓存在实例字段上等于每个请求
    /// 重建一次，连接池随之作废，高峰期表现为端口耗尽而不是任何一条错误日志。
    /// 按密钥分桶而不是只存一份：密钥轮换后自然拿到新客户端，旧的那个随桶一起被丢掉。
    /// </summary>
    private static readonly ConcurrentDictionary<string, StripeClient> Clients = new(StringComparer.Ordinal);

    /// <summary>按配置取一个 Stripe 客户端（同一密钥全进程共用一个）。</summary>
    internal static StripeClient Create(StripeOptions options)
    {
        Check.NotNull(options);
        return Clients.GetOrAdd(options.SecretKey ?? string.Empty, static key => new StripeClient(key));
    }

    /// <summary>
    /// 把 <paramref name="client"/> 登记为 <paramref name="secretKey"/> 对应的客户端（测试用）。
    /// </summary>
    /// <remarks>
    /// 唯一的用途是让测试塞进一个走桩 <c>HttpClient</c> 的 <see cref="StripeClient"/>，
    /// 从而不触网地跑完绑卡归属校验这类要往返两次 Stripe 的路径。每个测试用自己的假密钥，互不干扰。
    /// </remarks>
    internal static void Use(string secretKey, StripeClient client)
    {
        Check.NotNullOrWhiteSpace(secretKey);
        Check.NotNull(client);
        Clients[secretKey] = client;
    }
}
