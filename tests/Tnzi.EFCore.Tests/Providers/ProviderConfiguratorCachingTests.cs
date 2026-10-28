namespace Tnzi.EFCore.Tests.Providers;

using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Tnzi.EFCore.Providers;

/// <summary>
/// 配置器在每个作用域构建 DbContextOptions 时都会被调用（options 生命周期是 Scoped），
/// 所以只取决于程序集、方法签名与服务器版本的探测结果必须缓存，不能每个请求重做一遍。
/// </summary>
public class ProviderConfiguratorCachingTests
{
    [Fact]
    public void PostgreSql_WithoutPgvector_ProbesForItOnlyOnce()
    {
        var configurator = new ExposedPostgreSqlConfigurator();
        var builderType = typeof(ProviderConfiguratorCachingTests);

        // 预热：首次探测允许失败一次（测试项目不引用 pgvector）
        Assert.Null(configurator.Extra(builderType));

        var probeFailures = CountFirstChanceLoadFailures("Pgvector", () => configurator.Extra(builderType));

        Assert.Equal(0, probeFailures);
    }

    [Fact]
    public void MySql_DetectsTheServerVersionOncePerConnectionString()
    {
        var configurator = new ExposedMySqlConfigurator();
        var method = typeof(FakeMySqlOptionsExtensions).GetMethod(nameof(FakeMySqlOptionsExtensions.UseMySql))!;
        var first = $"Server=one;Database={Guid.NewGuid():N}";
        var second = $"Server=two;Database={Guid.NewGuid():N}";

        configurator.Invoke(method, new DbContextOptionsBuilder(), first);
        configurator.Invoke(method, new DbContextOptionsBuilder(), first);
        configurator.Invoke(method, new DbContextOptionsBuilder(), second);

        Assert.Equal(1, ServerVersion.DetectionsFor(first));
        Assert.Equal(1, ServerVersion.DetectionsFor(second));
        Assert.Equal(2, FakeMySqlOptionsExtensions.CallsFor(first));
    }

    [Fact]
    public void Configure_LooksUpTheExtensionMethodOnlyOnce_EvenWhenTheLookupIsOverridden()
    {
        var configurator = new CountingConfigurator();

        configurator.Configure(new DbContextOptionsBuilder(), "Data Source=one");
        configurator.Configure(new DbContextOptionsBuilder(), "Data Source=two");

        Assert.Equal(1, configurator.Lookups);
        Assert.Equal(2, FakeProviderOptionsExtensions.Calls);
    }

    private static int CountFirstChanceLoadFailures(string assemblyNamePart, Action action)
    {
        var threadId = Environment.CurrentManagedThreadId;
        var count = 0;

        void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == threadId
                && e.Exception is FileNotFoundException or FileLoadException
                && e.Exception.Message.Contains(assemblyNamePart, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref count);
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        try
        {
            action();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
        }

        return count;
    }

    private sealed class ExposedPostgreSqlConfigurator : PostgreSqlConfigurator
    {
        public Delegate? Extra(Type optionsBuilderType) => BuildExtraOptionsAction(optionsBuilderType);
    }

    private sealed class ExposedMySqlConfigurator : MySqlConfigurator
    {
        public void Invoke(MethodInfo method, DbContextOptionsBuilder builder, string connectionString)
            => InvokeExtensionMethod(method, builder, connectionString, null);
    }

    private sealed class CountingConfigurator : DatabaseProviderConfiguratorBase
    {
        private int _lookups;

        public int Lookups => _lookups;

        public override DatabaseProvider Provider => DatabaseProvider.Sqlite;

        protected override string AssemblyName => typeof(CountingConfigurator).Assembly.GetName().Name!;

        protected override string ExtensionMethodName => nameof(FakeProviderOptionsExtensions.UseCountingFakeProvider);

        protected override MethodInfo? FindExtensionMethod(Assembly assembly, string methodName)
        {
            Interlocked.Increment(ref _lookups);
            return typeof(FakeProviderOptionsExtensions).GetMethod(methodName);
        }
    }
}

/// <summary>按名字被 <see cref="MySqlConfigurator"/> 认出的 Pomelo 版本类型替身。</summary>
public sealed class ServerVersion
{
    private static readonly ConcurrentDictionary<string, int> Detections = new();

    public static ServerVersion AutoDetect(string connectionString)
    {
        Detections.AddOrUpdate(connectionString, 1, (_, n) => n + 1);
        return new ServerVersion();
    }

    public static int DetectionsFor(string connectionString)
        => Detections.TryGetValue(connectionString, out var n) ? n : 0;
}

public static class FakeMySqlOptionsExtensions
{
    private static readonly ConcurrentDictionary<string, int> Calls = new();

    public static DbContextOptionsBuilder UseMySql(this DbContextOptionsBuilder builder, string connectionString, ServerVersion serverVersion)
    {
        Calls.AddOrUpdate(connectionString, 1, (_, n) => n + 1);
        return builder;
    }

    public static int CallsFor(string connectionString)
        => Calls.TryGetValue(connectionString, out var n) ? n : 0;
}

public static class FakeProviderOptionsExtensions
{
    private static int _calls;

    public static int Calls => _calls;

    public static DbContextOptionsBuilder UseCountingFakeProvider(this DbContextOptionsBuilder builder, string connectionString)
    {
        Interlocked.Increment(ref _calls);
        return builder;
    }
}
