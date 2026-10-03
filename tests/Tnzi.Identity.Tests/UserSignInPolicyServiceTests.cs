using Tnzi.Security.Authorization;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 登录准入策略的读写半边。执行在 <see cref="IpAllowListLoginGuard"/>，这里只管「存什么、拒什么」。
/// </summary>
public class UserSignInPolicyServiceTests
{
    private readonly Mock<IRepository<UserSignInPolicy, Guid>> _repository = new();
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<IUserTenantScopeProvider> _scope = new();
    private readonly Mock<IScopedContext> _scopedContext = new();
    private readonly List<UserSignInPolicy> _rows = [];
    private readonly User _user = new() { Id = Guid.NewGuid(), UserName = "target" };
    private string[] _exemptRoles = [];
    private readonly Mock<IFunctionAuthorizationService> _functionAuthorization = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _actorId = Guid.NewGuid();

    public UserSignInPolicyServiceTests()
    {
        var store = new Mock<IUserStore<User>>();
        _userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _userManager.Setup(m => m.FindByIdAsync(_user.Id.ToString())).ReturnsAsync(_user);
        _userManager.Setup(m => m.GetRolesAsync(_user)).ReturnsAsync(["Coordinator"]);
        _scope.Setup(s => s.ContainsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _scopedContext.SetupGet(c => c.ClientIpAddress).Returns("198.51.100.7");

        _repository.Setup(r => r.InsertAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<UserSignInPolicy, CancellationToken>((p, _) => _rows.Add(p))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.DeleteAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<UserSignInPolicy, CancellationToken>((p, _) => _rows.Remove(p))
            .Returns(Task.CompletedTask);
        _repository.Setup(r => r.UpdateAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private UserSignInPolicyService CreateService()
    {
        _repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(() => _rows.BuildMock());

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        provider.Setup(p => p.GetService(typeof(IScopedContext))).Returns(_scopedContext.Object);
        provider.Setup(p => p.GetService(typeof(ICurrentUser))).Returns(_currentUser.Object);
        _currentUser.SetupGet(c => c.Id).Returns(_actorId);

        var options = Monitor(new IdentityOptions
        {
            AccountSecurity = new AccountSecurityOptions { IpAllowListExemptRoles = _exemptRoles }
        });

        return new UserSignInPolicyService(provider.Object, _repository.Object, _userManager.Object, _scope.Object, options, _functionAuthorization.Object);
    }

    /// <summary>
    /// 普通管理员给超管设一条谁也命中不了的允许列表，就把超管锁在了门外。
    /// </summary>
    [Fact]
    public async Task SetIpAllowList_OnASuperAdmin_ByANonSuperAdmin_IsForbiddenAndWritesNothing()
    {
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(_user.Id)).ReturnsAsync(true);
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(_actorId)).ReturnsAsync(false);

        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = true, AllowedIps = "192.0.2.1" });

        Assert.Equal(403, result.Code);
        Assert.Empty(_rows);
    }

