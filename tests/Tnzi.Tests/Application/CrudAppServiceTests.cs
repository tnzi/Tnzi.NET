using System.Linq.Expressions;
using Tnzi.Application;
using Tnzi.Domain.Entities;
using Tnzi.Domain.Repositories;
using Tnzi.Mapping;

namespace Tnzi.Tests.Application;

/// <summary>
/// <see cref="CrudAppService{TEntity,TKey,TDto,TCreateDto,TUpdateDto}"/> 的行为测试：
/// 新建/取详情/更新/删除/批删的 happy path + 404（不存在）+ 范围外不可见。
/// 用手写 <see cref="IObjectMapper"/> 假件 + Moq 仓储，无 UoW 管理器（直接执行）。
/// </summary>
public class CrudAppServiceTests
{
    #region Test doubles

    public class TestEntity : EntityBase<Guid>
    {
        public string Name { get; set; } = string.Empty;
        public int Tenant { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public decimal Salary { get; set; }
        public TestOwner? Owner { get; set; }
    }

    public class TestOwner
    {
        public string Secret { get; set; } = string.Empty;
    }

    public class TestDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CreateTestDto { public string Name { get; set; } = string.Empty; }

    public class UpdateTestDto { public string Name { get; set; } = string.Empty; }

    private sealed class TestMapper : IObjectMapper
    {
        public TTarget Map<TTarget>(object source)
        {
            if (typeof(TTarget) == typeof(TestDto) && source is TestEntity e)
                return (TTarget)(object)new TestDto { Id = e.Id, Name = e.Name };
            if (typeof(TTarget) == typeof(TestEntity) && source is CreateTestDto c)
                return (TTarget)(object)new TestEntity { Name = c.Name };
            throw new NotSupportedException($"No mapping from {source.GetType().Name} to {typeof(TTarget).Name}");
        }

        public TTarget Map<TSource, TTarget>(TSource source, TTarget destination)
        {
            if (source is UpdateTestDto u && destination is TestEntity e)
                e.Name = u.Name;
            return destination;
        }

        public List<TTarget> MapToList<TTarget>(IEnumerable<object>? source)
        {
            var list = new List<TTarget>();
            if (source == null) return list;
            foreach (var item in source)
                list.Add(Map<TTarget>(item));
            return list;
        }
    }

    private sealed class TestCrudService(IServiceProvider serviceProvider, IRepository<TestEntity, Guid> repository)
        : CrudAppService<TestEntity, Guid, TestDto, CreateTestDto, UpdateTestDto>(serviceProvider, repository)
    {
        public Expression<Func<TestEntity, bool>>? Scope { get; set; }

        protected override Task<Expression<Func<TestEntity, bool>>?> ApplyScopeAsync(CancellationToken cancellationToken)
            => Task.FromResult(Scope);
    }

    private static (TestCrudService service, Mock<IRepository<TestEntity, Guid>> repo) CreateService()
    {
        var repo = new Mock<IRepository<TestEntity, Guid>>();
        var services = new ServiceCollection();
        services.AddSingleton<IObjectMapper>(new TestMapper());
        var provider = services.BuildServiceProvider();
        return (new TestCrudService(provider, repo.Object), repo);
    }

    #endregion

    [Fact]
    public async Task CreateAsync_ShouldMapInsertAndReturnDto()
    {
        var (service, repo) = CreateService();
        TestEntity? inserted = null;
        repo.Setup(r => r.InsertAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()))
            .Callback<TestEntity, CancellationToken>((e, _) => { e.Id = Guid.NewGuid(); inserted = e; })
            .Returns(Task.CompletedTask);

        var result = await service.CreateAsync(new CreateTestDto { Name = "Alice" });

