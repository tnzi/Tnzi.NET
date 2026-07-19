namespace Tnzi.AI.Metadata;

/// <summary>
/// 上下文提供器执行顺序常量 — 数值越小越先执行
/// </summary>
public static class ContextProviderOrders
{
    /// <summary>记忆上下文（最先注入）</summary>
    public const int Memory = -100;

    /// <summary>RAG 文本搜索</summary>
    public const int Rag = 0;

    /// <summary>技能上下文</summary>
    public const int Skills = 100;

    /// <summary>自定义上下文提供器（用户扩展）</summary>
    public const int Custom = 200;
}