    /// <summary>
    /// 两个管理员同时写同一账号的第一份策略：后到的撞上 <c>UserId</c> 唯一索引。那不是 500，
    /// 而是晚了一步 —— 读回先到的那一行、按本次请求改写（后写者生效），并把失败的实体从跟踪器里丢掉。
    /// </summary>
    [Fact]
    public async Task SetIpAllowList_WhenAConcurrentFirstWriteWins_OverwritesTheWinnersRowInsteadOfFailing()
    {
        var winner = new UserSignInPolicy { Id = Guid.NewGuid(), UserId = _user.Id, IpAllowListEnabled = true, AllowedIps = "10.0.0.0/8" };
        _repository.Setup(r => r.InsertAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()))
            .Callback(() => _rows.Add(winner))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException(
                "insert failed", new InvalidOperationException("UNIQUE constraint failed: Identity_UserSignInPolicy.UserId")));
        var service = CreateService();

        var result = await service.SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = true, AllowedIps = "192.0.2.1" });

        Assert.True(result.Succeeded);
        var row = Assert.Single(_rows);
        Assert.Same(winner, row);
        Assert.Equal("192.0.2.1", row.AllowedIps);
        _repository.Verify(r => r.Discard(It.Is<UserSignInPolicy>(p => p != winner)), Times.Once);
        _repository.Verify(r => r.UpdateAsync(winner, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetIpAllowList_OnASuperAdmin_ByASuperAdmin_IsAllowed()
    {
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(It.IsAny<Guid>())).ReturnsAsync(true);

        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = true, AllowedIps = "192.0.2.1" });

        Assert.True(result.Succeeded);
        Assert.Single(_rows);
    }

    private static IOptionsMonitor<IdentityOptions> Monitor(IdentityOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<IdentityOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        return monitor.Object;
    }

    [Fact]
    public async Task GetAsync_WithoutARow_ReturnsAnAllOffPolicy_NotNotFound()
    {
        var result = await CreateService().GetAsync(_user.Id);

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.IpAllowListEnabled);
        Assert.Null(result.Data.AllowedIps);
        Assert.Empty(result.Data.Entries);
        Assert.Equal("198.51.100.7", result.Data.CallerIpAddress);
    }

    [Fact]
    public async Task GetAsync_OutOfTenantScope_AnswersNotFound()
    {
        _scope.Setup(s => s.ContainsAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateService().GetAsync(_user.Id);

        Assert.Equal(404, result.Code);
        _userManager.Verify(m => m.FindByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_ReportsWhichExemptRolesMakeTheListInert()
    {
        _exemptRoles = ["coordinator"];
        _rows.Add(new UserSignInPolicy { Id = Guid.NewGuid(), UserId = _user.Id, IpAllowListEnabled = true, AllowedIps = "10.0.0.0/8" });

        var result = await CreateService().GetAsync(_user.Id);

        Assert.True(result.Data!.IpAllowListEnabled);
        Assert.Equal(["Coordinator"], result.Data.ExemptedByRoles);
    }

    [Fact]
    public async Task SetIpAllowList_EnablingWithoutAnyEntry_IsRefused()
    {
        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = true, AllowedIps = "# just a comment" });

        Assert.Equal(400, result.Code);
        Assert.Contains("at least one", result.Message);
        _repository.Verify(r => r.InsertAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetIpAllowList_AnyMalformedEntry_RefusesTheWholeWrite_NamingTheEntries()
    {
        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = false, AllowedIps = "10.0.0.1\nhead-office\n10.0.0.0/33" });

        Assert.Equal(400, result.Code);
        Assert.Contains("head-office", result.Message);
        Assert.Contains("10.0.0.0/33", result.Message);
        _repository.Verify(r => r.InsertAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetIpAllowList_FirstWrite_InsertsARow_KeepingTheOperatorsTextVerbatim()
    {
        const string text = "# office\r\n203.0.113.5\r\n10.0.0.0/8 ; 2001:db8::/32";

        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = true, AllowedIps = text });

        Assert.True(result.Succeeded);
        var row = Assert.Single(_rows);
        Assert.Equal(_user.Id, row.UserId);
        Assert.True(row.IpAllowListEnabled);
        Assert.Equal(text, row.AllowedIps);
        Assert.Equal(["203.0.113.5", "10.0.0.0/8", "2001:db8::/32"], result.Data!.Entries);
    }

    [Fact]
    public async Task SetIpAllowList_SecondWrite_UpdatesTheExistingRow()
    {
        var existing = new UserSignInPolicy { Id = Guid.NewGuid(), UserId = _user.Id, IpAllowListEnabled = true, AllowedIps = "10.0.0.1" };
        _rows.Add(existing);

        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = false, AllowedIps = "10.0.0.2" });

        Assert.True(result.Succeeded);
        Assert.False(existing.IpAllowListEnabled);
        Assert.Equal("10.0.0.2", existing.AllowedIps);
        _repository.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.InsertAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>「关了、清空了」就是「没有限制」，而没有限制的规范形态是没有行。</summary>
    [Fact]
    public async Task SetIpAllowList_DisabledAndEmpty_DeletesTheRow()
    {
        var existing = new UserSignInPolicy { Id = Guid.NewGuid(), UserId = _user.Id, IpAllowListEnabled = true, AllowedIps = "10.0.0.1" };
        _rows.Add(existing);

        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = false, AllowedIps = "  " });

        Assert.True(result.Succeeded);
        Assert.Empty(_rows);
        Assert.False(result.Data!.IpAllowListEnabled);
        _repository.Verify(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetIpAllowList_DisabledAndEmptyWithNoRow_IsANoOp()
    {
        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = false, AllowedIps = null });

        Assert.True(result.Succeeded);
        _repository.Verify(r => r.InsertAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.DeleteAsync(It.IsAny<UserSignInPolicy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetIpAllowList_OutOfTenantScope_AnswersNotFoundAndWritesNothing()
    {
        _scope.Setup(s => s.ContainsAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateService().SetIpAllowListAsync(_user.Id, new SetIpAllowListDto { Enabled = true, AllowedIps = "10.0.0.1" });

        Assert.Equal(404, result.Code);
        Assert.Empty(_rows);
    }
}
