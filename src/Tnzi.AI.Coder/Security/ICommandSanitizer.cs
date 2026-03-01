namespace Tnzi.AI.Coder.Security;

/// <summary>
/// 命令消毒接口 — 检查 shell 命令是否安全
/// </summary>
public interface ICommandSanitizer
{
    /// <summary>
    /// 检查命令安全性
    /// </summary>
    /// <param name="command">待检查的命令</param>
    /// <returns>消毒结果</returns>
    CommandSanitizeResult Sanitize(string command);
}

/// <summary>
/// 命令消毒结果
/// </summary>
public class CommandSanitizeResult
{
    /// <summary>
    /// 是否允许执行
    /// </summary>
    public bool IsAllowed { get; set; }

    /// <summary>
    /// 是否需要审批
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// 原因说明
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 允许执行
    /// </summary>
    public static CommandSanitizeResult Allow() => new() { IsAllowed = true };

    /// <summary>
    /// 拒绝执行
    /// </summary>
    public static CommandSanitizeResult Deny(string reason) => new() { IsAllowed = false, Reason = reason };

    /// <summary>
    /// 需要审批
    /// </summary>
    public static CommandSanitizeResult NeedsApproval(string reason) => new() { IsAllowed = true, RequiresApproval = true, Reason = reason };
}
