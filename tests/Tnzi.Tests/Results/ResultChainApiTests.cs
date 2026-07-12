namespace Tnzi.Tests.Results;

/// <summary>
/// Result 链式 API（Match / Bind / Switch / FailIf / Else / Try）的行为测试，
/// 重点锁定「成功但数据为 null」是合法成功状态：分支只依据 <see cref="BaseResult{T}.Succeeded"/>，
/// 空数据走成功分支并把 null 传给 continuation，而不是被误判为失败。
/// </summary>
public class ResultChainApiTests
{
    #region Match

    [Fact]
    public void Match_WhenSuccessWithValue_ShouldRouteToOnSuccess()
    {
        var result = Result<string>.Success("hello");

        var output = result.Match(
            onSuccess: data => $"ok:{data}",
            onFailure: _ => "fail");

        Assert.Equal("ok:hello", output);
    }

    [Fact]
    public void Match_WhenSuccessWithNull_ShouldRouteToOnSuccessWithNull()
    {
        var result = Result<string?>.Success(null);
        string? observed = "sentinel";

        var output = result.Match(
            onSuccess: data => { observed = data; return "success-branch"; },
            onFailure: _ => "failure-branch");

        Assert.Equal("success-branch", output);
        Assert.Null(observed);
    }

    [Fact]
    public void Match_WhenFailure_ShouldRouteToOnFailureWithPreservedError()
    {
        var result = Result<string>.Failure("boom", 404, "NOT_FOUND");

        var output = result.Match(
            onSuccess: _ => "ok",
            onFailure: r => $"{r.Message}:{r.Code}:{r.ErrorCode}");

        Assert.Equal("boom:404:NOT_FOUND", output);
    }

    [Fact]
    public async Task MatchAsync_WhenSuccessWithNull_ShouldRouteToOnSuccess()
    {
        var result = Result<string?>.Success(null);

        var output = await result.MatchAsync(
            onSuccess: _ => Task.FromResult("success-branch"),
            onFailure: _ => Task.FromResult("failure-branch"));

        Assert.Equal("success-branch", output);
    }

    [Fact]
    public async Task MatchAsync_OnTask_WhenSuccessWithNull_ShouldRouteToOnSuccess()
    {
        var resultTask = Task.FromResult(Result<string?>.Success(null));

        var output = await resultTask.MatchAsync(
            onSuccess: _ => Task.FromResult("success-branch"),
            onFailure: _ => Task.FromResult("failure-branch"));

        Assert.Equal("success-branch", output);
    }

    [Fact]
    public void Match_NoData_WhenSuccess_ShouldRouteToOnSuccess()
    {
        var result = Result.Success();

        var output = result.Match(
            onSuccess: () => "ok",
            onFailure: _ => "fail");

        Assert.Equal("ok", output);
    }

    [Fact]
    public void Match_NoData_WhenFailure_ShouldRouteToOnFailure()
    {
        var result = Result.Failure("boom", 400);

        var output = result.Match(
            onSuccess: () => "ok",
            onFailure: r => r.Message ?? "");

        Assert.Equal("boom", output);
    }

    #endregion

    #region Switch

    [Fact]
    public void Switch_WhenSuccessWithNull_ShouldInvokeOnSuccessWithNull()
    {
        var result = Result<string?>.Success(null);
        var successCalled = false;
        var failureCalled = false;
        string? observed = "sentinel";

        result.Switch(
            onSuccess: data => { successCalled = true; observed = data; },
            onFailure: _ => failureCalled = true);

        Assert.True(successCalled);
        Assert.False(failureCalled);
        Assert.Null(observed);
    }

    [Fact]
    public void Switch_WhenFailure_ShouldInvokeOnFailure()
    {
        var result = Result<string>.Failure("boom", 400);
        var successCalled = false;
        var failureCalled = false;

        result.Switch(
            onSuccess: _ => successCalled = true,
            onFailure: _ => failureCalled = true);

        Assert.False(successCalled);
        Assert.True(failureCalled);
    }

    #endregion

    #region Bind

