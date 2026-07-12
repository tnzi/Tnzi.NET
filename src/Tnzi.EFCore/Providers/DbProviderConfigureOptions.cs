
namespace Tnzi.EFCore.Providers;

/// <summary>
/// 传递给数据库提供者配置器的连接级选项（重试策略、命令超时）。
/// 由 <see cref="Tnzi.EFCore.Options.DbContextConfiguration"/> 的对应字段构建，
/// 在反射调用 <c>Use{Provider}(builder, connectionString, action)</c> 时应用到 provider 的 options builder。
/// </summary>
/// <remarks>
/// 在此之前，自动发现的 DbContext 无任何入口配置 EnableRetryOnFailure / CommandTimeout
/// （配置器反射调用时第三参硬编码为 null）。
/// </remarks>
public sealed class DbProviderConfigureOptions
{
    /// <summary>
    /// 是否启用瞬时错误重试（retrying execution strategy）。
    /// ⚠️ 启用后与框架 UnitOfWork 的手动事务互斥：UoW 的手动 BeginTransaction 会在运行时抛异常。
    /// SQLite 无重试策略，此项对 SQLite 无效。
    /// </summary>
    public bool EnableRetryOnFailure { get; init; }

    /// <summary>
    /// 最大重试次数。为 null 时使用 provider 默认值（EF Core 默认 6）。
    /// 仅当 <see cref="EnableRetryOnFailure"/> 为 true 时生效。
    /// </summary>
    public int? MaxRetryCount { get; init; }

    /// <summary>
    /// 数据库命令超时（秒）。为 null 时使用 provider 默认值。
    /// </summary>
    public int? CommandTimeout { get; init; }

    /// <summary>
    /// 空选项（不改变任何 provider 默认行为，等价于旧的传 null 行为）。
    /// </summary>
    public static DbProviderConfigureOptions None { get; } = new();

    /// <summary>
    /// 是否存在任何需要应用到 provider options builder 的设置。
    /// 无任何设置时配置器保持传 null，与升级前行为完全一致。
    /// </summary>
    public bool HasAny => EnableRetryOnFailure || CommandTimeout.HasValue;

    /// <summary>
    /// 是否需要向使用方发出"重试策略与 UnitOfWork 手动事务互斥"的告警。
    /// 启用重试型 execution strategy 后，框架 UoW 的手动 BeginTransaction 会抛异常。
    /// </summary>
    public bool ConflictsWithUnitOfWorkTransaction => EnableRetryOnFailure;
}
