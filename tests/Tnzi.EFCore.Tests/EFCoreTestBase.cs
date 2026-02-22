
namespace Tnzi.EFCore.Tests;

/// <summary>
/// 测试基类,提供通用的测试基础设施
/// </summary>
public abstract class EFCoreTestBase : IDisposable
{
    protected readonly ServiceProvider ServiceProvider;
    protected readonly TestDbContext DbContext;
    protected readonly ICurrentUser CurrentUser;
    protected readonly ICurrentTenant CurrentTenant;

    protected EFCoreTestBase()
    {
        var services = new ServiceCollection();

        // 注册 Mock 服务
        var currentUser = new MockCurrentUser();
        var currentTenant = new MockCurrentTenant();

        services.AddSingleton<ICurrentUser>(currentUser);
        services.AddSingleton<ICurrentTenant>(currentTenant);

        // 注册 DbContext (使用 SQLite 数据库)
        // 每个测试使用唯一的数据库文件,确保测试隔离
        var dbName = $"test_{Guid.NewGuid():N}.db";
        services.AddDbContext<TestDbContext>((sp, options) =>
        {
            options.UseSqlite($"Data Source={dbName}");
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        ServiceProvider = services.BuildServiceProvider();

        // 获取服务
        DbContext = ServiceProvider.GetRequiredService<TestDbContext>();
        CurrentUser = ServiceProvider.GetRequiredService<ICurrentUser>();
        CurrentTenant = ServiceProvider.GetRequiredService<ICurrentTenant>();

        // 确保数据库已创建并应用迁移
        DbContext.Database.EnsureDeleted(); // 清理旧数据
        DbContext.Database.EnsureCreated(); // 创建新数据库
    }

    public virtual void Dispose()
    {
        // 清理数据库
        try
        {
            DbContext.Database.EnsureDeleted();
        }
        catch
        {
            // 忽略清理错误
        }

        DbContext?.Dispose();
        ServiceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Mock CurrentUser 实现
/// </summary>
public class MockCurrentUser : ICurrentUser
{
    private Guid? _id;
    private string? _userName;
    private Guid? _tenantId;
    private string[] _roles = Array.Empty<string>();

    public bool IsAuthenticated => _id.HasValue;

    public Guid? Id
    {
        get => _id;
        set => _id = value;
    }

    public string? UserName
    {
        get => _userName;
        set => _userName = value;
    }

    public Guid? TenantId
    {
        get => _tenantId;
        set => _tenantId = value;
    }

    public string[] Roles
    {
        get => _roles;
        set => _roles = value ?? Array.Empty<string>();
    }

    public bool IsInRole(string roleName)
    {
        return _roles.Contains(roleName);
    }

    public void SetUser(Guid userId, string userName, Guid? tenantId = null, string[]? roles = null)
    {
        _id = userId;
        _userName = userName;
        _tenantId = tenantId;
        _roles = roles ?? Array.Empty<string>();
    }

    public void Clear()
    {
        _id = null;
        _userName = null;
        _tenantId = null;
        _roles = Array.Empty<string>();
    }
}

/// <summary>
/// Mock CurrentTenant 实现
/// </summary>
public class MockCurrentTenant : ICurrentTenant
{
    private Guid? _id;
    private string? _name;

    public Guid? Id
    {
        get => _id;
        set => _id = value;
    }

    public string? Name
    {
        get => _name;
        set => _name = value;
    }

    public bool IsAvailable => _id.HasValue;

    public IDisposable Change(Guid? tenantId, string? tenantName = null)
    {
        var oldId = _id;
        var oldName = _name;

        _id = tenantId;
        _name = tenantName;

        return new DisposableAction(() =>
        {
            _id = oldId;
            _name = oldName;
        });
    }

    public void SetTenant(Guid tenantId, string tenantName)
    {
        _id = tenantId;
        _name = tenantName;
    }

    public void Clear()
    {
        _id = null;
        _name = null;
    }
}

/// <summary>
/// 可释放的动作包装器
/// </summary>
internal class DisposableAction : IDisposable
{
    private readonly Action _action;

    public DisposableAction(Action action)
    {
        _action = action;
    }

    public void Dispose()
    {
        _action?.Invoke();
    }
}