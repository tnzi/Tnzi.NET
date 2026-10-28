using System.Linq.Expressions;
using Tnzi.Application;
using Tnzi.Mapping;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// <see cref="CrudAppService{TEntity,TKey,TDto,TCreateDto,TUpdateDto}"/> 的单行访问（取详情 / 更新 / 删除）与批删
/// 由数据库判定范围谓词，和列表查询同一套 SQL 语义。此前单行校验把谓词编译后在内存里对 <c>FindAsync</c> 加载的实体求值，
/// 而那个实体的导航没有加载：文档示范的「按指派关联可见」谓词在列表里看得见这一行，取详情却 404、批删静默跳过。
/// 真实 SQLite。
/// </summary>
public class CrudAppServiceNavigationScopeTests : IDisposable
{
    public class ScopedProject : EntityBase<Guid>
    {
        public string Name { get; set; } = string.Empty;
        public List<ScopedAssignment> Assignments { get; set; } = [];
    }

    public class ScopedAssignment : EntityBase<Guid>
    {
        public Guid ProjectId { get; set; }
        public Guid UserId { get; set; }
    }

    public class ProjectDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ScopeTestDbContext(DbContextOptions<ScopeTestDbContext> options, ICurrentUser currentUser)
        : TnziDbContext<ScopeTestDbContext>(options, currentUser)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ScopedProject>(entity =>
            {
                entity.ToTable("ScopedProjects");
                entity.HasKey(e => e.Id);
                entity.HasMany(e => e.Assignments).WithOne().HasForeignKey(a => a.ProjectId);
            });
            modelBuilder.Entity<ScopedAssignment>(entity =>
            {
                entity.ToTable("ScopedAssignments");
                entity.HasKey(e => e.Id);
            });
            ConfigureQueryFilters(modelBuilder);
        }
    }

    private sealed class ProjectMapper : IObjectMapper
    {
        public TTarget Map<TTarget>(object source)
            => source is ScopedProject p && typeof(TTarget) == typeof(ProjectDto)
                ? (TTarget)(object)new ProjectDto { Id = p.Id, Name = p.Name }
                : throw new NotSupportedException();

        public TTarget Map<TSource, TTarget>(TSource source, TTarget destination) => destination;

        public List<TTarget> MapToList<TTarget>(IEnumerable<object>? source)
            => source?.Select(Map<TTarget>).ToList() ?? [];
    }

    /// <summary>文档示范的形状：因为当前用户被指派到这个项目，所以这一行可见。</summary>
    private sealed class AssignedProjectService(IServiceProvider serviceProvider, IRepository<ScopedProject, Guid> repository, Guid userId)
        : CrudAppService<ScopedProject, Guid, ProjectDto, ProjectDto, ProjectDto>(serviceProvider, repository)
    {
        protected override Task<Expression<Func<ScopedProject, bool>>?> ApplyScopeAsync(CancellationToken cancellationToken)
            => Task.FromResult<Expression<Func<ScopedProject, bool>>?>(p => p.Assignments.Any(a => a.UserId == userId));
    }

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly ScopeTestDbContext _dbContext;
    private readonly Guid _me = Guid.NewGuid();
    private readonly ScopedProject _assignedToMe;
    private readonly ScopedProject _assignedToSomeoneElse;

    public CrudAppServiceNavigationScopeTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());
        services.AddSingleton<IObjectMapper>(new ProjectMapper());
        services.AddDbContext<ScopeTestDbContext>(options => options.UseSqlite(_connection));
        _serviceProvider = services.BuildServiceProvider();

        _dbContext = _serviceProvider.GetRequiredService<ScopeTestDbContext>();
        _dbContext.Database.EnsureCreated();

        _assignedToMe = new ScopedProject { Id = Guid.NewGuid(), Name = "mine", Assignments = [new ScopedAssignment { Id = Guid.NewGuid(), UserId = _me }] };
        _assignedToSomeoneElse = new ScopedProject { Id = Guid.NewGuid(), Name = "theirs", Assignments = [new ScopedAssignment { Id = Guid.NewGuid(), UserId = Guid.NewGuid() }] };
        _dbContext.AddRange(_assignedToMe, _assignedToSomeoneElse);
        _dbContext.SaveChangesAsync().GetAwaiter().GetResult();
        _dbContext.ChangeTracker.Clear();
    }

    private AssignedProjectService CreateService()
        => new(_serviceProvider, new EFCoreRepository<ScopeTestDbContext, ScopedProject, Guid>(_dbContext, options: null, serviceProvider: _serviceProvider, logger: null), _me);

    [Fact]
    public async Task ListAndDetail_AgreeOnANavigationScope()
    {
        var service = CreateService();

        var list = await service.QueryAsync(new PagedQuery());
        var visible = await service.GetByIdAsync(_assignedToMe.Id);
        var invisible = await service.GetByIdAsync(_assignedToSomeoneElse.Id);

        Assert.Equal([_assignedToMe.Id], list.Data!.Items.Select(p => p.Id));
        Assert.True(visible.Succeeded);
        Assert.Equal("mine", visible.Data!.Name);
        Assert.Equal(404, invisible.Code);
    }

    [Fact]
    public async Task Delete_UsesTheSameScopeAsTheList()
    {
        var service = CreateService();

        var mine = await service.DeleteAsync(_assignedToMe.Id);
        var theirs = await service.DeleteAsync(_assignedToSomeoneElse.Id);

        Assert.True(mine.Succeeded);
        Assert.Equal(404, theirs.Code);
        _dbContext.ChangeTracker.Clear();
        Assert.Equal(["theirs"], await _dbContext.Set<ScopedProject>().Select(p => p.Name).ToListAsync());
    }

    [Fact]
    public async Task BatchDelete_DeletesTheRowsInScope_AndSkipsTheOthers()
    {
        var service = CreateService();

        var result = await service.BatchDeleteAsync([_assignedToMe.Id, _assignedToSomeoneElse.Id]);

        Assert.True(result.Succeeded);
        _dbContext.ChangeTracker.Clear();
        Assert.Equal(["theirs"], await _dbContext.Set<ScopedProject>().Select(p => p.Name).ToListAsync());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }
}
