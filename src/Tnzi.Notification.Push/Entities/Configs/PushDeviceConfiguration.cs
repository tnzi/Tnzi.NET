namespace Tnzi.Notification.Push.Entities.Configs;

/// <summary>
/// <see cref="PushDevice"/> 的 EF 映射。
/// </summary>
public class PushDeviceConfiguration : EntityTypeConfigurationBase<PushDevice, Guid>
{
    /// <summary>
    /// 令牌列宽。★ <b>必须不大于 <c>Notification_Recipient.Address</c> 的 500。</b>
    /// </summary>
    /// <remarks>
    /// 投递时令牌被抄进 <c>Recipient.Address</c>。本列若更宽，一个存得进本表却塞不进那一列的
    /// 令牌会在落库时抛异常 —— 而那一次 <c>SaveChanges</c> 承载的是整批收件人的状态，
    /// 于是<b>已经发出去</b>的那些退回 <c>Pending</c>，续发时再发一遍。同一形态见
    /// <c>NotificationFieldLimits</c> 的说明。
    /// <para>
    /// 实测宽度：FCM 注册令牌约 163 字符，APNs 令牌 64 位十六进制；500 有充足余量。
    /// 这里不复用父模块的 <c>NotificationFieldLimits</c>：那是 <c>internal</c> 的，
    /// 而为一个常量开 <c>InternalsVisibleTo</c> 会把父模块的内部面永久暴露给本包。
    /// 约束改由 <c>PushDeviceTokenWidthTests</c> 断言，不靠这段注释。
    /// </para>
    /// </remarks>
    public const int TokenMaxLength = 500;

    /// <summary>设备名列宽。</summary>
    public const int DeviceNameMaxLength = 200;

    /// <summary>
    /// 设备密钥哈希的列宽：取自 <see cref="OneTimeToken.HashLength"/>（SHA-256 的 64 个十六进制字符）。
    /// </summary>
    /// <remarks>
    /// 引用那个常量而不是写死 64，理由写在它自己的文档里：建表时列宽按它取，
    /// 不要凭记忆写 128 或 256。
    /// </remarks>
    public const int DeviceKeyHashMaxLength = OneTimeToken.HashLength;

    /// <summary>
    /// 客户端自报的平台设备标识的列宽。
    /// </summary>
    /// <remarks>
    /// 128 装得下 UUID（36）、Firebase 安装 ID（22）与 Android SSAID（16），还留了余量给
    /// 别家平台的形态。它只是个辨认用的字符串，不需要与任何别的列对齐宽度。
    /// </remarks>
    public const int ExternalDeviceIdMaxLength = 128;

    public override void Configure(EntityTypeBuilder<PushDevice> builder)
    {
        builder.Property(d => d.Token).IsRequired().HasMaxLength(TokenMaxLength);
        builder.Property(d => d.DeviceName).HasMaxLength(DeviceNameMaxLength);
        builder.Property(d => d.DeviceKeyHash).HasMaxLength(DeviceKeyHashMaxLength);
        builder.Property(d => d.ExternalDeviceId).HasMaxLength(ExternalDeviceIdMaxLength);

        // ★ 普通索引，**不是**唯一索引：这一列可冒名、在 iOS 上还会被同 vendor 的多个 App
        // 共享，而且它不参与寻址。建索引只为运维能按它查得动，见实体上的说明。
        builder.HasIndex(d => d.ExternalDeviceId);

        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled)
        {
            builder.HasIndex(d => d.TenantId);
            // 令牌在租户内唯一。跨租户不做唯一：同一台设备可以分别注册进两个租户，
            // 而把它们判成冲突会让后注册的那个租户静默收不到推送。
            builder.HasIndex(d => new { d.TenantId, d.Token }).IsUnique();
            builder.HasIndex(d => new { d.TenantId, d.UserId });
            builder.HasIndex(d => new { d.TenantId, d.DeviceKeyHash })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(PushDevice.DeviceKeyHash)));
        }
        else
        {
            // HasFilter(null) 显式关掉 SqlServer provider 对唯一索引自动补的
            // 「WHERE 列 IS NOT NULL」。Token 是必填列，这一句实际是空操作，
            // 写出来是为了让「本表没有软删除、索引不带任何谓词」是读得出来的，
            // 与 UserPresenceConfiguration 同一写法。
            builder.HasIndex(d => d.Token).IsUnique().HasFilter(null);
            builder.HasIndex(d => d.UserId);

            // ★ 设备密钥哈希的唯一索引**必须**带非空过滤器，这不是可选的优化。
            // 登录设备的这一列全是 NULL，而各家数据库对唯一索引里的 NULL 判定不同：
            // PostgreSQL / SQLite 认为 NULL 互不相等（多少行都行），SQL Server 认为
            // NULL 彼此相等（只许一行）。不带过滤器时，第二台纯登录设备在 SQL Server 上
            // 直接插不进去，而在 PostgreSQL 上跑得好好的 —— 「在另一个库上是好的」
            // 正是这类缺陷最擅长的伪装。见 IndexFilterFactory.GetColumnNull 的说明。
            builder.HasIndex(d => d.DeviceKeyHash)
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNull(nameof(PushDevice.DeviceKeyHash)));
        }
    }
}
