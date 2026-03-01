namespace Tnzi.AI.Options;

/// <summary>
/// 文件对话存储配置选项
/// </summary>
public class FileConversationStoreOptions
{
    /// <summary>
    /// 项目根目录
    /// </summary>
    public string ProjectRoot { get; set; } = ".";

    /// <summary>
    /// 对话存储目录（相对于项目根目录）
    /// </summary>
    public string ConversationDirectory { get; set; } = ".tnzi/conversations";
}
