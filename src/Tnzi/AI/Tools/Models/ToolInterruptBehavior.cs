namespace Tnzi.AI.Tools.Models;

/// <summary>
/// 工具中断行为 — 当 Agent 取消时工具如何响应
/// </summary>
public enum ToolInterruptBehavior
{
    /// <summary>
    /// 立即取消（默认） — CancellationToken 立即触发
    /// </summary>
    Cancel = 0,

    /// <summary>
    /// 优雅关闭 — 给工具一个超时窗口完成清理后再取消
    /// </summary>
    GracefulShutdown = 1
}
