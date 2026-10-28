namespace Tnzi.Storage.Workspace.Dtos;

/// <summary>
/// 管理员给某个用户设的配额覆盖值。没有覆盖值的用户用 <c>Storage:Quota:DefaultUserAllowanceBytes</c>；
/// 用户的实际用量见 <c>GET admin/files/quota/{userId}</c>。
/// </summary>
public class StorageAllowanceDto
{
    /// <summary>用户 ID。</summary>
    public Guid UserId { get; set; }

    /// <summary>这个用户可用的字节数；<c>null</c> = 不限。</summary>
    public long? AllowanceBytes { get; set; }

    /// <summary>备注。</summary>
    public string? Note { get; set; }

    /// <summary>设置时间。</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>最后修改时间。</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 设置某个用户的配额覆盖值。
/// </summary>
public class SetStorageAllowanceRequest
{
    /// <summary>可用的字节数；<c>null</c> = 这个用户不限（即便默认值有上限）。不得为负。</summary>
    [Range(0, long.MaxValue)]
    public long? AllowanceBytes { get; set; }

    /// <summary>备注（为什么给他这么多），只给管理员看。</summary>
    [MaxLength(512)]
    public string? Note { get; set; }
}

/// <summary>
/// 配额覆盖值列表的分页参数。
/// </summary>
public class StorageAllowanceQueryDto : PagedQueryDto
{
}
