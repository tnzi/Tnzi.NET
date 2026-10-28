using System.Data.SqlTypes;

namespace Tnzi.Tests.Utilities;

/// <summary>
/// SequentialGuid 单元测试
/// </summary>
public class SequentialGuidTests
{
    #region 基本功能测试

    [Fact]
    public void NewGuid_Default_ShouldGenerateValidGuid()
    {
        // Act
        var guid = SequentialGuid.NewGuid();

        // Assert
        Assert.NotEqual(Guid.Empty, guid);
        Assert.True(guid != default(Guid));
    }

    [Fact]
    public void NewGuid_SequentialAtEnd_ShouldGenerateValidGuid()
    {
        // Act
        var guid = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAtEnd);

        // Assert
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void NewGuid_SequentialAsString_ShouldGenerateValidGuid()
    {
        // Act
        var guid = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAsString);

        // Assert
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void NewGuid_SequentialAsBinary_ShouldGenerateValidGuid()
    {
        // Act
        var guid = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAsBinary);

        // Assert
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void NewGuid_MultipleCalls_ShouldGenerateUniqueGuids()
    {
        // Arrange
        const int count = 100;
        var guids = new HashSet<Guid>();

        // Act
        for (int i = 0; i < count; i++)
        {
            guids.Add(SequentialGuid.NewGuid());
        }

        // Assert
        Assert.Equal(count, guids.Count); // 所有 GUID 应该唯一
    }

    #endregion

    #region 有序性测试（按目标数据库的真实比较规则）

    // 只比较 ToByteArray 的某一段证明不了任何事：数据库比较的是整个值，而且各库的比较规则不同。
    // 下面每条都按目标库实际的比较方式断言整段序列严格递增。

    [Fact]
    public void NewGuid_SequentialAsString_IsOrderedAsText()
    {
        // MySQL char(36) / SQLite TEXT 存的是 ToString() 的文本
        var guids = GenerateSequence(SequentialGuid.SequentialGuidType.SequentialAsString);

        AssertStrictlyIncreasing(guids, (a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));
        AssertStrictlyIncreasing(guids, (a, b) => string.CompareOrdinal(a.ToString().ToUpperInvariant(), b.ToString().ToUpperInvariant()));
    }

    [Fact]
    public void NewGuid_SequentialAsString_IsOrderedAsBigEndianBytes()
    {
        // PostgreSQL uuid 按 RFC 9562 的大端字节序逐字节比较
        var guids = GenerateSequence(SequentialGuid.SequentialGuidType.SequentialAsString);

        AssertStrictlyIncreasing(guids, (a, b) => a.ToByteArray(bigEndian: true).AsSpan().SequenceCompareTo(b.ToByteArray(bigEndian: true)));
    }

    [Fact]
    public void NewGuid_SequentialAtEnd_IsOrderedAsSqlServerUniqueIdentifier()
    {
        // SqlGuid.CompareTo 就是 SQL Server uniqueidentifier 的比较规则
        var guids = GenerateSequence(SequentialGuid.SequentialGuidType.SequentialAtEnd);

        AssertStrictlyIncreasing(guids, CompareAsSqlServer);
    }

    [Fact]
    public void NewGuid_SequentialAsBinary_IsOrderedAsDotNetByteOrder()
    {
        var guids = GenerateSequence(SequentialGuid.SequentialGuidType.SequentialAsBinary);

        AssertStrictlyIncreasing(guids, CompareAsDotNetBytes);
    }

    public static TheoryData<SequentialGuid.SequentialGuidType> AllLayouts =>
    [
        SequentialGuid.SequentialGuidType.SequentialAsString,
        SequentialGuid.SequentialGuidType.SequentialAsBinary,
        SequentialGuid.SequentialGuidType.SequentialAtEnd,
    ];

    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void Layout_LaterTimestampSortsLater_AcrossEveryByteBoundary(SequentialGuid.SequentialGuidType type)
    {
        // 2^24 与 2^32 毫秒是旧排列的两处回绕点（约 4.66 小时、约 49.7 天）；随机字节故意取最不利的值
        (long Earlier, long Later)[] boundaries =
        [
            (0xFFL, 0x100L),
            (0xFFFFL, 0x1_0000L),
            (0xFF_FFFFL, 0x100_0000L),
            (0xFFFF_FFFFL, 0x1_0000_0000L),
            (0xFF_FFFF_FFFFL, 0x100_0000_0000L),
            (0xFFFF_FFFF_FFFEL, 0xFFFF_FFFF_FFFFL),
        ];

        foreach (var (earlier, later) in boundaries)
        {
            var a = Compose(type, earlier, counter: 0xFFF, randomFill: 0xFF);
            var b = Compose(type, later, counter: 0, randomFill: 0x00);

            Assert.True(CompareFor(type)(a, b) < 0, $"{type}: timestamp {earlier:X} should sort before {later:X}");
        }
    }

    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void Layout_CounterOrdersIdsWithinOneMillisecond(SequentialGuid.SequentialGuidType type)
    {
        const long timestamp = 0x0199_0000_0000L;
        (int Earlier, int Later)[] counters = [(0, 1), (0x0F, 0x10), (0xFF, 0x100), (0xFFE, 0xFFF)];

        foreach (var (earlier, later) in counters)
        {
            var a = Compose(type, timestamp, earlier, randomFill: 0xFF);
            var b = Compose(type, timestamp, later, randomFill: 0x00);

            Assert.True(CompareFor(type)(a, b) < 0, $"{type}: counter {earlier:X} should sort before {later:X}");
        }
    }

