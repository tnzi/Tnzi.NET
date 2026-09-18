
namespace Tnzi.Identity.Data;

/// <summary>
/// Tnzi Identity 数据库上下文基类
/// 继承自 ASP.NET Core Identity 的 IdentityDbContext,并集成 Tnzi.NET 框架的自动配置功能
/// </summary>
/// <typeparam name="TDbContext">派生的 DbContext 类型</typeparam>
public abstract class IdentityDbContext<TDbContext> : IdentityDbContext<User, Role, Guid, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>
    , IMultiTenancySwitchProvider
    , IQueryFilterContext
    , IAuditPropertyContext
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
        // 三个可选协作者与下面的开关同一个根因：消费方只声明 (options, currentUser)，这里的实参恒为 null。
        // 注入值缺席时从 options 携带的应用容器解析；手工构造（设计期）没有容器，仍是 null。
        // 与 TnziDbContext 走同一处（DbContextCollaborators），两个基类必须给出同一个答案。
        CurrentTenant = DbContextCollaborators.Resolve(currentTenant, options);
        DataFilterManager = DbContextCollaborators.Resolve(dataFilterManager, options);
        TimeProvider = DbContextCollaborators.Resolve(timeProvider, options);
        // 显式转发的选项优先；否则读 options 里的 MultiTenancyOptionsExtension（AddTnziDbContext 与
        // DesignTimeDbContextFactoryBase 共同写入的载体）。与 TnziDbContext 走同一处解析：
        // 两份各自的三元表达式漂开时不会有任何东西报错。见 MultiTenancySwitch。
        _multiTenancyEnabled = MultiTenancySwitch.Resolve(multiTenancyOptions?.Value.Enabled, options);
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

        // 4. SQLite 下让 LockoutEnd 可以在 SQL 里做大小比较
        ConfigureLockoutEndForSqlite(builder);
    }

    /// <summary>
    /// SQLite 下把 <c>User.LockoutEnd</c>（<see cref="DateTimeOffset"/>）映射成可比较的 UTC 文本。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ EF Core 的 SQLite 提供者<b>不翻译</b> <see cref="DateTimeOffset"/> 的大小比较（只翻译等值）。
    /// 框架有三处按 <c>LockoutEnd &lt;= UtcNow</c> / <c>&gt; UtcNow</c> 判「是否仍锁定」的查询
    /// （用户统计、用户列表的 <c>isLockedOut</c> 筛选、登录安全总览），在 SQLite 部署上一律 500，
    /// 而 SQLite 是框架宣称支持的提供者。
    /// </para>
    /// <para>
    /// 转换成文本后比较退化成字典序，对<b>同一偏移量</b>的 ISO 文本，字典序 = 时间序，所以写入时归一到 UTC
    /// （管理员经 JSON 传进来的 <c>LockoutEnd</c> 可以带 <c>+08:00</c>）。文本格式与 Microsoft.Data.Sqlite 自己写
    /// <see cref="DateTimeOffset"/> 时用的逐字相同（<c>yyyy-MM-dd HH:mm:ss.FFFFFFFzzz</c>，列类型仍是 TEXT），
    /// 所以既有的 SQLite 库<b>零迁移</b>、既有行照常读回；差别只在此前带非零偏移量写入的行读回来是 UTC 表示的同一瞬间。
    /// 刻意不用 <c>DateTimeOffsetToBinaryConverter</c>：它把列改成 INTEGER，对既有库是一次数据迁移。
    /// </para>
    /// <para>
    /// 只作用于 SQLite；其它提供者原生支持该类型的比较，一字不动。这是 ASP.NET Identity 自带的列，
    /// 也是框架实体里唯一的 <see cref="DateTimeOffset"/> 属性，消费方自己的 <see cref="DateTimeOffset"/> 列不在此列。
    /// </para>
    /// </remarks>
    protected virtual void ConfigureLockoutEndForSqlite(ModelBuilder builder)
    {
        if (EntityConfigurationContext.GetDatabaseProviderFromDbContext(this) != DatabaseProvider.Sqlite)
        {
            return;
        }

        builder.Entity<User>()
            .Property(u => u.LockoutEnd)
            .HasConversion(SqliteUtcDateTimeOffsetConverter.Instance);
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

    /// <summary>
    /// 配置查询过滤器（软删除和多租户）。实现与 <c>TnziDbContext</c> 共用
    /// （<see cref="TnziDbContextHelper.ConfigureQueryFilters"/>）：同时是 <c>ISoftDelete + IMultiTenant</c>
    /// 的实体得到<b>一条</b>组合过滤器。
    /// </summary>
    /// <remarks>
    /// ★ 此前本类自己维护一套并对这类实体两次裸调 <c>HasQueryFilter</c>（软删一次、租户一次）。
    /// EF Core 的无名 <c>HasQueryFilter</c> 是覆盖式的，多租户一开，租户过滤器顶掉软删过滤器，
    /// 已软删的行（含已收回的授权）对所有查询重新可见。
    /// </remarks>
    protected virtual void ConfigureQueryFilters(ModelBuilder builder)
    {
        TnziDbContextHelper.ConfigureQueryFilters(this, builder, _multiTenancyEnabled);
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

    protected virtual bool IsSoftDeleteFilterEnabled => DataFilterManager?.IsEnabled<ISoftDeleteFilter>() ?? true;
    protected virtual bool IsMultiTenantFilterEnabled => _multiTenancyEnabled && (DataFilterManager?.IsEnabled<IMultiTenantFilter>() ?? true);

    /// <summary>
    /// 获取当前租户ID（在查询时调用，表达式树延迟求值）
    /// </summary>
    protected virtual Guid? GetCurrentTenantId() => CurrentTenant?.Id ?? CurrentUser?.TenantId;

    // 过滤器表达式经这个契约访问上面三个成员；显式实现让子类对 protected virtual 的覆写照常生效。
    bool IQueryFilterContext.IsSoftDeleteFilterEnabled => IsSoftDeleteFilterEnabled;
    bool IQueryFilterContext.IsMultiTenantFilterEnabled => IsMultiTenantFilterEnabled;
    Guid? IQueryFilterContext.CurrentTenantId => GetCurrentTenantId();

    // 绕过变更跟踪器的写入路径（Dapper 批量插入 / 更新）经这个契约取到与 SaveChanges 同一组协作者。
    ICurrentUser IAuditPropertyContext.CurrentUser => CurrentUser;
    ICurrentTenant? IAuditPropertyContext.CurrentTenant => CurrentTenant;
    TimeProvider? IAuditPropertyContext.TimeProvider => TimeProvider;

    /// <summary>
    /// 单独为一个实体配置软删过滤器（<see cref="ConfigureQueryFilters"/> 已覆盖全部实体；保留给需要逐个实体覆写的子类）。
    /// </summary>
    protected void ConfigureSoftDeleteFilter<T>(ModelBuilder modelBuilder) where T : class, ISoftDelete
        => QueryFilterHelper.ApplySoftDeleteFilter(this, modelBuilder, typeof(T));

    /// <summary>
    /// 单独为一个实体配置多租户过滤器。
    /// </summary>
    protected void ConfigureMultiTenantFilter<T>(ModelBuilder modelBuilder) where T : class, IMultiTenant
        => QueryFilterHelper.ApplyMultiTenantFilter(this, modelBuilder, typeof(T));

    /// <summary>
    /// 单独为一个实体配置组合过滤器（软删 + 多租户）。
    /// </summary>
    protected void ConfigureCombinedFilter<T>(ModelBuilder modelBuilder) where T : class, ISoftDelete, IMultiTenant
        => QueryFilterHelper.ApplyCombinedFilter(this, modelBuilder, typeof(T));

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
