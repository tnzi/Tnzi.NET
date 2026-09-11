namespace Tnzi.SignalR.Services;

/// <summary>
/// 按 connectionId 强制中断实时连接。
///
/// ★ 为什么需要它：SignalR 服务端没有"按用户 id 断开"的能力，能中断一条连接的只有
/// 该连接自己的 <see cref="HubCallerContext.Abort"/>。<see cref="IConnectionManager"/>
/// 记的是"谁有哪些连接",清掉那份登记**不会**动到底层传输 —— 套接字照常连着、照常收
/// 广播,只是从管理界面上消失了。所以需要一份 connectionId → <c>HubCallerContext</c>
/// 的登记表，由 <c>TnziHub</c> 在连接生命周期两端维护。
///
/// ★ 与 <see cref="IConnectionManager"/> 一样是**进程内**的：多实例部署时，本实例只
/// 断得掉挂在自己身上的连接。参见 docs/modules/signalr.md 的多实例一节。
/// </summary>
public interface IHubConnectionAborter
{
    /// <summary>
    /// 登记一条连接。同一 connectionId 重复登记以最后一次为准。
    /// </summary>
    /// <param name="connectionId">连接 ID</param>
    /// <param name="context">该连接的 Hub 调用上下文</param>
    void Register(string connectionId, HubCallerContext context);

    /// <summary>
    /// 注销一条连接（连接正常断开时调用）。
    /// </summary>
    /// <param name="connectionId">连接 ID</param>
    void Unregister(string connectionId);

    /// <summary>
    /// 中断一条连接。
    /// </summary>
    /// <param name="connectionId">连接 ID</param>
    /// <returns>该连接在本实例上存在且已发出中断时为 true</returns>
    bool Abort(string connectionId);

    /// <summary>
    /// 批量中断，返回实际中断的条数。
    /// </summary>
    /// <param name="connectionIds">连接 ID 集合</param>
    /// <returns>实际中断的条数（本实例上不存在的不计入）</returns>
    int AbortRange(IEnumerable<string> connectionIds);

    /// <summary>
    /// 本实例上已登记的连接数（诊断用）。
    /// </summary>
    int RegisteredCount { get; }
}
