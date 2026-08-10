using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tnzi.Data;
using Tnzi.EFCore;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 只为让 <c>EFCoreModule</c> 走完整注册路径而存在的 DbContext。
/// </summary>
/// <remarks>
/// <para>
/// <c>EFCoreModule</c> 在 <c>AutoDiscoverDbContexts</c>（默认开）下要求配置里给出一个可解析的
/// <c>DbContextType</c>，否则抛「all DbContext configurations are invalid」并整个模块出局。
/// 而 <b>Repository 的注册发生在 <c>AddTnziDbContext</c> 内部</b>，模块出局或走手动模式跳过注册，
/// <c>IRepository&lt;,&gt;</c> 就不会进服务图 —— 依赖审计随之对所有消费仓储的模块变成假阴性。
/// 给一个真实类型是让审计面完整的最小代价。
/// </para>
/// <para>
/// <b>不会触发实体自动发现。</b>实体扫描发生在 <c>OnApplicationInitializationAsync</c>
/// （<c>IEntityManager.Initialize</c>）与首次构建模型时，架构门禁只跑到
/// <c>Post/ConfigureServicesAsync</c> 为止，两者都不会执行。这一点很重要：本项目引用了
/// 全生态程序集，真去建模型会撞上 Chat / Notification 双 <c>Message</c> 实体这类同名冲突。
/// </para>
/// </remarks>
public class ArchitectureTestDbContext : TnziDbContext<ArchitectureTestDbContext>
{
    /// <inheritdoc />
    public ArchitectureTestDbContext(
        DbContextOptions<ArchitectureTestDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options, currentUser, currentTenant, dataFilterManager, timeProvider, multiTenancyOptions)
    {
    }
}
