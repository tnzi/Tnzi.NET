namespace Tnzi.EFCore.Providers;

/// <summary>
/// PostgreSQL database provider configurator.
/// Automatically calls UseVector() when Pgvector.EntityFrameworkCore is loaded,
/// enabling EF Core to treat Vector as a scalar type rather than a POCO.
/// </summary>
public class PostgreSqlConfigurator : DatabaseProviderConfiguratorBase
{
    public override DatabaseProvider Provider => DatabaseProvider.PostgreSQL;

    protected override string AssemblyName => "Npgsql.EntityFrameworkCore.PostgreSQL";

    protected override string ExtensionMethodName => "UseNpgsql";

    /// <summary>
    /// 贡献 pgvector 的 <c>UseVector()</c> 作为额外配置；基类会把它与重试/超时选项合成到
    /// <c>Action&lt;NpgsqlDbContextOptionsBuilder&gt;</c> 一并传入 UseNpgsql。
    /// pgvector 未加载时返回 null（等价于旧的传 null 行为）。
    /// </summary>
    protected override Delegate? BuildExtraOptionsAction(Type optionsBuilderType)
        => TryBuildUseVectorAction(optionsBuilderType);

    /// <summary>
    /// Tries to build an Action&lt;NpgsqlDbContextOptionsBuilder&gt; that calls UseVector().
    /// Returns null if Pgvector.EntityFrameworkCore is not available (safe to pass as null action).
    /// Does NOT use the shared _assemblyCache to avoid caching null before the assembly is loaded.
    /// </summary>
    private static Delegate? TryBuildUseVectorAction(Type npgsqlOptionsBuilderType)
    {
        // Search loaded assemblies first (no cache — pgvector may not be loaded at startup)
        var pvAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Pgvector.EntityFrameworkCore");

        if (pvAssembly == null)
        {
            // Assembly is in the probing path but not yet JIT-loaded — force load
            try { pvAssembly = Assembly.Load(new AssemblyName("Pgvector.EntityFrameworkCore")); }
            catch { return null; }
        }

        if (pvAssembly == null)
            return null;

        Type[]? allTypes;
        try { allTypes = pvAssembly.GetExportedTypes(); }
        catch (ReflectionTypeLoadException ex)
        { allTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }

        foreach (var type in allTypes)
        {
            if (type == null || !type.IsClass || !type.IsSealed || type.IsGenericType)
                continue;

            var useVectorMethod = type.GetMethod(
                "UseVector",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { npgsqlOptionsBuilderType },
                null);

            if (useVectorMethod == null)
                continue;

            var actionType = typeof(Action<>).MakeGenericType(npgsqlOptionsBuilderType);
            try
            {
                return Delegate.CreateDelegate(actionType, null, useVectorMethod);
            }
            catch
            {
                // Continue searching other types
            }
        }

        return null;
    }
}
