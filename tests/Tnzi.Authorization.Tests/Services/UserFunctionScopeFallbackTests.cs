namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// <see cref="IUserFunctionService"/> 有界写入的**默认实现**（读-改-写回退）测试。
/// </summary>
/// <remarks>
/// 框架自己的 <c>UserFunctionService</c> 重写了这两个方法（单 UnitOfWork 原子完成），
/// 走不到默认实现；但接口是公开的，任何只实现了抽象成员的第三方实现都会继承这份回退。
/// 回退可以不原子，但**不能不有界**——切片外的直授一旦被它删掉，这个 API 就白加了。
/// 故这里用一个只实现抽象成员的假实现，逐字锁住边界语义。
/// </remarks>
public class UserFunctionScopeFallbackTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InScopeA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid InScopeB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OutOfScope = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Fallback_set_preserves_grants_outside_the_scope()
    {
        IUserFunctionService service = new FakeUserFunctionService { Allowed = { InScopeA, OutOfScope } };

        var result = await service.SetUserFunctionsInScopeAsync(UserId, [InScopeA, InScopeB], [InScopeB]);

        result.Succeeded.ShouldBeTrue();
        var ids = (await service.GetUserFunctionIdsAsync(UserId)).Data!.ToList();
        ids.ShouldContain(InScopeB);
        ids.ShouldNotContain(InScopeA);
        ids.ShouldContain(OutOfScope);
    }

    [Fact]
    public async Task Fallback_set_denied_preserves_deny_rows_outside_the_scope()
    {
        IUserFunctionService service = new FakeUserFunctionService { Denied = { InScopeA, OutOfScope } };

        var result = await service.SetUserDeniedFunctionsInScopeAsync(UserId, [InScopeA, InScopeB], [InScopeB]);

        result.Succeeded.ShouldBeTrue();
        var ids = (await service.GetUserDeniedFunctionIdsAsync(UserId)).Data!.ToList();
        ids.ShouldContain(InScopeB);
        ids.ShouldNotContain(InScopeA);
        ids.ShouldContain(OutOfScope);
    }

    [Fact]
    public async Task Fallback_rejects_ids_outside_the_declared_scope_without_writing()
    {
        var fake = new FakeUserFunctionService { Allowed = { OutOfScope } };
        IUserFunctionService service = fake;

        var result = await service.SetUserFunctionsInScopeAsync(UserId, [InScopeA], [InScopeA, OutOfScope]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        fake.WriteCount.ShouldBe(0);
        fake.Allowed.ShouldBe(new HashSet<Guid> { OutOfScope }, ignoreOrder: true);
    }

    [Fact]
    public async Task Fallback_propagates_a_failing_read_instead_of_writing()
    {
        var fake = new FakeUserFunctionService { ReadFailure = "boom" };
        IUserFunctionService service = fake;

        var result = await service.SetUserFunctionsInScopeAsync(UserId, [InScopeA], [InScopeA]);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("boom");
        fake.WriteCount.ShouldBe(0);
    }

    /// <summary>
    /// 只实现抽象成员的最小实现，用两个集合建模 allow/deny 互斥（唯一索引的内存等价物）。
    /// </summary>
    private sealed class FakeUserFunctionService : IUserFunctionService
    {
        public HashSet<Guid> Allowed { get; } = [];
        public HashSet<Guid> Denied { get; } = [];
        public int WriteCount { get; private set; }
        public string? ReadFailure { get; init; }

        public Task<Result<IEnumerable<Guid>>> GetUserFunctionIdsAsync(Guid userId) =>
            Task.FromResult(ReadFailure != null
                ? Result.Failure<IEnumerable<Guid>>(ReadFailure, 500)
                : Result.Success<IEnumerable<Guid>>(Allowed.ToList()));

        public Task<Result<IEnumerable<Guid>>> GetUserDeniedFunctionIdsAsync(Guid userId) =>
            Task.FromResult(ReadFailure != null
                ? Result.Failure<IEnumerable<Guid>>(ReadFailure, 500)
                : Result.Success<IEnumerable<Guid>>(Denied.ToList()));

        public Task<Result> SetUserFunctionsAsync(Guid userId, IEnumerable<Guid> functionIds)
        {
            WriteCount++;
            var ids = functionIds.ToHashSet();
            Allowed.Clear();
            Allowed.UnionWith(ids);
            Denied.ExceptWith(ids);          // 显式授予翻转 deny 行
            return Task.FromResult(Result.Success());
        }

        public Task<Result> SetUserDeniedFunctionsAsync(Guid userId, IEnumerable<Guid> functionIds)
        {
            WriteCount++;
            var ids = functionIds.ToHashSet();
            Denied.Clear();
            Denied.UnionWith(ids);
            Allowed.ExceptWith(ids);         // 显式拒绝翻转 allow 行
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IEnumerable<ModuleFunction>>> GetUserFunctionsAsync(Guid userId) =>
            throw new NotSupportedException();

        public Task<Result> AssignFunctionsToUserAsync(Guid userId, IEnumerable<Guid> functionIds) =>
            throw new NotSupportedException();

        public Task<Result> RemoveFunctionsFromUserAsync(Guid userId, IEnumerable<Guid> functionIds) =>
            throw new NotSupportedException();

        public Task<Result> ClearUserFunctionsAsync(Guid userId) =>
            throw new NotSupportedException();
    }
}
