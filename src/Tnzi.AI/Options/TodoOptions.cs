namespace Tnzi.AI.Options;

/// <summary>
/// Todo/计划模式配置选项
/// </summary>
public class TodoOptions
{
    /// <summary>是否启用 Todo 中间件</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>最大 Todo 数量</summary>
    public int MaxItems { get; set; } = 50;
}
