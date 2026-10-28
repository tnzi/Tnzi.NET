namespace Tnzi.EFCore.Tests;

/// <summary>
/// 框架生成的 Guid 主键在<b>目标库自己的比较规则</b>下按生成顺序排列。
/// </summary>
/// <remarks>
/// 顺序 Guid 存在的唯一理由是让聚集索引的插入落在末尾。排列与库的比较规则对不上时，
/// 它在库里的表现与 <see cref="Guid.NewGuid"/> 无异（每次插入随机落页、页分裂），而唯一性、读写结果都正常，没有任何症状。
/// </remarks>
public class SequentialGuidProviderLayoutTests : EFCoreTestBase
{
    [Fact]
    public async Task SqliteKeys_SortInGenerationOrder_WhenTheDatabaseOrdersThem()
    {
        var names = new List<string>();
        for (var i = 0; i < 120; i++)
        {
            var name = $"row-{i:D3}";
            DbContext.TestEntitiesWithGuidId.Add(new TestEntityWithGuidId { Name = name });
            await DbContext.SaveChangesAsync();
            names.Add(name);
            if (i % 40 == 39)
                Thread.Sleep(2);
        }

        DbContext.ChangeTracker.Clear();

        // ORDER BY 在 SQLite 里执行：Guid 列存成 TEXT，按文本比较
        var orderedByKey = await DbContext.TestEntitiesWithGuidId.OrderBy(e => e.Id).Select(e => e.Name).ToListAsync();

        Assert.Equal(names, orderedByKey);
    }

    [Fact]
    public void DefaultEntityIdGenerator_OnSqlite_IssuesTextOrderedKeys()
    {
        var generator = new DefaultEntityIdGenerator(DbContext);

        var keys = Enumerable.Range(0, 200).Select(_ => (Guid)generator.GenerateId(typeof(TestEntityWithGuidId), typeof(Guid))!).ToList();

        for (var i = 1; i < keys.Count; i++)
        {
            Assert.True(string.CompareOrdinal(keys[i - 1].ToString(), keys[i].ToString()) < 0, $"key {i} should sort after key {i - 1}");
        }
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", SequentialGuid.SequentialGuidType.SequentialAtEnd)]
    // Npgsql 按 RFC 大端字节序写 uuid，PostgreSQL 按同一顺序比较，与 ToString 的文本顺序相同
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", SequentialGuid.SequentialGuidType.SequentialAsString)]
    [InlineData("Pomelo.EntityFrameworkCore.MySql", SequentialGuid.SequentialGuidType.SequentialAsString)]
    [InlineData("MySql.EntityFrameworkCore", SequentialGuid.SequentialGuidType.SequentialAsString)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", SequentialGuid.SequentialGuidType.SequentialAsString)]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory", SequentialGuid.SequentialGuidType.SequentialAtEnd)]
    [InlineData(null, SequentialGuid.SequentialGuidType.SequentialAtEnd)]
    public void ProviderMapsToTheLayoutItsDatabaseCompares(string? providerName, SequentialGuid.SequentialGuidType expected)
    {
        var method = typeof(TnziDbContext<>).Assembly
            .GetType("Tnzi.EFCore.Internal.IdGenerationHelper", throwOnError: true)!
            .GetMethod("GetSequentialGuidType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("IdGenerationHelper.GetSequentialGuidType(string?) was not found.");

        Assert.Equal(expected, (SequentialGuid.SequentialGuidType)method.Invoke(null, [providerName])!);
    }
}
