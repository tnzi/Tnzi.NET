namespace Tnzi.AI.Services;

/// <summary>
/// 给 <see cref="AiUtilityHttpClientNames"/> 名下的<b>每一个</b>命名客户端关掉 <see cref="HttpClient.Timeout"/>，
/// 超时交给 <see cref="OpenAiCompatibleAiUtility"/> 的每次尝试超时（<c>TimeoutSeconds</c>，未配为 100 秒）。
/// </summary>
/// <remarks>
/// <para>
/// ★ 按名字规则而不是按启动时的配置清单逐个注册：<see cref="AiUtilityHttpClientNames.For"/> 对任何提供商名都给出
/// 一个具名客户端，热重载新增的提供商拿到的是启动时没登记过的名字。逐个注册时，这种名字取到的是带默认 100 秒
/// <see cref="HttpClient.Timeout"/> 的裸客户端，<c>TimeoutSeconds</c> 超过 100 被静默截断。
/// </para>
/// <para>
/// 选项配置按注册顺序执行，本配置由核心模块最先注册：消费方自己给这些客户端设的 <c>Timeout</c> 排在后面，仍然生效。
/// </para>
/// </remarks>
internal sealed class AiUtilityHttpClientTimeoutConfigurator : IConfigureNamedOptions<HttpClientFactoryOptions>
{
    public void Configure(string? name, HttpClientFactoryOptions options)
    {
        if (!IsAiUtilityClient(name))
        {
            return;
        }

        options.HttpClientActions.Add(client => client.Timeout = Timeout.InfiniteTimeSpan);
    }

    public void Configure(HttpClientFactoryOptions options) => Configure(Microsoft.Extensions.Options.Options.DefaultName, options);

    internal static bool IsAiUtilityClient(string? name) =>
        name is not null
        && (name == AiUtilityHttpClientNames.Fallback
            || name == AiUtilityHttpClientNames.Inline
            || name.StartsWith(AiUtilityHttpClientNames.Fallback + ":", StringComparison.Ordinal));
}
