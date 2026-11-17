namespace Tnzi.AI.Cli.Adapters;

/// <summary>
/// 一个协议适配器能停下进程里的单个任务（子 agent、后台命令），而不停整条运行（stream-json：claude 的 <c>stop_task</c> 控制请求，2.1.308 起）。
/// </summary>
/// <remarks>
/// 停下之后 CLI 照常为那个任务发一条 <c>task_notification</c>（<c>status: stopped</c>），任务事件与界面都从那里得知结局；
/// 这里只负责把请求写给 CLI。没有这个能力的协议（ACP）只能停整条运行。
/// </remarks>
public interface ICliTaskControl
{
    /// <summary>请 CLI 停下任务 <paramref name="taskId"/>（CLI 给的 <c>task_id</c>）。</summary>
    /// <returns>false：这个进程已经不再读（运行已结束、还没开始），什么都没写。</returns>
    Task<bool> StopTaskAsync(string taskId, CancellationToken cancellationToken);
}
