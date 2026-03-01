namespace Tnzi.AI.ProjectContext;

/// <summary>
/// 项目上下文加载器接口 — 加载项目指令文件
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public interface IProjectContextLoader
{
    /// <summary>
    /// 加载项目上下文（指令文件、元数据）
    /// </summary>
    /// <param name="projectRoot">项目根目录（为空时使用配置默认值）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>项目上下文</returns>
    Task<ProjectContextData> LoadAsync(string? projectRoot = null, CancellationToken ct = default);
}

/// <summary>
/// 项目上下文数据
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public class ProjectContextData
{
    /// <summary>
    /// 合并后的指令内容
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// 项目根目录（绝对路径）
    /// </summary>
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// 已加载的文件列表
    /// </summary>
    public List<string> LoadedFiles { get; set; } = [];

    /// <summary>
    /// 从 YAML frontmatter 解析的元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
