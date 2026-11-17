namespace Tnzi.AI.Metadata;

/// <summary>
/// 线程标题从哪来，决定自动标题还能不能改它。
/// </summary>
/// <remarks>
/// 自动标题（回退标题与 AI 标题）只覆盖 <see cref="Placeholder"/> / <see cref="Fallback"/> / <see cref="Generating"/>；
/// <see cref="User"/> 是钉住的：用户改过名之后，任何自动标题都不再替换它。
/// 写入一律是带这个条件的单条 <c>UPDATE</c>，用户改名与后台起标题同时发生时谁后到谁说了算，不会出现「改名被 AI 标题盖掉」。
/// </remarks>
public enum ThreadTitleSource
{
    /// <summary>建线程时的占位标题（或本列出现之前就有的线程）。</summary>
    Placeholder = 0,

    /// <summary>取自第一条用户消息的回退标题，且不会再有 AI 标题（没开自动标题，或生成失败）。</summary>
    Fallback = 1,

    /// <summary>回退标题已写下，AI 标题正在生成；客户端据此知道标题还会变。</summary>
    Generating = 2,

    /// <summary>AI 生成的标题。</summary>
    Generated = 3,

    /// <summary>用户自己起的名字：钉住，自动标题不再替换。</summary>
    User = 4
}
