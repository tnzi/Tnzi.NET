namespace Tnzi.System.Options;

/// <summary>
/// 访问日志采集选项。配置路径：<c>System:AccessLog</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>采集是 opt-in，默认关闭。</b>开了它，每个 API 请求在响应结束后往 <c>Sys_AccessLog</c> 投一行
/// （经 <c>IAccessLogSender</c> 的内存队列、由 <c>AccessLogBackgroundService</c> 成批落库并富化 IP 归属地 / UA），
/// 那是一张高频写入表，不是每个宿主默认该多出来的。关着时 <c>AccessLogMiddleware</c> 是一次判断即放行。
/// </para>
/// <para>
/// 关着时这张表可以一直是空的：查询端点、管理页与仪表盘 KPI 照常存在，只是没有数据。
/// 管理端的手工 <c>POST admin/access-logs</c>（<c>system.accessLog.create</c>）不受本开关影响。
/// </para>
/// </remarks>
[ConfigSection("System:AccessLog")]
public class AccessLogOptions
{
    /// <summary>是否采集 API 访问日志。默认 <c>false</c>。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 排除的请求路径前缀（按路径段匹配：<c>/health</c> 排除 <c>/health/live</c> 但不排除 <c>/healthcheck</c>）。
    /// 初值与 <c>Audit:ExcludedPaths</c> 相同：文档页、健康探针与 WebSocket 长连接
    /// （<c>/hubs</c> 在断开时才结束，会记成一条耗时数小时的「请求」）。
    /// </summary>
    public string[] ExcludedPaths { get; set; } = ["/swagger", "/health", "/scalar", "/hubs"];

    /// <summary>
    /// 内存队列容量（默认 5000）。队列满时<b>丢新来的一条并记账</b>：<c>AccessLogSender.DroppedCount</c> 逐条计数，
    /// 第一次与之后每 10000 次记一条 Warning。丢的是峰值处的记录 —— 统计页与趋势图在那一段少算，日志是唯一看得出来的地方。
    /// 容量在进程启动时读一次（BoundedChannel 容量运行时不可变），改它要重启。
    /// </summary>
    public int QueueCapacity { get; set; } = 5000;
}
