namespace Tnzi.AI.Tools;

/// <summary>
/// AI 工具扫描器接口
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public interface IToolScanner
{
    /// <summary>
    /// 扫描程序集，查找所有工具
    /// </summary>
    IEnumerable<ToolDefinition> ScanAssembly(Assembly assembly);
}
