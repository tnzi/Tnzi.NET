using Tnzi.Threading;

namespace Tnzi.Tests.Threading;

/// <summary>
/// KeyedAsyncLock 并发正确性测试。
/// 重点验证引用计数 / 移除竞争不会破坏同一 key 的互斥保证
/// （即：同一 key 在任意时刻最多只有一个持有者）。
/// </summary>
public class KeyedAsyncLockConcurrencyTests
{
    /// <summary>
    /// 在同一 key 上高频取/放锁（refcount 频繁在 0↔1 跳动），
    /// 复现 "Decrement→0 尚未 TryRemove 时被另一线程重新获取同实例、
    /// 随后该实例被孤立、后续线程新建第二把锁" 的竞争 —— 导致两个持有者并存。
    /// 修复后该测试应稳定通过（maxConcurrent 恒为 1）。
    /// </summary>
    // 非原子共享计数器：仅依赖互斥保护其 read-modify-write。
    // 一旦互斥被破坏，并发的两个持有者会相互覆盖，产生永久性丢失更新，
    // 最终计数将 < 总迭代次数。这是比"瞬时并发计数 gauge"敏感得多的检测器。
    private long _unsafeCounter;

    [Fact]
    public async Task LockAsync_SameKeyUnderHeavyChurn_PreservesMutualExclusion()
    {
        var keyLock = new KeyedAsyncLock();
        const string key = "contended";
        var workers = Math.Max(8, Environment.ProcessorCount * 2);
        const int iterationsPerWorker = 30000;
        var expected = (long)workers * iterationsPerWorker;

        _unsafeCounter = 0;
        var maxObserved = 0;
        var activeHolders = 0;

        async Task Worker()
        {
            for (var i = 0; i < iterationsPerWorker; i++)
            {
                await using (await keyLock.LockAsync(key))
                {
                    var current = Interlocked.Increment(ref activeHolders);
                    int snapshot;
                    while (current > (snapshot = Volatile.Read(ref maxObserved)))
                    {
                        if (Interlocked.CompareExchange(ref maxObserved, current, snapshot) == snapshot)
                            break;
                    }

                    // 非原子 read-modify-write，并加宽窗口以放大丢失更新
                    var tmp = _unsafeCounter;
                    Thread.SpinWait(15);
                    _unsafeCounter = tmp + 1;

                    Interlocked.Decrement(ref activeHolders);
                }
            }
        }

        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(Worker)).ToArray();
        await Task.WhenAll(tasks);

        // 互斥成立 ⇒ 无丢失更新 ⇒ 计数等于总次数；且任意时刻最多一个持有者
        Assert.Equal(expected, _unsafeCounter);
        Assert.Equal(1, maxObserved);
        // 锁全部释放后字典应清空，不泄漏条目
        Assert.Equal(0, keyLock.ActiveLockCount);
    }

    /// <summary>
    /// 不同 key 之间互不阻塞：N 个不同 key 可同时持有。
    /// </summary>
    [Fact]
    public async Task LockAsync_DifferentKeys_DoNotBlockEachOther()
    {
        var keyLock = new KeyedAsyncLock();
        const int keyCount = 8;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allAcquired = new CountdownEvent(keyCount);

        async Task HoldKey(int n)
        {
            await using (await keyLock.LockAsync($"key-{n}"))
            {
                allAcquired.Signal();
                await gate.Task; // 持有锁直到统一放行
            }
        }

        var tasks = Enumerable.Range(0, keyCount).Select(HoldKey).ToArray();

        // 所有不同 key 应能在合理时间内全部同时获得锁
        Assert.True(allAcquired.Wait(TimeSpan.FromSeconds(5)),
            "Different keys must not block each other");

        gate.SetResult();
        await Task.WhenAll(tasks);
    }
}