    [Fact]
    public void Bind_WhenSuccessWithValue_ShouldInvokeBinder()
    {
        var result = Result<int>.Success(21);

        var bound = result.Bind(x => Result<int>.Success(x * 2));

        Assert.True(bound.Succeeded);
        Assert.Equal(42, bound.Data);
    }

    [Fact]
    public void Bind_WhenSuccessWithNull_ShouldInvokeBinderWithNull()
    {
        var result = Result<string?>.Success(null);
        string? observed = "sentinel";

        var bound = result.Bind(data =>
        {
            observed = data;
            return Result<string>.Success("from-binder");
        });

        // 空数据是合法成功值：binder 应被调用（收到 null），而不是被伪造成失败
        Assert.True(bound.Succeeded);
        Assert.Equal("from-binder", bound.Data);
        Assert.Null(observed);
    }

    [Fact]
    public void Bind_WhenFailure_ShouldPropagateErrorWithoutInvokingBinder()
    {
        var result = Result<string>.Failure("original error", 409, "CONFLICT", new { detail = 1 });
        var binderCalled = false;

        var bound = result.Bind(_ =>
        {
            binderCalled = true;
            return Result<int>.Success(1);
        });

        Assert.False(binderCalled);
        Assert.False(bound.Succeeded);
        Assert.Equal("original error", bound.Message);
        Assert.Equal(409, bound.Code);
        Assert.Equal("CONFLICT", bound.ErrorCode);
        Assert.NotNull(bound.ErrorDetails);
    }

    [Fact]
    public void Bind_WhenChainedAndMiddleFails_ShouldShortCircuitAndPreserveFirstError()
    {
        var bound = Result<int>.Success(1)
            .Bind(_ => Result<int>.Failure("step2 failed", 422, "UNPROCESSABLE"))
            .Bind(_ => Result<int>.Success(999));

        Assert.False(bound.Succeeded);
        Assert.Equal("step2 failed", bound.Message);
        Assert.Equal(422, bound.Code);
        Assert.Equal("UNPROCESSABLE", bound.ErrorCode);
    }

    [Fact]
    public async Task BindAsync_WhenSuccessWithNull_ShouldInvokeBinder()
    {
        var result = Result<string?>.Success(null);

        var bound = await result.BindAsync(_ => Task.FromResult(Result<string>.Success("ok")));

        Assert.True(bound.Succeeded);
        Assert.Equal("ok", bound.Data);
    }

    [Fact]
    public async Task BindAsync_OnTask_WhenSuccessWithNull_ShouldInvokeBinder()
    {
        var resultTask = Task.FromResult(Result<string?>.Success(null));

        var bound = await resultTask.BindAsync(_ => Task.FromResult(Result<string>.Success("ok")));

        Assert.True(bound.Succeeded);
        Assert.Equal("ok", bound.Data);
    }

    [Fact]
    public async Task Bind_OnTask_WithSyncBinder_WhenSuccess_ShouldInvokeBinder()
    {
        var resultTask = Task.FromResult(Result<int>.Success(10));

        var bound = await resultTask.Bind(x => Result<int>.Success(x + 5));

        Assert.True(bound.Succeeded);
        Assert.Equal(15, bound.Data);
    }

    [Fact]
    public void Bind_NoData_WhenSuccess_ShouldInvokeBinder()
    {
        var result = Result.Success();
        var bound = result.Bind(() => Result.Success("chained"));

        Assert.True(bound.Succeeded);
    }

    [Fact]
    public void Bind_NoData_WhenFailure_ShouldPassthrough()
    {
        var result = Result.Failure("boom", 400);
        var binderCalled = false;

        var bound = result.Bind(() => { binderCalled = true; return Result.Success(); });

        Assert.False(binderCalled);
        Assert.False(bound.Succeeded);
        Assert.Equal("boom", bound.Message);
    }

    #endregion

    #region FailIf (chained)

