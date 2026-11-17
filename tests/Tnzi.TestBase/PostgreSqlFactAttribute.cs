using Xunit;

namespace Tnzi.TestBase;

/// <summary>
/// 只在环境变量 <see cref="ConnectionStringVariable"/> 给出 PostgreSQL 连接串时运行的测试。
/// </summary>
/// <remarks>
/// <para>
/// 连接串指向一个可以建库、删库的服务器（例如 <c>Host=127.0.0.1;Port=5432;Username=postgres</c>）；
/// 用例在上面建自己的库（<see cref="NewDatabase"/>），跑完删掉。
/// </para>
/// <para>
/// xUnit 2.x 没有运行期跳过，在发现阶段按环境变量决定：没设时用例根本不开始执行。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    /// <summary>给出 PostgreSQL 连接串的环境变量。</summary>
    public const string ConnectionStringVariable = "TNZI_TEST_POSTGRES";

    /// <summary>环境变量里的连接串；没有时为 null。</summary>
    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = $"Set {ConnectionStringVariable} to a PostgreSQL connection string to run this test.";
        }
    }

    /// <summary>
    /// 环境变量里的连接串，库名换成一个以 <paramref name="prefix"/> 开头的新名字（小写，PostgreSQL 不加引号时按小写处理）。
    /// </summary>
    public static string NewDatabase(string prefix)
    {
        var connectionString = ConnectionString
            ?? throw new InvalidOperationException($"{ConnectionStringVariable} is not set.");
        var database = $"{prefix}_{Guid.NewGuid():N}".ToLowerInvariant();
        return $"{connectionString.TrimEnd(';')};Database={database}";
    }
}
