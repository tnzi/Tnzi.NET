using Tnzi.Exceptions;

namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// RetryMiddleware 增强功能测试 — 后台任务 429 中止、MaxRetryAfter、AbortBackgroundOn429 默认值
/// </summary>
public class RetryMiddlewareEnhancedTests
{
    #region 后台任务 429 中止

    [Fact]
    public async Task ShouldRetry_BackgroundTask429_ReturnsFalse()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        context.Properties["is_background_task"] = true;
        var callCount = 0;

        // 后台任务遇到 429 应立即中止，不重试
        await Should.ThrowAsync<HttpRequestException>(async () =>
        {
            await mw.InvokeAsync(context, (ctx, ct) =>
            {
                callCount++;
                throw new HttpRequestException("Rate limit", null, (System.Net.HttpStatusCode)429);
            });
        });

        callCount.ShouldBe(1, "Background task should NOT retry on 429");
    }

    [Fact]
    public async Task ShouldRetry_ForegroundTask429_ReturnsTrue()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        // 不设置 is_background_task — 默认为前台任务
        var callCount = 0;

        var result = await mw.InvokeAsync(context, (ctx, ct) =>
        {
            callCount++;
            if (callCount == 1)
                throw new HttpRequestException("Rate limit", null, (System.Net.HttpStatusCode)429);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        callCount.ShouldBe(2, "Foreground task should retry on 429");
        result.Response.ShouldBe("ok");
    }

    [Fact]
    public async Task ShouldRetry_BackgroundTask500_ReturnsTrue()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        context.Properties["is_background_task"] = true;
        var callCount = 0;

        // 后台任务遇到 500 仍然应该重试（只有 429 被特殊处理）
        var result = await mw.InvokeAsync(context, (ctx, ct) =>
        {
            callCount++;
            if (callCount == 1)
                throw new HttpRequestException("Server error", null, System.Net.HttpStatusCode.InternalServerError);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        callCount.ShouldBeGreaterThan(1, "Background task should still retry on 500");
        result.Response.ShouldBe("ok");
    }

    #endregion

    #region 后台任务 429 中止 — AbortBackgroundOn429 禁用时仍重试

    [Fact]
    public async Task ShouldRetry_BackgroundTask429_AbortDisabled_StillRetries()
    {
        var mw = CreateMiddleware(abortBackgroundOn429: false);
        var context = TestHelpers.CreateMinimalContext();
        context.Properties["is_background_task"] = true;
        var callCount = 0;

        var result = await mw.InvokeAsync(context, (ctx, ct) =>
        {
            callCount++;
            if (callCount == 1)
                throw new HttpRequestException("Rate limit", null, (System.Net.HttpStatusCode)429);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        callCount.ShouldBe(2, "When AbortBackgroundOn429 is false, background tasks should still retry on 429");
        result.Response.ShouldBe("ok");
    }

    #endregion

    #region RetryOptions 默认值

    [Fact]
    public void RetryOptions_DefaultMaxRetryAfter_Is20()
    {
        var options = new RetryOptions();
        options.MaxRetryAfterSeconds.ShouldBe(20);
    }

    [Fact]
    public void RetryOptions_DefaultAbortBackground_IsTrue()
    {
        var options = new RetryOptions();
        options.AbortBackgroundOn429.ShouldBeTrue();
    }

    #endregion

    #region 流式后台任务 429 中止

    [Fact]
    public async Task InvokeStreamingAsync_BackgroundTask429_Aborts()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        context.Properties["is_background_task"] = true;
        var callCount = 0;

        // 流式路径中后台任务 429 也应立即中止（不重试）
        await Should.ThrowAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in mw.InvokeStreamingAsync(context, (ctx, ct) =>
            {
                callCount++;
                throw new HttpRequestException("Rate limit", null, (System.Net.HttpStatusCode)429);
#pragma warning disable CS0162
                return CreateTestStream("never");
#pragma warning restore CS0162
            }))
            {
                // 不应该到这里
            }
        });

        callCount.ShouldBe(1, "Background streaming task should NOT retry on 429");
    }

    #endregion

    #region Helpers

    private static RetryMiddleware CreateMiddleware(bool enabled = true, bool abortBackgroundOn429 = true)
    {
        var options = new AIOptions
        {
            Retry = new RetryOptions
            {
                Enabled = enabled,
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(10),
                BackoffMultiplier = 2,
                CircuitBreakerDuration = TimeSpan.FromSeconds(30),
                CircuitBreakerFailureThreshold = 100,
                MaxRetryAfterSeconds = 20,
                AbortBackgroundOn429 = abortBackgroundOn429
            }
        };

        return new RetryMiddleware(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<RetryMiddleware>.Instance);
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<AgentStreamChunk> CreateTestStream(
        string text,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new AgentStreamChunk { Text = text };
    }
#pragma warning restore CS1998

    #endregion
}
