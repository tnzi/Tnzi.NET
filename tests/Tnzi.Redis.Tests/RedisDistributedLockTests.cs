namespace Tnzi.Redis.Tests;

/// <summary>
/// RedisDistributedLock 行为测试。
/// 覆盖获取/释放的基本语义，以及自动续租看门狗（Task 4）：续租触发、可关闭、
/// 续租失败标记锁丢失、释放后停止续租且句柄不可再延长。
/// </summary>
public class RedisDistributedLockTests
{
    private static RedisResult Ok(long value) => RedisResult.Create((RedisValue)value, ResultType.Integer);

    private static (Mock<IConnectionMultiplexer> Mux, Mock<IDatabase> Db) BuildMux(bool acquireSucceeds = true)
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(acquireSucceeds);
        // 默认脚本（续租/释放）都返回成功，个别测试再覆盖
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Ok(1));

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return (mux, db);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        for (int waited = 0; waited < timeoutMs; waited += 20)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    // ============ 获取 / 释放基本语义 ============

    [Fact]
    public async Task AcquireAsync_ReturnsHandle_WhenLockIsFree()
    {
        var (mux, _) = BuildMux(acquireSucceeds: true);
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { EnableAutoRenewal = false });

        await using var handle = await sut.AcquireAsync("resource");

        Assert.NotNull(handle);
        Assert.Equal("resource", handle!.Key);
        Assert.True(handle.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_ReturnsNull_WhenLockAlreadyHeld()
    {
        var (mux, _) = BuildMux(acquireSucceeds: false);
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { EnableAutoRenewal = false });

        // 无超时 => 只尝试一次，失败返回 null
        var handle = await sut.AcquireAsync("resource");

        Assert.Null(handle);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesLock_AndMarksNotAcquired()
    {
        var (mux, db) = BuildMux();
        int releaseCalls = 0;
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns((string script, RedisKey[] _, RedisValue[] __, CommandFlags ___) =>
            {
                if (script.Contains("del")) Interlocked.Increment(ref releaseCalls);
                return Task.FromResult(Ok(1));
            });
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { EnableAutoRenewal = false });

        var handle = await sut.AcquireAsync("r");
        await handle!.DisposeAsync();

        Assert.Equal(1, releaseCalls);
        Assert.False(handle.IsAcquired);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var (mux, db) = BuildMux();
        int releaseCalls = 0;
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns((string script, RedisKey[] _, RedisValue[] __, CommandFlags ___) =>
            {
                if (script.Contains("del")) Interlocked.Increment(ref releaseCalls);
                return Task.FromResult(Ok(1));
            });
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { EnableAutoRenewal = false });

        var handle = await sut.AcquireAsync("r");
        await handle!.DisposeAsync();
        await handle.DisposeAsync();

        Assert.Equal(1, releaseCalls);
    }

    [Fact]
    public async Task ExtendAsync_AfterDispose_ReturnsFalse()
    {
        var (mux, _) = BuildMux();
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { EnableAutoRenewal = false });

        var handle = await sut.AcquireAsync("r");
        await handle!.DisposeAsync();

        Assert.False(await handle.ExtendAsync(TimeSpan.FromSeconds(30)));
    }

    // ============ Task 4: 自动续租看门狗 ============

    [Fact]
    public async Task AutoRenewal_PeriodicallyExtendsLock()
    {
        var (mux, db) = BuildMux();
        var firstExtend = new TaskCompletionSource();
        int extendCalls = 0;
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns((string script, RedisKey[] _, RedisValue[] __, CommandFlags ___) =>
            {
                if (script.Contains("pexpire"))
                {
                    Interlocked.Increment(ref extendCalls);
                    firstExtend.TrySetResult();
                }
                return Task.FromResult(Ok(1));
            });
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { DefaultExpirySeconds = 3, EnableAutoRenewal = true });

        await using var handle = await sut.AcquireAsync("r");

        // 续租间隔 = max(1s, expiry/3) = 1s；等首次续租触发（不做固定长 sleep）
        await firstExtend.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(extendCalls >= 1);
        Assert.True(handle!.IsAcquired);
    }

    [Fact]
    public async Task AutoRenewalDisabled_NeverExtends()
    {
        var (mux, db) = BuildMux();
        int extendCalls = 0;
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns((string script, RedisKey[] _, RedisValue[] __, CommandFlags ___) =>
            {
                if (script.Contains("pexpire")) Interlocked.Increment(ref extendCalls);
                return Task.FromResult(Ok(1));
            });
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { DefaultExpirySeconds = 3, EnableAutoRenewal = false });

        await using var handle = await sut.AcquireAsync("r");
        // 超过一个续租间隔仍不应有续租发生
        await Task.Delay(1300);

        Assert.Equal(0, extendCalls);
    }

    [Fact]
    public async Task AutoRenewal_WhenExtendFails_MarksLockLost()
    {
        var (mux, db) = BuildMux();
        // 续租脚本返回 0（锁已被抢占/丢失），释放脚本仍返回 1
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns((string script, RedisKey[] _, RedisValue[] __, CommandFlags ___) =>
                Task.FromResult(script.Contains("pexpire") ? Ok(0) : Ok(1)));
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { DefaultExpirySeconds = 3, EnableAutoRenewal = true });

        await using var handle = await sut.AcquireAsync("r");

        var lost = await WaitUntilAsync(() => !handle!.IsAcquired);

        Assert.True(lost);
        Assert.False(handle!.IsAcquired);
    }

    [Fact]
    public async Task DisposeAsync_StopsAutoRenewal()
    {
        var (mux, db) = BuildMux();
        int extendCalls = 0;
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Returns((string script, RedisKey[] _, RedisValue[] __, CommandFlags ___) =>
            {
                if (script.Contains("pexpire")) Interlocked.Increment(ref extendCalls);
                return Task.FromResult(Ok(1));
            });
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { DefaultExpirySeconds = 3, EnableAutoRenewal = true });

        var handle = await sut.AcquireAsync("r");
        await handle!.DisposeAsync();
        var countAtDispose = Volatile.Read(ref extendCalls);

        // 释放后等待超过一个续租间隔，续租次数不应再增长
        await Task.Delay(1300);

        Assert.Equal(countAtDispose, Volatile.Read(ref extendCalls));
        Assert.False(handle.IsAcquired);
    }
}
