namespace Tnzi.Authorization.DataAuth.Tests.Extensions;

/// <summary>
/// <c>WithDataAuthAsync</c> 对「没有用户」的处置。
/// </summary>
/// <remarks>
/// 「有用户但没有规则」= 不限制，是本模块记录在案的设计；「没有用户」不是同一件事：
/// 一个名字里带 DataAuth 的方法在它无法判定的场景里放行，就是一条安静的 fail-open ——
/// 调用它的匿名端点会拿到全表，而日志、返回值、界面全都正常。
/// </remarks>
public class RepositoryExtensionsTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IRepository<EntityInfo, Guid>> _repository = new();
    private readonly Mock<IDataAuthService> _dataAuth = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private readonly List<EntityInfo> _rows =
    [
        new() { Id = Guid.NewGuid(), Name = "a", TypeName = "A" },
        new() { Id = Guid.NewGuid(), Name = "b", TypeName = "B" },
        new() { Id = Guid.NewGuid(), Name = "c", TypeName = "C" },
    ];

    public RepositoryExtensionsTests()
    {
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(() => _rows.AsQueryable());
    }

    private void ActAsAnonymous() => _currentUser.Setup(u => u.Id).Returns((Guid?)null);

    private void ActAsUser() => _currentUser.Setup(u => u.Id).Returns(UserId);

    /// <summary>★现形用例：此前匿名调用方拿到的是整张表。</summary>
    [Fact]
    public async Task Anonymous_caller_sees_no_rows_by_default()
    {
        ActAsAnonymous();

        var query = await _repository.Object.WithDataAuthAsync(_dataAuth.Object, _currentUser.Object);

        query.ToList().ShouldBeEmpty();
        // 没有用户就没有角色可查 —— 服务根本不该被问到。
        _dataAuth.Verify(s => s.GetDataFilterAsync<EntityInfo>(It.IsAny<Guid>(), It.IsAny<DataAuthOperation>()), Times.Never);
    }

    [Fact]
    public async Task Anonymous_caller_may_be_let_through_only_by_an_explicit_choice_at_the_call_site()
    {
        // 「公开行 + 登录后按规则过滤」这类混合端点是合法场景；它必须在调用点写明自己要放行，
        // 而不是由这个方法替它决定。
        ActAsAnonymous();

        var query = await _repository.Object.WithDataAuthAsync(
            _dataAuth.Object, _currentUser.Object, DataAuthOperation.Query, AnonymousDataAccess.Unrestricted);

        query.Count().ShouldBe(3);
        _dataAuth.Verify(s => s.GetDataFilterAsync<EntityInfo>(It.IsAny<Guid>(), It.IsAny<DataAuthOperation>()), Times.Never);
    }

    [Fact]
    public async Task An_empty_guid_counts_as_no_user()
    {
        // Guid.Empty 是「有值但不是任何人」：按用户查角色会得到空集，与匿名同判。
        _currentUser.Setup(u => u.Id).Returns(Guid.Empty);

        var query = await _repository.Object.WithDataAuthAsync(_dataAuth.Object, _currentUser.Object);

        query.ToList().ShouldBeEmpty();
    }

    [Fact]
    public async Task Authenticated_caller_gets_the_service_filter_applied()
    {
        ActAsUser();
        _dataAuth
            .Setup(s => s.GetDataFilterAsync<EntityInfo>(UserId, DataAuthOperation.Query))
            .ReturnsAsync(e => e.Name == "a");

        var query = await _repository.Object.WithDataAuthAsync(_dataAuth.Object, _currentUser.Object);

        query.Select(e => e.Name).ToList().ShouldBe(new[] { "a" });
    }

    [Fact]
    public async Task Authenticated_caller_without_rules_sees_everything()
    {
        // 「有用户但没有规则」= 不限制：这条是刻意保留的设计，不能被匿名收口连带改掉。
        ActAsUser();
        _dataAuth
            .Setup(s => s.GetDataFilterAsync<EntityInfo>(UserId, DataAuthOperation.Query))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<EntityInfo, bool>>?)null);

        var query = await _repository.Object.WithDataAuthAsync(_dataAuth.Object, _currentUser.Object);

        query.Count().ShouldBe(3);
    }
}
