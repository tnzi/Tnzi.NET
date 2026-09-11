namespace Tnzi.AI.Options;

/// <summary>
/// <see cref="AiProviderRegistryOptions"/> 验证器。
/// </summary>
/// <remarks>
/// ★校验面刻意窄于 <c>Tnzi.AI</c> 的 <c>AIOptionsValidator</c>：本验证器在**每个应用**
/// 启动时都会跑（核心无条件加载），把 Agent 层的校验搬进来会让不用 AI 的应用因为
/// 一段与它无关的配置而启动失败。没有任何**已启用**的提供商是合法配置 —— 表示这个应用不用 AI。
/// </remarks>
public class AiProviderRegistryOptionsValidator : OptionsValidatorBase<AiProviderRegistryOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(AiProviderRegistryOptions options, List<string> errors)
    {
        // ★「这个应用用不用 AI」的判据是**有没有已启用的提供商**，不是「Providers 字典里有没有键」。
        // 两者曾被当成同一件事，而它们的差别不在理论上：配置绑定合并**所有**配置源，于是一条为同机器上
        // 另一个应用设的用户级环境变量 AI__Providers__<Name>__ApiKey，就足以让每一个 Tnzi 应用的这个
        // 字典非空。那种条目只带一个 ApiKey，Enabled 停在类型默认值 false，ResolveEnabled 永远解析不到
        // 它 —— 但「字典非空」会把一个从头到尾没提过 AI 的应用判成「在用 AI」，进而要求它声明
        // DefaultProvider，启动即失败；而那个变量是全机器的，应用自己改不掉。
        //
        // 按 Enabled 判则与运行时的解析规则同源（见 AiProviderRegistryOptions.ResolveEnabled）：
        // 一个提供商都没启用时，DefaultProvider 指向谁都解析不出东西，校验它是在校验一个没有任何
        // 运行时后果的字段 —— 那正是上面那条误报的来源。
        //
        // 代价是「全部提供商都 Enabled=false，同时 DefaultProvider 写错了」不再当场报错。这与本验证器
        // 既有的取舍一致（见下方 ★）：关掉就是关掉，不是配置错误；等到重新启用、写错的名字真的会造成
        // 影响的那一次启动，它照样 fail-fast。
        if (!options.Providers.Values.Any(provider => provider.Enabled))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.DefaultProvider))
        {
            errors.Add("AI:DefaultProvider cannot be null or empty when AI:Providers contains an enabled provider");
        }
        else if (!options.Providers.ContainsKey(options.DefaultProvider))
        {
            // ★消息带上实际存在的键名与「配置源会合并」这句话：这条错误最贵的形态不是「名字打错了」，
            // 而是「应用根本没写过 AI 配置，键是别处来的」，而原来的消息对后者只字不提。
            errors.Add(
                $"AI:DefaultProvider '{options.DefaultProvider}' is not found in AI:Providers " +
                $"(configured providers: {string.Join(", ", options.Providers.Keys)}). " +
                "AI:Providers merges every configuration source, including environment variables " +
                "named AI__Providers__<Name>__<Field>.");
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
