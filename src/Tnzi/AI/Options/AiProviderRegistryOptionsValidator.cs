namespace Tnzi.AI.Options;

/// <summary>
/// <see cref="AiProviderRegistryOptions"/> 验证器。
/// </summary>
/// <remarks>
/// ★校验面刻意窄于 <c>Tnzi.AI</c> 的 <c>AIOptionsValidator</c>：本验证器在**每个应用**
/// 启动时都会跑（核心无条件加载），把 Agent 层的校验搬进来会让不用 AI 的应用因为
/// 一段与它无关的配置而启动失败。零 provider 是合法配置 —— 表示这个应用不用 AI。
/// </remarks>
public class AiProviderRegistryOptionsValidator : OptionsValidatorBase<AiProviderRegistryOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(AiProviderRegistryOptions options, List<string> errors)
    {
        if (options.Providers.Count == 0)
        {
            // 未配置任何提供商 = 该应用不使用 AI，IAiUtility 调用将返回 null。
            return;
        }

        if (string.IsNullOrWhiteSpace(options.DefaultProvider))
        {
            errors.Add("AI:DefaultProvider cannot be null or empty when AI:Providers is not empty");
        }
        else if (!options.Providers.ContainsKey(options.DefaultProvider))
        {
            errors.Add($"AI:DefaultProvider '{options.DefaultProvider}' is not found in AI:Providers");
        }

        // ★ 刻意不检查 DefaultProvider 是否 Enabled（`Tnzi.AI` 的 AIOptionsValidator 会检查）：
        // 把某个提供商 Enabled 置 false 是「临时关掉 AI」的合法做法，而本验证器在**每个应用**
        // 启动时都跑。在这里 fail-fast 会让一个根本不用 AI、只是配置文件里残留 AI 节的应用
        // 升级框架后突然起不来。运行时的表现是 IAiUtility 记一条 Warning 并返回 null，
        // 且 IsAvailable 会如实报 false —— 关掉就是关掉，不是错误。

        foreach (var (providerName, providerOptions) in options.Providers)
        {
            if (!providerOptions.Enabled)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(providerOptions.BaseUrl))
            {
                if (!Uri.TryCreate(providerOptions.BaseUrl, UriKind.Absolute, out var baseUri))
                {
                    errors.Add($"AI:Providers:{providerName}:BaseUrl '{providerOptions.BaseUrl}' is not a valid absolute URI");
                }
                else if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
                {
                    errors.Add($"AI:Providers:{providerName}:BaseUrl must use the http or https scheme");
                }
            }

            if (providerOptions.TimeoutSeconds is { } timeout && (timeout <= 0 || timeout > 600))
            {
                errors.Add($"AI:Providers:{providerName}:TimeoutSeconds must be between 1 and 600");
            }

            if (providerOptions.Temperature is { } temperature && (temperature < 0 || temperature > 2))
            {
                errors.Add($"AI:Providers:{providerName}:Temperature must be between 0 and 2");
            }

            if (providerOptions.Models == null)
            {
                continue;
            }

            foreach (var (alias, modelName) in providerOptions.Models)
            {
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    errors.Add($"AI:Providers:{providerName}:Models alias '{alias}' has an empty model name");
                }
            }
        }
    }
}
