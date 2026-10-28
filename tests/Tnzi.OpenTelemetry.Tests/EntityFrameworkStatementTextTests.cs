using System.Data;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.EntityFrameworkCore;
using OpenTelemetry.Trace;
using Tnzi.OpenTelemetry.Options;

namespace Tnzi.OpenTelemetry.Tests;

/// <summary>
/// EF Core span 上的 SQL 文本跟着 <see cref="OpenTelemetryOptions.RecordDbStatementText"/> 走，对真实的 EF 命令断言。
/// </summary>
/// <remarks>
/// 检测库早已删掉关掉 SQL 文本的开关，每条文本命令都会写 <c>db.query.text</c>（或旧约定的 <c>db.statement</c>），
/// 只对认得出的 SQL 方言做字面量脱敏。只断言「选项上设了什么」的测试在这种升级面前恒绿，所以这里起一个真的
/// TracerProvider、跑一条带字面量的 SQLite 命令，看导出前的 span 上到底留下了什么。
/// </remarks>
public class EntityFrameworkStatementTextTests
{
    private const string SecretLiteral = "card-4111-1111";
    private const string Command = "SELECT '" + SecretLiteral + "'";

    [Fact]
    public async Task RecordingOff_NoSqlTextLeavesTheProcess()
    {
        var span = await CaptureCommandSpanAsync(record: false);

        foreach (var tag in OpenTelemetryModule.DbStatementTagNames)
        {
            span.GetTagItem(tag).ShouldBeNull($"'{tag}' carried SQL text with RecordDbStatementText=false");
        }
    }

    [Fact]
    public async Task RecordingOn_TheSpanCarriesTheCommandVerbatim()
    {
        var span = await CaptureCommandSpanAsync(record: true);

        var text = OpenTelemetryModule.DbStatementTagNames
            .Select(tag => span.GetTagItem(tag) as string)
            .FirstOrDefault(value => value != null);
        text.ShouldNotBeNull("no SQL text attribute with RecordDbStatementText=true");
        // 排障要能照着复现：不是字面量被换成 ? 的脱敏版本
        text.ShouldContain(SecretLiteral);
    }

    /// <summary>存储过程命令的这个属性写的是过程名，不带数据：开关只管文本命令，过程名照旧保留。</summary>
    [Fact]
    public void RecordingOff_KeepsAStoredProcedureName()
    {
        using var activity = new Activity("db").Start();
        activity.SetTag("db.statement", "dbo.GetOrders");
        var command = new Mock<IDbCommand>();
        command.SetupGet(c => c.CommandType).Returns(CommandType.StoredProcedure);
        command.SetupGet(c => c.CommandText).Returns("dbo.GetOrders");

        OpenTelemetryModule.ApplyDbStatementPolicy(activity, command.Object, record: false);

        activity.GetTagItem("db.statement").ShouldBe("dbo.GetOrders");
    }

    /// <summary>选项上只有一个增强回调：已经配上的那个照跑，SQL 文本策略在它之后收尾。</summary>
    [Fact]
    public void AnEnrichCallbackConfiguredEarlier_StillRuns_AndThePolicyRunsLast()
    {
        var options = new EntityFrameworkInstrumentationOptions
        {
            EnrichWithIDbCommand = (activity, _) =>
            {
                activity.SetTag("app.tenant", "t1");
                activity.SetTag("db.statement", "SELECT 'leaked by the app callback'");
            }
        };
        OpenTelemetryModule.ApplyEntityFrameworkOptions(options, new OpenTelemetryOptions { RecordDbStatementText = false });
        using var activity = new Activity("db").Start();
        var command = new Mock<IDbCommand>();
        command.SetupGet(c => c.CommandType).Returns(CommandType.Text);

        options.EnrichWithIDbCommand.ShouldNotBeNull().Invoke(activity, command.Object);

        activity.GetTagItem("app.tenant").ShouldBe("t1");
        activity.GetTagItem("db.statement").ShouldBeNull();
    }

    private static async Task<Activity> CaptureCommandSpanAsync(bool record)
    {
        // 检测库订阅的是进程级的 EF 诊断事件：只认挂在本测试父 span 下面的那条，不被并行跑的别的测试干扰
        using var source = new ActivitySource($"{nameof(EntityFrameworkStatementTextTests)}.{Guid.NewGuid():N}");
        var captured = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(source.Name)
            .AddEntityFrameworkCoreInstrumentation(options =>
                OpenTelemetryModule.ApplyEntityFrameworkOptions(options, new OpenTelemetryOptions { RecordDbStatementText = record }))
            .AddProcessor(new CapturingProcessor(captured))
            .Build();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        ActivitySpanId parentSpanId;
        using (var parent = source.StartActivity("probe").ShouldNotBeNull())
        {
            parentSpanId = parent.SpanId;
            await using var context = new ProbeContext(new DbContextOptionsBuilder<ProbeContext>().UseSqlite(connection).Options);
            await context.Database.ExecuteSqlRawAsync(Command);
        }

        tracerProvider.ForceFlush();
        lock (captured)
        {
            return captured.Where(a => a.ParentSpanId == parentSpanId).ShouldHaveSingleItem();
        }
    }

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options);

    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            lock (sink)
            {
                sink.Add(data);
            }
        }
    }
}
