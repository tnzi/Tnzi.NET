namespace Tnzi.AI.Cli.Registry;

/// <summary>
/// provider 描述的有效视图：内置表与 <c>AI:Cli</c> 配置合并后的结果。
/// </summary>
public interface ICliProviderRegistry
{
    /// <summary>全部已知 provider（含被禁用的），按声明顺序。</summary>
    IReadOnlyList<CliProviderDescriptor> GetAll();

    /// <summary>仅本部署启用的 provider。</summary>
    IReadOnlyList<CliProviderDescriptor> GetEnabled();

    /// <summary>按键取描述；未知键返回 null。</summary>
    CliProviderDescriptor? Find(string providerKey);
}