    [Fact]
    public void NewGuid_SequentialAsString_CarriesTheUnixMillisecondTimestampOfUuidV7()
    {
        // 标准 UUIDv7：前 12 个十六进制字符是 Unix 毫秒时间戳，任何 UUIDv7 解析器都读得出生成时刻
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAsString);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var timestamp = Convert.ToInt64(guid.ToString("N")[..12], 16);

        Assert.InRange(timestamp, before, after);
    }

    [Fact]
    public void GetDateFrom_ReturnsTheGenerationTimeOfTheDefaultLayout()
    {
        var before = DateTime.UtcNow.AddMilliseconds(-1);
        var guid = SequentialGuid.NewGuid();
        var after = DateTime.UtcNow.AddMilliseconds(1);

        var date = SequentialGuid.GetDateFrom(guid);

        Assert.Equal(DateTimeKind.Utc, date.Kind);
        Assert.InRange(date, before, after);
    }

    [Fact]
    public void NewGuid_SameMillisecond_ShouldIncrementCounter()
    {
        // Arrange
        var guids = new List<Guid>();
        var startTime = DateTime.UtcNow;

        // Act
        // 在同一毫秒内快速生成多个 GUID
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < 1)
        {
            guids.Add(SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAtEnd));
        }

        // Assert
        // 至少应该生成一个 GUID
        Assert.True(guids.Count > 0);

        // 如果生成了多个，它们应该不同（通过计数器区分）
        if (guids.Count > 1)
        {
            var uniqueGuids = guids.Distinct().ToList();
            Assert.Equal(guids.Count, uniqueGuids.Count);
        }
    }

    #endregion

    #region 并发安全性测试

    [Fact]
    public async Task NewGuid_ConcurrentCalls_ShouldGenerateUniqueGuids()
    {
        // Arrange
        const int threadCount = 10;
        const int guidsPerThread = 100;
        var allGuids = new ConcurrentBag<Guid>();
        var exceptions = new ConcurrentBag<Exception>();

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(threadId => Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < guidsPerThread; i++)
                    {
                        allGuids.Add(SequentialGuid.NewGuid());
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        Assert.Empty(exceptions); // 不应该有异常
        Assert.Equal(threadCount * guidsPerThread, allGuids.Count); // 应该生成所有 GUID
        Assert.Equal(threadCount * guidsPerThread, allGuids.Distinct().Count()); // 所有 GUID 应该唯一
    }

    [Fact]
    public async Task NewGuid_HighConcurrency_ShouldNotDeadlock()
    {
        // Arrange
        const int threadCount = 50;
        const int guidsPerThread = 20;
        var allGuids = new ConcurrentBag<Guid>();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < guidsPerThread; i++)
                {
                    allGuids.Add(SequentialGuid.NewGuid());
                }
            }))
            .ToArray();

        var completed = await Task.WhenAll(tasks).ContinueWith(_ => true);

        // Assert
        Assert.True(completed, "所有任务应该在10秒内完成（无死锁）");
        Assert.Equal(threadCount * guidsPerThread, allGuids.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 10000, "应该在合理时间内完成");
    }

    [Fact]
    public void NewGuid_ConcurrentDifferentTypes_ShouldGenerateUniqueGuids()
    {
        // Arrange
        const int count = 100;
        var allGuids = new ConcurrentBag<Guid>();
        var types = new[]
        {
            SequentialGuid.SequentialGuidType.SequentialAtEnd,
            SequentialGuid.SequentialGuidType.SequentialAsString,
            SequentialGuid.SequentialGuidType.SequentialAsBinary
        };

        // Act
        Parallel.ForEach(Enumerable.Range(0, count), i =>
        {
            var type = types[i % types.Length];
            allGuids.Add(SequentialGuid.NewGuid(type));
        });

        // Assert
        Assert.Equal(count, allGuids.Count);
        Assert.Equal(count, allGuids.Distinct().Count());
    }

    #endregion

    #region GUID 格式验证

    [Fact]
    public void NewGuid_SequentialAtEnd_IsAnRfc9562Version8Uuid()
    {
        var guid = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAtEnd);

        Assert.Equal(8, guid.Version);
        Assert.Equal(0x80, guid.ToByteArray()[8] & 0xC0);
    }

    [Fact]
    public void NewGuid_SequentialAsBinary_IsAnRfc9562Version8Uuid()
    {
        var guid = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAsBinary);

        Assert.Equal(8, guid.Version);
        Assert.Equal(0x80, guid.ToByteArray()[8] & 0xC0);
    }

    [Fact]
    public void NewGuid_SequentialAsString_IsAnRfc9562Version7Uuid()
    {
        var guid = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAsString);

        Assert.Equal(7, guid.Version);
        Assert.Equal(0x80, guid.ToByteArray()[8] & 0xC0);
        Assert.Equal('7', guid.ToString()[14]);
    }

    #endregion

    #region 性能测试

    [Fact]
    public void NewGuid_Performance_ShouldGenerateQuickly()
    {
        // Arrange
        const int count = 10000;
        var stopwatch = Stopwatch.StartNew();

        // Act
        for (int i = 0; i < count; i++)
        {
            SequentialGuid.NewGuid();
        }

        stopwatch.Stop();

        // Assert
        var guidsPerSecond = count / stopwatch.Elapsed.TotalSeconds;
        Assert.True(guidsPerSecond > 100000, $"应该能够每秒生成超过 100,000 个 GUID，实际: {guidsPerSecond:F0}");
    }

    [Fact]
    public void NewGuid_ConcurrentPerformance_ShouldMaintainThroughput()
    {
        // Arrange
        const int threadCount = 10;
        const int guidsPerThread = 1000;
        var stopwatch = Stopwatch.StartNew();

        // Act
        Parallel.ForEach(Enumerable.Range(0, threadCount * guidsPerThread), _ =>
        {
            SequentialGuid.NewGuid();
        });

        stopwatch.Stop();

        // Assert
        var totalGuids = threadCount * guidsPerThread;
        var guidsPerSecond = totalGuids / stopwatch.Elapsed.TotalSeconds;
        Assert.True(guidsPerSecond > 50000, $"并发场景下应该能够每秒生成超过 50,000 个 GUID，实际: {guidsPerSecond:F0}");
    }

    #endregion

    #region 边界情况测试

    [Fact]
    public void NewGuid_RapidGeneration_ShouldHandleCounterOverflow()
    {
        // Arrange
        // 这个测试很难直接验证计数器溢出（需要在同一毫秒内生成超过 65535 个 GUID）
        // 但我们可以验证快速生成时 GUID 仍然唯一
        const int count = 1000;
        var guids = new ConcurrentBag<Guid>();

        // Act
        Parallel.For(0, count, _ =>
        {
            guids.Add(SequentialGuid.NewGuid());
        });

        // Assert
        Assert.Equal(count, guids.Distinct().Count());
    }

    [Fact]
    public void NewGuid_AllTypes_ShouldGenerateDifferentGuids()
    {
        // Act
        var guid1 = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAtEnd);
        var guid2 = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAsString);
        var guid3 = SequentialGuid.NewGuid(SequentialGuid.SequentialGuidType.SequentialAsBinary);

        // Assert
        Assert.NotEqual(guid1, guid2);
        Assert.NotEqual(guid1, guid3);
        Assert.NotEqual(guid2, guid3);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 连续生成一段 Guid：大部分落在同一毫秒内（走计数器），中间穿插几次跨毫秒
    /// </summary>
    private static List<Guid> GenerateSequence(SequentialGuid.SequentialGuidType type)
    {
        var guids = new List<Guid>();
        for (int i = 0; i < 600; i++)
        {
            guids.Add(SequentialGuid.NewGuid(type));
            if (i % 100 == 99)
                Thread.Sleep(2);
        }
        return guids;
    }

    private static void AssertStrictlyIncreasing(List<Guid> guids, Func<Guid, Guid, int> compare)
    {
        for (int i = 1; i < guids.Count; i++)
        {
            Assert.True(compare(guids[i - 1], guids[i]) < 0, $"GUID at index {i} ({guids[i]}) should sort after index {i - 1} ({guids[i - 1]})");
        }
    }

    private static Func<Guid, Guid, int> CompareFor(SequentialGuid.SequentialGuidType type) => type switch
    {
        SequentialGuid.SequentialGuidType.SequentialAsString => (a, b) => string.CompareOrdinal(a.ToString(), b.ToString()),
        SequentialGuid.SequentialGuidType.SequentialAsBinary => CompareAsDotNetBytes,
        _ => CompareAsSqlServer,
    };

    private static int CompareAsSqlServer(Guid a, Guid b) => new SqlGuid(a).CompareTo(new SqlGuid(b));

    private static int CompareAsDotNetBytes(Guid a, Guid b) => a.ToByteArray().AsSpan().SequenceCompareTo(b.ToByteArray());

    /// <summary>
    /// 直接驱动排列函数：时钟给不出跨越 2^24 / 2^32 毫秒的相邻时间戳
    /// </summary>
    private static Guid Compose(SequentialGuid.SequentialGuidType type, long timestamp, int counter, byte randomFill)
    {
        var method = typeof(SequentialGuid).GetMethod("Compose", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SequentialGuid.Compose(type, timestamp, counter, random) was not found; the layout tests drive it directly.");
        return (Guid)method.Invoke(null, [type, timestamp, counter, Enumerable.Repeat(randomFill, 16).ToArray()])!;
    }

    #endregion
}
