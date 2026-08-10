namespace Tnzi.Audit.Dtos;

/// <summary>
/// 记录级读取审计条目。
/// </summary>
public class RecordAccessDto
{
    /// <summary>条目 ID。</summary>
    public Guid Id { get; set; }

    /// <summary>该用户链条内的序号。</summary>
    public long Sequence { get; set; }

    /// <summary>被读取的资源类型。</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>被读取记录的主键。</summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>读取用途或场景。</summary>
    public string? Purpose { get; set; }

    /// <summary>读取者用户 ID。</summary>
    public Guid? UserId { get; set; }

    /// <summary>读取者用户名（冗余保存，用户改名后仍能追回当时是谁）。</summary>
    public string? UserName { get; set; }

    /// <summary>本条哈希。</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>读取发生的时间。</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 记录级读取审计查询条件。
/// </summary>
/// <remarks>
/// 两个主要查询方向都要支持：
/// <list type="bullet">
///   <item>「<strong>这条记录</strong>都被谁看过」——按 <c>ResourceType</c> + <c>ResourceId</c>，
///     这是隐私合规最常被追问的问题；</item>
///   <item>「<strong>这个人</strong>看过哪些记录」——按 <c>UserId</c>，
///     账号被盗或内部人员异常访问的排查方向。</item>
/// </list>
/// </remarks>
public class RecordAccessQueryDto : PagedQueryDto
{
    /// <summary>按资源类型过滤（大小写不敏感）。</summary>
    public string? ResourceType { get; set; }

    /// <summary>按被读取记录的主键过滤。</summary>
    public string? ResourceId { get; set; }

    /// <summary>按读取者过滤。</summary>
    public Guid? UserId { get; set; }

    /// <summary>按用途过滤（大小写不敏感）。</summary>
    public string? Purpose { get; set; }

    /// <summary>起始时间（含）。</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（含）。</summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 某个用户的读取量统计，用于发现异常访问。
/// </summary>
/// <remarks>
/// 配额（<c>MaxReadsPerUserPerHour</c>）是<strong>事前</strong>闸门，本统计是<strong>事后</strong>视角：
/// 没有超过配额但读取量是平时十倍的账号，闸门拦不住，只有把量摆在一起才看得出来。
/// </remarks>
public class RecordAccessUserStatDto
{
    /// <summary>读取者用户 ID。</summary>
    public Guid? UserId { get; set; }

    /// <summary>读取者用户名。</summary>
    public string? UserName { get; set; }

    /// <summary>统计区间内的读取次数。</summary>
    public int AccessCount { get; set; }

    /// <summary>读取涉及的不同记录数。</summary>
    public int DistinctRecordCount { get; set; }

    /// <summary>区间内最后一次读取的时间。</summary>
    public DateTime LastAccessTime { get; set; }
}
