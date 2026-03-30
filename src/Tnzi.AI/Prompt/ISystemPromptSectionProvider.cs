namespace Tnzi.AI.Prompt;

/// <summary>
/// 系统提示词段落提供器 — 动态提供提示词段落
/// </summary>
public interface ISystemPromptSectionProvider
{
    /// <summary>
    /// 获取提示词段落（返回 null 表示不注入）
    /// </summary>
    SystemPromptSection? GetSection();
}
