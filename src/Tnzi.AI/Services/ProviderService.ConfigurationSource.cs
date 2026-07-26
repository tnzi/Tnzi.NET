namespace Tnzi.AI.Services;

/// <summary>
/// ProviderService 配置来源（appsettings <c>AI:Providers</c>）支持。
/// </summary>
/// <remarks>
/// 配置条目以只读形式合并进 admin 列表/详情/options 端点（<c>Source=Configuration</c>），
/// 解决"agent 实际使用的 provider 来自配置文件，但 admin 列表只显示数据库实体"的可见性断层。
/// 安全约束：配置中的 <c>ApiKey</c> 明文绝不进入任何 DTO - 仅以 <c>HasApiKey</c> 布尔标志暴露。
/// </remarks>
public partial class ProviderService
{
    /// <summary>配置条目合成 ID 的派生种子前缀（与名称小写拼接后取 MD5 → Guid）。</summary>
    private const string ConfigurationIdSeedPrefix = "Tnzi.AI.ConfigurationProvider:";

    /// <summary>对配置条目执行 Update/Delete 时返回的统一说明。</summary>
    internal const string ConfigurationProviderReadOnlyMessage =
        "This provider is defined in application configuration (AI:Providers) and is read-only. " +
        "Edit the configuration file, or create a database provider with the same name to override it.";

    /// <summary>
    /// 为配置来源 Provider 派生确定性合成 ID（名称大小写不敏感）。
    /// 同名条目在所有请求间 ID 稳定，前端可安全用作 rowKey / 详情路由；
    /// 该 ID 不会与数据库随机/顺序 GUID 冲突（MD5 派生空间独立）。
    /// </summary>
    public static Guid GetConfigurationProviderId(string name)
    {
        Check.NotNullOrWhiteSpace(name);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(ConfigurationIdSeedPrefix + name.Trim().ToLowerInvariant()));
        return new Guid(hash);
    }

    /// <summary>判断给定 ID 是否对应某个配置条目。</summary>
    private bool IsConfigurationProviderId(Guid id)
    {
        var providers = _aiOptionsMonitor?.CurrentValue?.Providers;
        if (providers == null || providers.Count == 0)
        {
            return false;
        }

        return providers.Keys.Any(name => !string.IsNullOrWhiteSpace(name) && GetConfigurationProviderId(name) == id);
    }

    /// <summary>按合成 ID 解析配置条目 DTO；未命中返回 false。</summary>
    private bool TryGetConfigurationProvider(Guid id, out ProviderDto? dto)
    {
        dto = BuildConfigurationProviderDtos().FirstOrDefault(p => p.Id == id);
        return dto != null;
    }

    /// <summary>按合成 ID 解析配置条目的模型列表探针（含明文 ApiKey，仅驻留内存）。</summary>
    private bool TryGetConfigurationModelProbe(Guid id, out ModelProbe? probe)
    {
        probe = null;
        var providers = _aiOptionsMonitor?.CurrentValue?.Providers;
        if (providers == null || providers.Count == 0)
        {
            return false;
        }

        foreach (var (name, opts) in providers)
        {
            if (string.IsNullOrWhiteSpace(name) || opts == null || GetConfigurationProviderId(name) != id)
            {
                continue;
            }

            probe = new ModelProbe(name, InferProviderType(name), opts.BaseUrl, opts.ApiKey, opts.DefaultModel);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 将 <c>AI:Providers</c> 配置字典映射为只读 ProviderDto 列表（按名称排序）。
    /// ApiKey 仅以 <c>HasApiKey</c> 布尔标志体现，明文/密文永不出现在 DTO 中。
    /// </summary>
    private List<ProviderDto> BuildConfigurationProviderDtos()
    {
        var providers = _aiOptionsMonitor?.CurrentValue?.Providers;
        if (providers == null || providers.Count == 0)
        {
            return [];
        }

        var result = new List<ProviderDto>(providers.Count);
        foreach (var (name, opts) in providers)
        {
            if (string.IsNullOrWhiteSpace(name) || opts == null)
            {
                continue;
            }

            result.Add(new ProviderDto
            {
                Id = GetConfigurationProviderId(name),
                Name = name,
                ProviderType = InferProviderType(name),
                Endpoint = opts.BaseUrl,
                DefaultModel = opts.DefaultModel,
                Priority = 0,
                IsEnabled = opts.Enabled,
                Description = "Defined in application configuration (AI:Providers); read-only.",
                HasApiKey = !string.IsNullOrEmpty(opts.ApiKey),
                Scope = ResourceScope.System,
                TenantId = null,
                Source = ProviderSources.Configuration,
                CreationTime = null,
                LastModificationTime = null
            });
        }

        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 在内存中对配置条目应用与数据库查询等价的过滤（ProviderType / IsEnabled / Keyword）。
    /// </summary>
    private static List<ProviderDto> FilterConfigurationDtos(List<ProviderDto> dtos, ProviderQueryDto query)
    {
        var keyword = query.Keyword?.Trim();
        var providerType = query.ProviderType?.Trim();

        return dtos.Where(p =>
                (string.IsNullOrEmpty(providerType) || string.Equals(p.ProviderType, providerType, StringComparison.OrdinalIgnoreCase)) &&
                (!query.IsEnabled.HasValue || p.IsEnabled == query.IsEnabled.Value) &&
                (string.IsNullOrEmpty(keyword)
                    || p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (p.Description != null && p.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    /// <summary>
    /// 从配置字典 key（provider 名称）推断展示用 ProviderType。
    /// 配置条目没有显式类型字段 - ChatClientFactory 按名称精确匹配 IChatClientProvider，
    /// 未命中时回退 OpenAI 兼容协议，此处的启发式与该行为保持一致。
    /// </summary>
    private static string InferProviderType(string providerName)
    {
        var name = providerName.ToLowerInvariant();
        if (name.Contains("anthropic") || name.Contains("claude")) return "Anthropic";
        if (name.Contains("azure")) return "AzureOpenAI";
        if (name.Contains("deepseek")) return "DeepSeek";
        if (name.Contains("gemini") || name.Contains("google")) return "Google";
        if (name.Contains("ollama")) return "Ollama";
        if (name.Contains("openai")) return "OpenAI";
        return "OpenAI-Compatible";
    }
}
