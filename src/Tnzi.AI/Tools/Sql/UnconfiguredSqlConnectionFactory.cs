using System.Data.Common;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// 应用没有接入数据库连接工厂时，SQL 工具套件使用的回退实现：解析得出来，一调用就报错。
/// </summary>
/// <remarks>
/// <para>
/// SQL 工具套件是<b>可选能力</b>，但它的注册此前是<b>无条件</b>的：
/// <see cref="ReadOnlySqlExecutor"/> 的构造函数要一个 <c>Func&lt;string?, DbConnection&gt;</c>，
/// 而那个委托只能由应用提供。于是任何加载 <c>Tnzi.AI</c>、却<b>不打算</b>让助手跑 SQL 的应用，
/// 都会在 <c>ValidateOnBuild</c>（Development 下的 .NET 默认值）阶段启动失败。
/// </para>
/// <para>
/// <b>为什么这个形状值得单独修：</b>消费方让这个报错消失的最省事办法是
/// <c>ValidateOnBuild = false</c> —— 一个<b>宿主级</b>开关，连带压掉应用自己代码里的
/// 每一处 captive dependency 与漏注册。也就是说，「可选能力却强制注册」这个形状
/// 在<b>教消费方关掉作用域校验</b>。代价的方向反了：不用这个功能的人不该为它付启动成本。
/// </para>
/// <para>
/// <b>取舍：</b>回退把「启动即失败」换成了「调用才失败」。这对本套件是正确的取舍——
/// 框架内部没有任何代码消费 <see cref="IReadOnlySqlExecutor"/> 或 <see cref="ISchemaInspector"/>，
/// 只有应用显式注入才会用到，所以「调用」必然是应用的主动行为，不会悄悄发生在别处。
/// 想要这套工具的应用注册自己的工厂即可，行为一字不变；忘了注册的，拿到的是下面这条
/// 指名道姓的错误，而不是空引用。
/// </para>
/// <para>
/// 安全姿态不受影响：权限默认仍是 <see cref="DenyAllSqlPermissionCheck"/>（拒绝一切），
/// 本回退只关注「连接从哪来」，不参与「谁可以跑 SQL」。
/// </para>
/// </remarks>
public static class UnconfiguredSqlConnectionFactory
{
    /// <summary>回退抛出的异常所携带的配置键，便于日志与诊断聚合。</summary>
    public const string ConfigurationKey = "AI:Sql:ConnectionFactory";

    /// <summary>
    /// 始终抛出 <see cref="ConfigurationException"/>，说明缺的是什么、怎么补。
    /// </summary>
    /// <param name="connectionName">调用方请求的连接名（仅用于错误消息）。</param>
    public static DbConnection Create(string? connectionName)
    {
        var requested = string.IsNullOrWhiteSpace(connectionName)
            ? "the default connection"
            : $"connection '{connectionName}'";

        throw new ConfigurationException(
            ConfigurationKey,
            $"The AI SQL tool suite was asked for {requested}, but this application has not "
            + "registered a DbConnection factory. IReadOnlySqlExecutor and ISchemaInspector are "
            + "registered unconditionally so that applications which do not use them still start "
            + "cleanly; supplying the connection is the application's part of the contract. "
            + "Register one before using these services, for example: "
            + "services.AddScoped<Func<string?, DbConnection>>(sp => name => new NpgsqlConnection(...)). "
            + "See docs/modules/ai-tools.md for the full setup.");
    }
}
