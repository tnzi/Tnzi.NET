using Tnzi.Exceptions;

namespace Tnzi.AI.Tests.Middleware;

/// <summary>
/// RetryMiddleware 功能测试 — 重试逻辑、禁用行为
/// </summary>
public class RetryMiddlewareTests
{
    #region 基本行为

    [Fact]
    public void Order_IsRetryOrder()
    {
        var mw = CreateMiddleware();
        mw.Order.ShouldBe(AiMiddlewareOrders.Retry);
    }

    [Fact]
    public async Task InvokeAsync_Success_ReturnsDirectly()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();

        var result = await mw.InvokeAsync(context,
            (ctx, ct) => Task.FromResult(new AgentRunResult { Response = "ok" }));

        result.Response.ShouldBe("ok");
    }

    #endregion

    #region 禁用行为

    [Fact]
    public async Task InvokeAsync_Disabled_PassesThrough()
    {
        var mw = CreateMiddleware(enabled: false);
        var context = TestHelpers.CreateMinimalContext();
        var callCount = 0;

        var result = await mw.InvokeAsync(context, (ctx, ct) =>
        {
            callCount++;
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        callCount.ShouldBe(1);
        result.Response.ShouldBe("ok");
    }

    #endregion

    #region 重试逻辑

    [Fact]
    public async Task InvokeAsync_TransientError_Retries()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        var callCount = 0;

        var result = await mw.InvokeAsync(context, (ctx, ct) =>
        {
            callCount++;
            if (callCount <= 2)
                throw new HttpRequestException("Server error", null, System.Net.HttpStatusCode.InternalServerError);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        callCount.ShouldBeGreaterThan(1, "Should retry on 5xx errors");
        result.Response.ShouldBe("ok");
    }

    [Fact]
    public async Task InvokeAsync_BusinessException_DoesNotRetry()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        var callCount = 0;

        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await mw.InvokeAsync(context, (ctx, ct) =>
            {
                callCount++;
                throw new BusinessException("Business error");
            });
        });

        callCount.ShouldBe(1, "BusinessException should NOT trigger retry");
    }

    [Fact]
    public async Task InvokeAsync_OperationCancelled_DoesNotRetry()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        var callCount = 0;

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await mw.InvokeAsync(context, (ctx, ct) =>
            {
                callCount++;
                throw new OperationCanceledException("Cancelled");
            });
        });

        callCount.ShouldBe(1, "OperationCanceledException should NOT trigger retry");
    }

    [Fact]
    public async Task InvokeAsync_Http429_Retries()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        var callCount = 0;

        var result = await mw.InvokeAsync(context, (ctx, ct) =>
        {
            callCount++;
            if (callCount == 1)
                throw new HttpRequestException("Rate limit", null, (System.Net.HttpStatusCode)429);
            return Task.FromResult(new AgentRunResult { Response = "ok" });
        });

        callCount.ShouldBe(2, "429 should trigger one retry");
        result.Response.ShouldBe("ok");
    }

    [Fact]
    public async Task InvokeAsync_Http400_DoesNotRetry()
    {
        var mw = CreateMiddleware();
        var context = TestHelpers.CreateMinimalContext();
        var callCount = 0;

        await Should.ThrowAsync<HttpRequestException>(async () =>
        {
            await mw.InvokeAsync(context, (ctx, ct) =>
            {
                callCount++;
                throw new HttpRequestException("Bad request", null, System.Net.HttpStatusCode.BadRequest);
            });
        });

        callCount.ShouldBe(1, "400 should NOT trigger retry");
    }

    #endregion

    #region 流式行为

    [Fact]
    public async Task InvokeStreamingAsync_Disabled_PassesThrough()
    {
        var mw = CreateMiddleware(enabled: false);
        var context = TestHelpers.CreateMinimalContext();
        var chunks = new List<string>();

        await foreach (var chunk in mw.InvokeStreamingAsync(context, (ctx, ct) => CreateTestStream("hello")))
        {
            chunks.Add(chunk.Text ?? "");
        }

        chunks.ShouldContain("hello");
    }

    #endregion

    #region Helpers

    private static RetryMiddleware CreateMiddleware(bool enabled = true)
    {
        var options = new AIOptions
        {
            Retry = new RetryOptions
            {
                Enabled = enabled,
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromMilliseconds(1), // 测试时最小延迟
                MaxDelay = TimeSpan.FromMilliseconds(10),
                BackoffMultiplier = 2,
                CircuitBreakerDuration = TimeSpan.FromSeconds(30),
                CircuitBreakerFailureThreshold = 100 // 高阈值避免测试中触发熔断
            }
        };

        return new RetryMiddleware(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<RetryMiddleware>.Instance);
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<AgentStreamChunk> CreateTestStream(string text, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new AgentStreamChunk { Text = text };
    }
#pragma warning restore CS1998

    #endregion
}
