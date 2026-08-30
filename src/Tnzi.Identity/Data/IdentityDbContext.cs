
namespace Tnzi.Identity.Data;

/// <summary>
/// Tnzi Identity 数据库上下文基类
/// 继承自 ASP.NET Core Identity 的 IdentityDbContext,并集成 Tnzi.NET 框架的自动配置功能
/// </summary>
/// <typeparam name="TDbContext">派生的 DbContext 类型</typeparam>
public abstract class IdentityDbContext<TDbContext> : IdentityDbContext<User, Role, Guid, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>
    , IMultiTenancySwitchProvider
    where TDbContext : DbContext
{
    protected ICurrentUser CurrentUser { get; }
    protected ICurrentTenant? CurrentTenant { get; }
    protected IDataFilterManager? DataFilterManager { get; }
    protected TimeProvider? TimeProvider { get; }
    private readonly bool _multiTenancyEnabled;

    /// <summary>
    /// 初始化 IdentityDbContext 实例
    /// </summary>
    /// <param name="options">DbContext 选项</param>
    /// <param name="currentUser">当前用户服务</param>
    /// <param name="currentTenant">当前租户服务（可选）</param>
    /// <param name="dataFilterManager">数据过滤器管理器（可选）</param>
    /// <param name="timeProvider">时间提供者（可选）</param>
    /// <param name="multiTenancyOptions">多租户配置（可选）</param>
    public IdentityDbContext(
        DbContextOptions<TDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant? currentTenant = null,
        IDataFilterManager? dataFilterManager = null,
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(options)
    {
        CurrentUser = Check.NotNull(currentUser);
        CurrentTenant = currentTenant;
        DataFilterManager = dataFilterManager;
        TimeProvider = timeProvider;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    public bool IsMultiTenancyEnabled => _multiTenancyEnabled;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // 使用辅助类统一处理模型初始化
        // 传入额外的 Identity 表名配置 Action，确保 Identity 实体被正确配置
        TnziDbContextHelper.OnModelCreating(this, builder, (b) =>
        {
            // 1. 移除 Identity 默认的 AspNet 前缀
            ConfigureIdentityTableNames(b);
        });

        // 2. 配置查询过滤器
        ConfigureQueryFilters(builder);

        // 3. 单租户模式下移除多租户专属身份模型
        if (!_multiTenancyEnabled)
        {
            ConfigureSingleTenantIdentityModel(builder);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // 全局模型约定（含未显式配置精度的 decimal 列的默认精度）
        // 与 TnziDbContext 共用同一实现——两个基类平行（本类继承 ASP.NET Core IdentityDbContext），
        // 应用侧的主 DbContext 多继承本类，故此处同样需要落约定。
        TnziDbContextHelper.ConfigureConventions(configurationBuilder);
    }

    /// <summary>
    /// 配置 Identity 相关实体的表名，移除默认的 "AspNet" 前缀
    /// 表名会在后续的 TableNamePrefixConfiguration 中自动添加 "Identity_" 前缀
    /// </summary>
    protected virtual void ConfigureIdentityTableNames(ModelBuilder builder)
    {
        builder.Entity<User>().ToTable("User");
        builder.Entity<Role>().ToTable("Role");
        builder.Entity<UserRole>().ToTable("UserRole");
        builder.Entity<UserClaim>().ToTable("UserClaim");
        builder.Entity<UserLogin>().ToTable("UserLogin");
        builder.Entity<RoleClaim>().ToTable("RoleClaim");
        builder.Entity<UserToken>().ToTable("UserToken");

        // Passkey 凭据表。★ 实体是 IdentityUserPasskey<Guid>（主键 CredentialId），
        // 不是 IdentityPasskeyData —— 后者是它的 Data 属性，一个复杂类型，
        // `builder.Entity<IdentityPasskeyData>()` 作用在它身上没有意义。
        // DbSet 由基类 IdentityUserContext 提供，EF 的 UserStore 已实现全部 IUserPasskeyStore 方法，
        // 所以这里只需要落表名，不需要任何额外映射。
        ConfigurePasskeyModel(builder);
    }

    /// <summary>
    /// Passkey 凭据表的完整映射。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <strong>这不是"改个表名"，是补上一个从来没有过的映射。</strong>
    /// <c>IdentityUserContext</c> 提供了 <c>UserPasskeys</c> 这个 <c>DbSet</c>，
    /// 但本类继承的 8 参数 <c>IdentityDbContext&lt;TUser,TRole,TKey,…&gt;</c> 基类
    /// <strong>不配置 passkey 实体</strong>（既没有主键，也没有说 <c>Data</c> 是复杂类型），
    /// 于是模型校验直接失败。
    /// </para>
    /// <para>
    /// 此前这里是一段 <c>builder.Entity&lt;IdentityPasskeyData&gt;().ToTable(...).HasNoKey()</c>
    /// 包在 <c>try/catch</c> 里的代码。它<strong>作用在错误的类型上</strong>
    /// （<c>IdentityPasskeyData</c> 是 <c>IdentityUserPasskey.Data</c> 的类型，不是实体），
    /// 效果只是"让模型能通过校验"，代价是 passkey 根本存不进去 ——
    /// 无主键实体在 EF 里是只读查询类型，<c>AddOrUpdatePasskeyAsync</c> 的写入路径走不通。
    /// 换句话说：<strong>那不是一个待完善的占位，是一个让启动不报错的止血带。</strong>
    /// </para>
    /// </remarks>
    protected virtual void ConfigurePasskeyModel(ModelBuilder builder)
    {
        // Data 是随凭据内联存储的一组字段，不是独立的表。
        // 不 Ignore 的话 EF 会按约定把它当成一个缺主键的实体类型。
        builder.Ignore<IdentityPasskeyData>();

        builder.Entity<IdentityUserPasskey<Guid>>(b =>
        {
            // WebAuthn 的凭据标识天然唯一，直接做主键。
            b.HasKey(p => p.CredentialId);

            // ★★ 前缀必须写全，这里是本文件唯一这么做的地方。框架的 TableNamePrefixConfiguration
            // 按**实体所在程序集**反查模块拿前缀，而这个实体属于
            // Microsoft.Extensions.Identity.Stores 而不是 Tnzi.Identity —— 自动前缀对它不生效。
            // 上面那些 ToTable("User") 写裸名是对的（它们住在本程序集里），照抄到这一行
            // 就会得到一张没有模块前缀的 UserPasskey 表，混在消费应用自己的表中间。
            // 有 IdentityConstants.TablePrefix 兜着，改前缀时这里会跟着变。
            b.ToTable($"{IdentityConstants.TablePrefix}_UserPasskey");
            b.ComplexProperty(p => p.Data);

            // 用户行被物理删除时凭据跟着走：留下一批指向不存在用户的凭据没有任何意义。
            // ★ 注意 User 是软删（ISoftDelete），框架的「停用/注销账户」都不会触发这条级联，
            // 凭据会继续留在库里 —— 那不是漏洞而是刻意的：账号能不能登录由
            // IAuthService.IssueTokenAsync 的账号状态检查判定，不靠删凭据来实现。
            b.HasOne<User>()
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // 缓存泛型方法实例，避免每次调用 MakeGenericMethod
    private static readonly ConcurrentDictionary<Type, MethodInfo> SoftDeleteFilterMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> MultiTenantFilterMethodCache = new();

    // 基础方法缓存（非泛型）
    private static readonly MethodInfo? BaseSoftDeleteFilterMethod = typeof(IdentityDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo? BaseMultiTenantFilterMethod = typeof(IdentityDbContext<TDbContext>)
        .GetMethod(nameof(ConfigureMultiTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// 配置查询过滤器 (软删除和多租户)
    /// </summary>
    protected virtual void ConfigureQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(ISoftDelete).IsAssignableFrom(clrType))
            {
                var method = GetOrCreateSoftDeleteFilterMethod(clrType);
                method?.Invoke(this, new object[] { builder });
            }

            if (typeof(IMultiTenant).IsAssignableFrom(clrType))
            {
                if (_multiTenancyEnabled)
                {
                    var method = GetOrCreateMultiTenantFilterMethod(clrType);
                    method?.Invoke(this, new object[] { builder });
                }
                else
                {
                    builder.Entity(clrType).Ignore(nameof(IMultiTenant.TenantId));
                }
            }
        }
    }

    /// <summary>
    /// 单租户模式下移除仅多租户需要的身份模型映射
    /// </summary>
    protected virtual void ConfigureSingleTenantIdentityModel(ModelBuilder builder)
    {
        // User.TenantId 作为身份租户列，在单租户模式下不生成数据库列
        builder.Entity<User>().Ignore(u => u.TenantId);

        // Tenant 主数据在单租户模式下不生成表
        builder.Ignore<Tenant>();
    }

    /// <summary>
    /// 获取或创建软删除过滤器的泛型方法实例
    /// </summary>
    private static MethodInfo? GetOrCreateSoftDeleteFilterMethod(Type entityType)
    {
        if (BaseSoftDeleteFilterMethod == null)
            return null;

        return SoftDeleteFilterMethodCache.GetOrAdd(entityType,
            type => BaseSoftDeleteFilterMethod.MakeGenericMethod(type));
    }

    /// <summary>
    /// 获取或创建多租户过滤器的泛型方法实例
    /// </summary>
    private static MethodInfo? GetOrCreateMultiTenantFilterMethod(Type entityType)
    {
        if (BaseMultiTenantFilterMethod == null)
            return null;

        return MultiTenantFilterMethodCache.GetOrAdd(entityType,
            type => BaseMultiTenantFilterMethod.MakeGenericMethod(type));
    }

    protected virtual bool IsSoftDeleteFilterEnabled => DataFilterManager?.IsEnabled<ISoftDeleteFilter>() ?? true;
    protected virtual bool IsMultiTenantFilterEnabled => _multiTenancyEnabled && (DataFilterManager?.IsEnabled<IMultiTenantFilter>() ?? true);

    /// <summary>
    /// 获取当前租户ID（在查询时调用，表达式树延迟求值）
    /// </summary>
    protected virtual Guid? GetCurrentTenantId() => CurrentTenant?.Id ?? CurrentUser?.TenantId;

    protected void ConfigureSoftDeleteFilter<T>(ModelBuilder modelBuilder) where T : class, ISoftDelete
        => modelBuilder.Entity<T>().HasQueryFilter(e => !IsSoftDeleteFilterEnabled || !e.IsDeleted);

    /// <summary>
    /// 配置多租户查询过滤器
    /// </summary>
    protected void ConfigureMultiTenantFilter<T>(ModelBuilder modelBuilder) where T : class, IMultiTenant
    {
        modelBuilder.Entity<T>().HasQueryFilter(e =>
            !IsMultiTenantFilterEnabled || e.TenantId == GetCurrentTenantId());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 使用辅助类处理通用的保存逻辑（审计、文件追踪、领域事件、事务等）
        return await TnziDbContextHelper.SaveChangesAsync(
            this,
            base.SaveChangesAsync,
            CurrentUser,
            CurrentTenant,
            cancellationToken,
            TimeProvider,
            _multiTenancyEnabled);
    }
}
