namespace Tnzi.AI.Options;

/// <summary>
/// Thread 行为配置，绑定 AI:Thread 配置节
/// </summary>
public class ThreadOptions
{
    /// <summary>
    /// 是否在首轮对话后自动 AI 生成线程标题（默认关闭）
    /// </summary>
    public bool AutoGenerateTitle { get; set; }

    /// <summary>
    /// 生成/截取标题的最大字符长度
    /// </summary>
    public int TitleMaxLength { get; set; } = 50;
}
