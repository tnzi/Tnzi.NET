namespace Tnzi.Storage.Dtos;

/// <summary>
/// 一个用户的存储配额：已用多少、可用多少。
/// </summary>
/// <remarks>
/// <see cref="UsedBytes"/> 与 <see cref="UserStorageUsage.TotalSize"/> 不是同一个数：后者只数文件记录，
/// 前者再加上子模块为这个用户保留的字节（加载 <c>Tnzi.Storage.Workspace</c> 时：文件的历史版本）。
/// 配额按前者判定。
/// </remarks>
public class StorageQuotaDto
{
    /// <summary>用户 ID。</summary>
    public Guid UserId { get; set; }

    /// <summary>名下文件记录的条数（含临时上传与回收站里尚未清除的文件）。</summary>
    public int FileCount { get; set; }

    /// <summary>已用字节数，配额按它判定。</summary>
    public long UsedBytes { get; set; }

    /// <summary>可用字节数；<c>null</c> = 不限。</summary>
    public long? AllowanceBytes { get; set; }

    /// <summary>还能再存多少字节；不限时为 <c>null</c>，已超出时为 0。</summary>
    public long? RemainingBytes => AllowanceBytes is { } allowance ? Math.Max(0, allowance - UsedBytes) : null;

    /// <summary>已用是否已经超过可用（管理员调低配额之后会出现）。</summary>
    public bool IsExceeded => AllowanceBytes is { } allowance && UsedBytes > allowance;
}

/// <summary>
/// 配额拒绝（<c>FILE_QUOTA_EXCEEDED</c>）随失败结果带出的 <c>errorDetails</c>：界面据此说清楚差多少，
/// 不必再单独查一次配额。
/// </summary>
public class StorageQuotaExceededDetails
{
    /// <summary>写入之前已用的字节数。</summary>
    public long UsedBytes { get; set; }

    /// <summary>这次写入要新增的字节数。</summary>
    public long RequestedBytes { get; set; }

    /// <summary>可用的字节数。</summary>
    public long AllowanceBytes { get; set; }
}
