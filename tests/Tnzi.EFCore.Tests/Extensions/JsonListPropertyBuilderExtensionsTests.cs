using Tnzi.EFCore.Extensions;

namespace Tnzi.EFCore.Tests.Extensions;

/// <summary>
/// <see cref="JsonListPropertyBuilderExtensions.HasJsonListConversion{T}"/>：列表存成 JSON 文本，而且就地修改会落库。
/// </summary>
/// <remarks>
/// 只配 <c>HasConversion</c> 时 EF 按引用比较新旧值：<c>entity.Tags.Add("x")</c> 之后 <c>SaveChanges</c> 一个字节都不写，
/// 没有异常、没有日志（只有模型校验时的一条警告）。真用 SQLite 落库再读回，断言写进去的是不是改过的值。
/// </remarks>
public sealed class JsonListPropertyBuilderExtensionsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public JsonListPropertyBuilderExtensionsTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private JsonListProbeContext CreateContext()
        => new(new DbContextOptionsBuilder<JsonListProbeContext>().UseSqlite(_connection).Options);

    private int Insert(List<string>? tags)
    {
        using var context = CreateContext();
        var row = new JsonListProbe { Tags = tags };
        context.Add(row);
        context.SaveChanges();
        return row.Id;
    }

    private List<string>? Read(int id)
    {
        using var context = CreateContext();
        return context.Set<JsonListProbe>().AsNoTracking().Single(e => e.Id == id).Tags;
    }

    [Fact]
    public void AnElementAddedInPlace_IsSaved()
    {
        var id = Insert(["a"]);

        using (var context = CreateContext())
        {
            var row = context.Set<JsonListProbe>().Single(e => e.Id == id);
            row.Tags!.Add("b");

            Assert.True(context.Entry(row).Property(e => e.Tags).IsModified);
            context.SaveChanges();
        }

        Assert.Equal(["a", "b"], Read(id));
    }

    [Fact]
    public void AReorderedList_IsSaved()
    {
        var id = Insert(["a", "b"]);

        using (var context = CreateContext())
        {
            var row = context.Set<JsonListProbe>().Single(e => e.Id == id);
            row.Tags!.Reverse();
            context.SaveChanges();
        }

        Assert.Equal(["b", "a"], Read(id));
    }

    [Fact]
    public void AnUntouchedList_IsNotWrittenBack()
    {
        var id = Insert(["a"]);

        using var context = CreateContext();
        var row = context.Set<JsonListProbe>().Single(e => e.Id == id);
        context.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Unchanged, context.Entry(row).State);
    }

    [Fact]
    public void Null_AndEmpty_RoundTrip_AsThemselves()
    {
        Assert.Null(Read(Insert(null)));
        Assert.Equal([], Read(Insert([])));
    }
}

public sealed class JsonListProbe
{
    public int Id { get; set; }

    public List<string>? Tags { get; set; }
}

public sealed class JsonListProbeContext(DbContextOptions<JsonListProbeContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<JsonListProbe>().Property(e => e.Tags).HasJsonListConversion();
}
