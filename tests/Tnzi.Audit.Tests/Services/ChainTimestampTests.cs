using System.Globalization;

namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// 哈希链时间字段的规整与格式化：写入时哈希的值与数据库读回来的值必须算出同一个哈希。
/// </summary>
public class ChainTimestampTests
{
    private static readonly DateTime Sample = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Normalize_TruncatesToWholeMicroseconds()
    {
        var value = Sample.AddTicks(1_234_567);

        var normalized = ChainTimestamp.Normalize(value);

        normalized.Ticks.ShouldBe(Sample.AddTicks(1_234_560).Ticks);
        normalized.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void Format_TreatsAnUnspecifiedValueReadBackFromTheDatabaseAsUtc()
    {
        // SQL Server / SQLite / MySQL 读回来的是 Unspecified。旧写法 ToUniversalTime() 把它当本地时间再平移一次：
        // 宿主机不在 UTC 时区时，整条链在校验时都对不上。
        var written = ChainTimestamp.Normalize(Sample.AddTicks(4_321_000));
        var readBack = DateTime.SpecifyKind(written, DateTimeKind.Unspecified);

        ChainTimestamp.Format(readBack).ShouldBe(ChainTimestamp.Format(written));
    }

    [Fact]
    public void Format_ConvertsATrueLocalValue()
    {
        var local = Sample.ToLocalTime();

        ChainTimestamp.Format(local).ShouldBe(ChainTimestamp.Format(Sample));
    }

    [Fact]
    public void Format_MatchesTheFormerHashTextForUtcValues()
    {
        // 兼容既有条目：保留了 100ns 精度的库上，旧条目按旧写法哈希，新写法对同一个 UTC 值必须给出逐字相同的文本。
        var legacy = Sample.AddTicks(1_234_567);

        ChainTimestamp.Format(legacy).ShouldBe(legacy.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }
}