        Assert.True(result.Succeeded);
        Assert.Equal("Alice", result.Data!.Name);
        Assert.NotNull(inserted);
        repo.Verify(r => r.InsertAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_WhenFound_ShouldReturnDto()
    {
        var (service, repo) = CreateService();
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestEntity { Id = id, Name = "Bob" });

        var result = await service.GetByIdAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal("Bob", result.Data!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_WhenMissing_ShouldReturn404()
    {
        var (service, repo) = CreateService();
        repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestEntity?)null);

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task GetByIdAsync_WhenOutOfScope_ShouldReturn404()
    {
        var (service, repo) = CreateService();
        service.Scope = e => e.Tenant == 1;
        var id = Guid.NewGuid();
        repo.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestEntity { Id = id, Name = "Other", Tenant = 2 });

        var result = await service.GetByIdAsync(id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_ShouldApplyUpdateDtoAndPersist()
    {
        var (service, repo) = CreateService();
        var id = Guid.NewGuid();
        var entity = new TestEntity { Id = id, Name = "old" };
        repo.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        repo.Setup(r => r.UpdateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.UpdateAsync(id, new UpdateTestDto { Name = "new" });

        Assert.True(result.Succeeded);
        Assert.Equal("new", result.Data!.Name);
        Assert.Equal("new", entity.Name);
        repo.Verify(r => r.UpdateAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenMissing_ShouldReturn404()
    {
        var (service, repo) = CreateService();
        repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((TestEntity?)null);

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateTestDto { Name = "x" });

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task DeleteAsync_WhenFound_ShouldDelete()
    {
        var (service, repo) = CreateService();
        var id = Guid.NewGuid();
        var entity = new TestEntity { Id = id };
        repo.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        repo.Setup(r => r.DeleteAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await service.DeleteAsync(id);

        Assert.True(result.Succeeded);
        repo.Verify(r => r.DeleteAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WhenMissing_ShouldReturn404()
    {
        var (service, repo) = CreateService();
        repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((TestEntity?)null);

        var result = await service.DeleteAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task BatchDeleteAsync_ShouldDeleteInScopeAndSkipOthers()
    {
        var (service, repo) = CreateService();
        service.Scope = e => e.Tenant == 1;
        var inScope = new TestEntity { Id = Guid.NewGuid(), Tenant = 1 };
        var outScope = new TestEntity { Id = Guid.NewGuid(), Tenant = 2 };
        repo.Setup(r => r.GetListAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([inScope, outScope]);
        List<TestEntity>? deleted = null;
        repo.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<TestEntity>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<TestEntity>, CancellationToken>((e, _) => deleted = e.ToList())
            .Returns(Task.CompletedTask);

        var result = await service.BatchDeleteAsync([inScope.Id, outScope.Id]);

        Assert.True(result.Succeeded);
        Assert.NotNull(deleted);
        Assert.Single(deleted!);
        Assert.Equal(inScope.Id, deleted![0].Id);
    }

    [Fact]
    public async Task BatchDeleteAsync_WhenEmpty_ShouldSucceedWithoutRepositoryCall()
    {
        var (service, repo) = CreateService();

        var result = await service.BatchDeleteAsync([]);

        Assert.True(result.Succeeded);
        repo.Verify(r => r.GetListAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_ShouldMapPageToDtoList()
    {
        var (service, repo) = CreateService();
        var items = new List<TestEntity>
        {
            new() { Id = Guid.NewGuid(), Name = "a" },
            new() { Id = Guid.NewGuid(), Name = "b" },
        };
        IPagedList<TestEntity> page = new PagedList<TestEntity>(items, 1, 10, 2);
        repo.Setup(r => r.GetPagedListAsync(It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var result = await service.QueryAsync(new PagedQuery());

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal(["a", "b"], result.Data!.Items.Select(d => d.Name));
    }

    [Fact]
    public async Task QueryAsync_WithScope_ShouldUsePredicateOverload()
    {
        var (service, repo) = CreateService();
        service.Scope = e => e.Tenant == 1;
        IPagedList<TestEntity> page = new PagedList<TestEntity>([], 1, 10, 0);
        repo.Setup(r => r.GetPagedListAsync(
                It.IsAny<Expression<Func<TestEntity, bool>>>(), It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var result = await service.QueryAsync(new PagedQuery());

        Assert.True(result.Succeeded);
        repo.Verify(r => r.GetPagedListAsync(
            It.IsAny<Expression<Func<TestEntity, bool>>>(), It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetPagedListAsync(It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_FilterOnNavigationProperty_Returns400()
    {
        // 请求来源的 Filter 不得走到实体的导航属性上：持 .view 者否则可按 Owner.Secret 逐字符二分
        var (service, repo) = CreateService();
        var query = new PagedQuery { Filter = FilterGroup.And().WhereStartsWith("Owner.Secret", "a") };

        var result = await service.QueryAsync(query);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("Owner.Secret", result.Message);
        Assert.DoesNotContain(nameof(TestEntity), result.Message);
        repo.Verify(r => r.GetPagedListAsync(It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.GetPagedListAsync(
            It.IsAny<Expression<Func<TestEntity, bool>>>(), It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_FilterOnRootScalarTheDtoNeverProjects_Returns400()
    {
        // 口令哈希 / 薪资是根实体上的标量列而 DTO 从不投影它们：默认策略必须按 DTO 的形状收窄，
        // 否则 Filter.Rules[0].Field=PasswordHash&Operator=StartsWith 仍能对隐藏列逐字符二分
        var (service, repo) = CreateService();

        foreach (var hidden in new[] { "PasswordHash", "salary", "Tenant" })
        {
            var result = await service.QueryAsync(new PagedQuery { Filter = FilterGroup.And().WhereGreaterThan(hidden, 1) });

            Assert.Equal(400, result.Code);
            Assert.Contains(hidden, result.Message);
            Assert.DoesNotContain(nameof(TestEntity), result.Message);
        }

        repo.Verify(r => r.GetPagedListAsync(It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_OrderByRootScalarTheDtoNeverProjects_Returns400()
    {
        // 排序是同一个预言机的弱形态：按 Salary 排序就把薪资的相对次序交了出去
        var (service, repo) = CreateService();

        var result = await service.QueryAsync(new PagedQuery { OrderBy = "Name ASC, Salary DESC" });

        Assert.Equal(400, result.Code);
        Assert.Contains("Salary", result.Message);
        Assert.DoesNotContain("Name", result.Message);
        Assert.DoesNotContain(nameof(TestEntity), result.Message);
        repo.Verify(r => r.GetPagedListAsync(It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_OrderByProjectedField_StillApplied()
    {
        var (service, repo) = CreateService();
        IPagedList<TestEntity> page = new PagedList<TestEntity>([], 1, 10, 0);
        repo.Setup(r => r.GetPagedListAsync(It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var result = await service.QueryAsync(new PagedQuery { OrderBy = "name desc, Id" });

        Assert.True(result.Succeeded);
        repo.Verify(r => r.GetPagedListAsync(It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryAsync_FilterOnUnknownField_Returns400WithoutTypeName()
    {
        var (service, _) = CreateService();
        var query = new PagedQuery { Filter = FilterGroup.And().WhereIsNotNull("NoSuchColumn") };

        var result = await service.QueryAsync(query);

        Assert.Equal(400, result.Code);
        Assert.DoesNotContain(nameof(TestEntity), result.Message);
    }

    [Fact]
    public async Task QueryAsync_FilterOnRootScalar_StillApplied()
    {
        var (service, repo) = CreateService();
        IPagedList<TestEntity> page = new PagedList<TestEntity>([], 1, 10, 0);
        repo.Setup(r => r.GetPagedListAsync(It.IsAny<PagedQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
        var query = new PagedQuery { Filter = FilterGroup.And().WhereEqual("name", "a") };

        var result = await service.QueryAsync(query);

        Assert.True(result.Succeeded);
        repo.Verify(r => r.GetPagedListAsync(
            It.Is<PagedQuery>(q => ReferenceEquals(q, query) && q.Filter!.HasFilters), It.IsAny<CancellationToken>()), Times.Once);
    }
}
