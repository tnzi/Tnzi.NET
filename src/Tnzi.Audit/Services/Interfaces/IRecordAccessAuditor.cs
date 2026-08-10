namespace Tnzi.Audit.Services;

/// <summary>
/// 记录级读取审计：登记「谁读了哪一条数据」，并在超出配额时拦下批量导出。
/// </summary>
/// <remarks>
/// <para>
/// <strong>与请求级审计的分工。</strong><c>AuditMiddleware</c> 已经记录了每次 API 调用
/// （含读操作），但那回答的是「谁调用了哪个端点」。隐私合规追问的通常是
/// 「上个月谁看过这位举报人的材料」，端点级日志答不了。
/// </para>
/// <para>
/// <strong>需要业务代码显式调用。</strong>框架无法替你判断「哪一次查询算是读了一条敏感记录」：
/// 列表页扫过一百条与详情页打开一条，合规意义完全不同。典型调用点是详情查询与导出：
/// <code>
/// var record = await _repository.GetAsync(id);
/// // 配额超限，拒绝这次读取
/// var audit = await _auditor.RecordAsync(nameof(Tip), id.ToString(), "case-review");
/// if (!audit.Succeeded) return Fail&lt;TipDto&gt;(audit.Message!, audit.Code ?? 429);
/// </code>
/// </para>
/// <para>
/// <strong>未启用时所有方法都是空操作</strong>（返回成功），调用方无需判断开关。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "记录级审计的链结构与配额口径仍在演进")]
public interface IRecordAccessAuditor
{
    /// <summary>
    /// 登记一次记录级读取。
    /// </summary>
    /// <param name="resourceType">资源类型，建议用实体全名。</param>
    /// <param name="resourceId">被读取记录的主键（字符串形式）。</param>
    /// <param name="purpose">读取用途或场景，便于事后区分正常业务与异常访问。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 成功表示已登记；失败（HTTP 429）表示该用户已超出每小时读取配额。
    /// <strong>由调用方决定是拒绝这次读取还是仅告警</strong>，框架不替你选：
    /// 对举报平台该拒绝，对客服系统可能只该告警。
    /// </returns>
    Task<Result> RecordAsync(
        string resourceType,
        string resourceId,
        string? purpose = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验某个用户的审计链是否完整未被篡改。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 链完整时成功；发现断链时失败，消息中包含<strong>第一个</strong>校验失败的序号。
    /// </returns>
    /// <remarks>
    /// 这不能阻止有数据库权限的人改数据，但能让改动<strong>无法不留痕迹</strong>：
    /// 删改中间任意一条，其后所有条目的校验都会失败。
    /// </remarks>
    Task<Result> VerifyChainAsync(Guid? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询读取记录。
    /// </summary>
    /// <param name="query">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// <para>
    /// <strong>这是本能力存在的理由。</strong>登记只是手段，能回答
    /// 「上个月谁看过这位举报人的材料」才是目的——一张只写不读的表，
    /// 在合规问询到来时和没有这张表没有区别。
    /// </para>
    /// <para>未启用时返回空页而不是失败：调用方不必判断开关。</para>
    /// </remarks>
    Task<Result<IPagedList<RecordAccessDto>>> GetAccessesAsync(
        RecordAccessQueryDto query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按读取者汇总读取量，用于发现异常访问。
    /// </summary>
    /// <param name="startTime">统计起始时间（含），为空表示不限。</param>
    /// <param name="endTime">统计结束时间（含），为空表示不限。</param>
    /// <param name="topN">返回读取量最高的前 N 位。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 配额是<strong>事前</strong>闸门，这里是<strong>事后</strong>视角：
    /// 没超过配额但读取量是平时十倍的账号，闸门拦不住，只有把量摆在一起才看得出来。
    /// </remarks>
    Task<Result<List<RecordAccessUserStatDto>>> GetUserStatisticsAsync(
        DateTime? startTime = null,
        DateTime? endTime = null,
        int topN = 20,
        CancellationToken cancellationToken = default);
}
