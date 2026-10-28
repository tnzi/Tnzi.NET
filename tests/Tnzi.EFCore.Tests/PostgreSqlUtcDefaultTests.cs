using Tnzi.EFCore.Sql;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// PostgreSQL 的 <c>HasDefaultCurrentUtcTime()</c> / <c>HasDefaultCurrentUtcDate()</c> 在任何会话时区下都给出真实的 UTC 值。
/// </summary>
/// <remarks>
/// <para>
/// ★ Npgsql 把 <see cref="DateTime"/> 映射成 <c>timestamp with time zone</c>。此前的 <c>(NOW() AT TIME ZONE 'UTC')</c>
/// 产出不带时区的 UTC 墙钟，赋给 timestamptz 列时 PostgreSQL 按会话 <c>TimeZone</c> 把它当本地时间再换算一次：
/// 服务器时区是 Asia/Shanghai 时默认值早 8 小时，America/Toronto 时晚 4 到 5 小时。
/// <c>(CURRENT_DATE AT TIME ZONE 'UTC')::DATE</c> 从会话本地日期出发，每天有一段时间差一天。
/// </para>
/// <para>
/// CI 里没有 PostgreSQL，断言落在生成的 SQL 上。
/// </para>
/// </remarks>
public class PostgreSqlUtcDefaultTests
{
    [Fact]
    public void CurrentUtcTime_IsAnAbsoluteInstant()
    {
        Assert.Equal("NOW()", SqlFunctionFactory.CurrentUtcTime(DatabaseProvider.PostgreSQL));
    }

    [Fact]
    public void CurrentUtcDate_TakesTheDateOfTheUtcWallClock()
    {
        Assert.Equal("(NOW() AT TIME ZONE 'UTC')::DATE", SqlFunctionFactory.CurrentUtcDate(DatabaseProvider.PostgreSQL));
    }
}
