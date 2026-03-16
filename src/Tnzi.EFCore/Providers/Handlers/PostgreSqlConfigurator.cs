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

    protected override void InvokeExtensionMethod(MethodInfo method, DbContextOptionsBuilder builder, string connectionString)
    {
        var parameters = method.GetParameters();

        if (parameters.Length == 3 &&
            parameters[2].ParameterType.IsGenericType &&
            parameters[2].ParameterType.GetGenericTypeDefinition() == typeof(Action<>))
        {
            var npgsqlOptionsType = parameters[2].ParameterType.GetGenericArguments()[0];
            var vectorAction = TryBuildUseVectorAction(npgsqlOptionsType);
            method.Invoke(null, new object?[] { builder, connectionString, vectorAction });
        }
        else
        {
            base.InvokeExtensionMethod(method, builder, connectionString);
        }
    }

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
