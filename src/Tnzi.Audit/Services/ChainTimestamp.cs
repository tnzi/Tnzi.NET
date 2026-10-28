namespace Tnzi.Audit.Services;

/// <summary>
/// 哈希链里的时间字段：写入前规整到每个受支持的数据库都存得住的精度，算哈希时按 UTC 解释。
/// </summary>
/// <remarks>
/// <para>
/// ★ 写入时算的哈希与校验时重算的哈希必须吃进<b>同一个</b>时间值，而校验用的那个值经过了一次数据库往返：
/// </para>
/// <list type="number">
/// <item><b>精度。</b><see cref="DateTime"/> 是 100ns，PostgreSQL / MySQL 只存到微秒。写入时哈希的是
/// <c>.1234567Z</c>，读回来是 <c>.1234560Z</c>：十条里九条校验报「已被篡改」。所以时间值先截到整微秒，
/// 再赋给实体、再算哈希：存进去的就是哈希过的那一个。</item>
/// <item><b>种类。</b>SQL Server / SQLite / MySQL 读回来的 <see cref="DateTime.Kind"/> 是 <c>Unspecified</c>，
/// <c>ToUniversalTime()</c> 会把它当本地时间再平移一次：宿主机不在 UTC 时区时整条链全部报「已被篡改」。
/// 框架只写 UTC，所以 <c>Unspecified</c> 一律按 UTC 解释，只有真正的 <c>Local</c> 才换算。</item>
/// </list>
/// <para>
/// 误报的防篡改链比没有链更糟：真篡改与误报从此无法区分。
/// </para>
/// <para>
/// 兼容：<see cref="Format"/> 对一个 UTC 值的输出与旧写法 <c>ToUniversalTime().ToString("O")</c> 逐字相同，
/// 所以在保留了 100ns 精度的库上（SQL Server <c>datetime2</c>、SQLite）既有条目照常通过校验，
/// 宿主机不在 UTC 时区的 SQL Server 上原先全部误报的条目也从此通过。微秒精度的库上旧条目的哈希
/// 写入时就吃进了库里没有的那一位，它们无法再验证，见模块文档。
/// </para>
/// </remarks>
internal static class ChainTimestamp
{
    /// <summary>当前 UTC 时间，截到整微秒。哈希链的新条目一律用它取时间。</summary>
    public static DateTime UtcNow() => Normalize(DateTime.UtcNow);

    /// <summary>按 UTC 解释并截到整微秒。写入哈希链的时间值先过这一步再赋给实体。</summary>
    public static DateTime Normalize(DateTime value)
    {
        var utc = AsUtc(value);
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    /// <summary>哈希载荷里的时间文本：按 UTC 解释，往返格式（"O"）+ 不变文化（换个区域设置不会算出另一个哈希）。</summary>
    public static string Format(DateTime value) => AsUtc(value).ToString("O", CultureInfo.InvariantCulture);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