    [Fact]
    public void FailIf_WhenSuccessWithNullAndPredicateFlagsNull_ShouldFail()
    {
        var result = Result<string?>.Success(null);

        // 「必须非空」断言：空数据不再被静默跳过，predicate 命中 → 转失败
        var flagged = result.FailIf(x => x is null, "Value is required", 400, "REQUIRED");

        Assert.False(flagged.Succeeded);
        Assert.Equal("Value is required", flagged.Message);
        Assert.Equal("REQUIRED", flagged.ErrorCode);
    }

    [Fact]
    public void FailIf_WhenSuccessWithNullAndPredicatePasses_ShouldKeepSuccess()
    {
        var result = Result<string?>.Success(null);

        var checked2 = result.FailIf(x => x == "forbidden", "not allowed");

        Assert.True(checked2.Succeeded);
    }

    [Fact]
    public void FailIf_WhenFailure_ShouldPassthroughUnchanged()
    {
        var result = Result<string>.Failure("original", 404);
        var predicateCalled = false;

        var checked2 = result.FailIf(_ => { predicateCalled = true; return true; }, "should not apply");

        Assert.False(predicateCalled);
        Assert.False(checked2.Succeeded);
        Assert.Equal("original", checked2.Message);
        Assert.Equal(404, checked2.Code);
    }

    [Fact]
    public void FailIf_WithMessageFactory_WhenSuccessWithNull_ShouldRunPredicate()
    {
        var result = Result<string?>.Success(null);

        var checked2 = result.FailIf(
            predicate: x => x is null,
            failureMessageFactory: x => $"bad value: {x ?? "<null>"}",
            code: 400);

        Assert.False(checked2.Succeeded);
        Assert.Equal("bad value: <null>", checked2.Message);
    }

    #endregion

    #region Else (error recovery)

    [Fact]
    public void Else_WhenFailure_ShouldRecoverWithFallbackValue()
    {
        var result = Result<int>.Failure("boom", 500);

        var recovered = result.Else(42);

        Assert.True(recovered.Succeeded);
        Assert.Equal(42, recovered.Data);
    }

    [Fact]
    public void Else_WhenSuccess_ShouldKeepOriginalValue()
    {
        var result = Result<int>.Success(7);

        var recovered = result.Else(42);

        Assert.True(recovered.Succeeded);
        Assert.Equal(7, recovered.Data);
    }

    [Fact]
    public void Else_WithFactory_WhenFailure_ShouldRecoverUsingFailureInfo()
    {
        var result = Result<string>.Failure("not found", 404);

        var recovered = result.Else(r => $"fallback-for-{r.Code}");

        Assert.True(recovered.Succeeded);
        Assert.Equal("fallback-for-404", recovered.Data);
    }

    [Fact]
    public async Task ElseAsync_WhenFailure_ShouldRecoverAsync()
    {
        var result = Result<string>.Failure("boom", 500);

        var recovered = await result.ElseAsync(_ => Task.FromResult("async-fallback"));

        Assert.True(recovered.Succeeded);
        Assert.Equal("async-fallback", recovered.Data);
    }

    #endregion

    #region Try (exception wrapping)

    [Fact]
    public void Try_WhenActionSucceeds_ShouldReturnSuccess()
    {
        var result = ResultTryExtensions.Try(() => 123);

        Assert.True(result.Succeeded);
        Assert.Equal(123, result.Data);
    }

    [Fact]
    public void Try_WhenActionReturnsNull_ShouldStillBeSuccess()
    {
        var result = ResultTryExtensions.Try<string?>(() => null);

        Assert.True(result.Succeeded);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Try_WhenActionThrows_ShouldReturnFailureFromException()
    {
        var result = ResultTryExtensions.Try<int>(() => throw new BusinessException("bad", ErrorCodes.VALIDATION_ERROR, 400));

        Assert.False(result.Succeeded);
        Assert.Equal("bad", result.Message);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task TryAsync_WhenActionThrows_ShouldReturnFailure()
    {
        var result = await ResultTryExtensions.TryAsync<int>(() => throw new InvalidOperationException("kaboom"));

        Assert.False(result.Succeeded);
        Assert.Equal("kaboom", result.Message);
        Assert.Equal(500, result.Code);
    }

    #endregion
}
