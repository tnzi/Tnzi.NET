namespace Tnzi.Identity.Tests;

public class UserNamePolicyTests
{
    [Theory]
    [InlineData(null, "a@x.com", "a@x.com")]
    [InlineData("", "a@x.com", "a@x.com")]
    [InlineData("A@X.com", "a@x.com", "a@x.com")]
    [InlineData(" a@x.com ", "a@x.com", "a@x.com")]
    public void EmailAsUserName_WithEmail_ResolvesToEmail(string? requested, string email, string expected)
    {
        var result = UserNamePolicy.ResolveForNewAccount(requested, email, useEmailAsUserName: true);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Data);
    }

    [Fact]
    public void EmailAsUserName_WithDifferentUserName_IsRejected()
    {
        var result = UserNamePolicy.ResolveForNewAccount("alice", "a@x.com", useEmailAsUserName: true);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("agent-7", "agent-7")]
    public void EmailAsUserName_WithoutEmail_FallsBackToRequested(string? requested, string? expected)
    {
        var result = UserNamePolicy.ResolveForNewAccount(requested, null, useEmailAsUserName: true);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Data);
    }

    [Theory]
    [InlineData("E1024", "b@x.com", "E1024")]
    [InlineData(null, "b@x.com", null)]
    [InlineData("b@x.com", "b@x.com", "b@x.com")]
    public void IndependentUserName_IsKeptAsGiven(string? requested, string? email, string? expected)
    {
        var result = UserNamePolicy.ResolveForNewAccount(requested, email, useEmailAsUserName: false);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Data);
    }

    [Theory]
    [InlineData("victim@x.com", "me@x.com")]
    [InlineData("victim@x.com", null)]
    public void IndependentUserName_ShapedLikeSomeoneElsesEmail_IsRejected(string requested, string? email)
    {
        var result = UserNamePolicy.ResolveForNewAccount(requested, email, useEmailAsUserName: false);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Theory]
    [InlineData("a@x.com", "a@x.com", true)]
    [InlineData("A@X.COM", "a@x.com", true)]
    [InlineData("admin", "a@x.com", false)]
    [InlineData("a@x.com", null, false)]
    public void FollowsEmail_ComparesValuesIgnoringCase(string userName, string? email, bool expected)
    {
        Assert.Equal(expected, UserNamePolicy.FollowsEmail(new User { UserName = userName, Email = email }));
    }
}

public class UserNameGeneratorTests
{
    private static Func<string, Task<bool>> Taken(params string[] names)
        => name => Task.FromResult(names.Contains(name, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task OwnEmail_IsUsedVerbatim_EvenWithCharactersTheSanitizerWouldDrop()
    {
        // 清洗会把它变成 ab@x.com，一个长得像另一个人邮箱的用户名
        var userName = await UserNameGenerator.GenerateUniqueAsync("a+b@x.com", Taken(), "a+b@x.com");

        Assert.Equal("a+b@x.com", userName);
    }

    [Fact]
    public async Task OwnEmail_AlreadyTaken_FallsBackToTheLocalPart_WithoutAnAtSign()
    {
        var userName = await UserNameGenerator.GenerateUniqueAsync("alice@x.com", Taken("alice@x.com", "alice"), "alice@x.com");

        Assert.Equal("alice_1", userName);
    }

    [Fact]
    public async Task OwnEmailWithoutAnAtSign_Taken_DoesNotThrow()
    {
        var userName = await UserNameGenerator.GenerateUniqueAsync("odd-value", Taken("odd-value"), "odd-value");

        Assert.Equal("odd-value_1", userName);
    }

    [Fact]
    public async Task NonEmailBase_NeverKeepsAnAtSign()
    {
        var userName = await UserNameGenerator.GenerateUniqueAsync("bob@corp", Taken(), "someone@x.com");

        Assert.DoesNotContain('@', userName);
    }
}

/// <summary>
/// 自定义 store 不支持 <see cref="IQueryableUserStore{TUser}"/> 时，校验器走按键查找的退路而不是抛异常。
/// 可查询 store 的路径由集成测试覆盖。
/// </summary>
public class CrossFieldIdentifierValidatorFallbackTests
{
    private static (UserManager<User> Manager, Mock<IUserEmailStore<User>> Store) CreateManager()
    {
        var store = new Mock<IUserStore<User>>();
        var emailStore = store.As<IUserEmailStore<User>>();
        var manager = new UserManager<User>(store.Object, null!, null!, null!, null!, new UpperInvariantLookupNormalizer(), null!, null!, null!);
        Assert.False(manager.SupportsQueryableUsers);
        return (manager, emailStore);
    }

    [Fact]
    public async Task NonQueryableStore_UserNameEqualToAnotherAccountsEmail_IsRejected()
    {
        var (manager, store) = CreateManager();
        store.Setup(s => s.FindByEmailAsync("VICTIM@X.COM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = Guid.NewGuid(), UserName = "victim", Email = "victim@x.com" });

        var result = await new CrossFieldIdentifierValidator().ValidateAsync(manager, new User { Id = Guid.NewGuid(), UserName = "victim@x.com", Email = "me@x.com" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == CrossFieldIdentifierValidator.UserNameIsAnotherUsersEmail);
    }

    [Fact]
    public async Task NonQueryableStore_OwnRecord_Passes()
    {
        var (manager, store) = CreateManager();
        var self = new User { Id = Guid.NewGuid(), UserName = "me@x.com", Email = "me@x.com" };
        store.Setup(s => s.FindByEmailAsync("ME@X.COM", It.IsAny<CancellationToken>())).ReturnsAsync(self);
        store.Setup(s => s.FindByNameAsync("ME@X.COM", It.IsAny<CancellationToken>())).ReturnsAsync(self);

        var result = await new CrossFieldIdentifierValidator().ValidateAsync(manager, self);

        Assert.True(result.Succeeded);
    }
}
