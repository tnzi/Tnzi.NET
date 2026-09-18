using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tnzi.Identity.Data;

/// <summary>
/// SQLite 下 <see cref="DateTimeOffset"/> 列的文本映射：写入归一到 UTC，格式与 Microsoft.Data.Sqlite 逐字相同。
/// </summary>
/// <remarks>
/// 存在的理由见 <c>IdentityDbContext.ConfigureLockoutEndForSqlite</c>：SQLite 提供者不翻译该类型的大小比较，
/// 而同一偏移量的 ISO 文本字典序即时间序。格式里的 <c>FFFFFFF</c> 会截掉末尾的零，
/// 但 <c>+</c>（0x2B）与 <c>.</c>（0x2E）都排在数字之前，截断不破坏字典序。
/// </remarks>
internal sealed class SqliteUtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, string>
{
    /// <summary>Microsoft.Data.Sqlite 绑定 <see cref="DateTimeOffset"/> 参数时用的格式。</summary>
    internal const string Format = "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz";

    public static readonly SqliteUtcDateTimeOffsetConverter Instance = new();

    private SqliteUtcDateTimeOffsetConverter()
        : base(
            v => v.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
            v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
    {
    }
}
