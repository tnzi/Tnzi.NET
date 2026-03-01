
namespace Tnzi.EFCore.Services;

/// <summary>
/// Repository 注册器实现
/// </summary>
public class RepositoryRegistrar : IRepositoryRegistrar
{
    /// <summary>
    /// 基于 DbContext 的 DbSet 属性注册 Repository
    /// </summary>
    public void RegisterRepositoriesFromDbContext(
        IServiceCollection services,
        Type dbContextType,
        ILogger? logger = null)
    {
        Check.NotNull(services);
        Check.NotNull(dbContextType);

        var dbSetProperties = dbContextType.GetProperties()
            .Where(p => p.PropertyType.IsGenericType &&
                       (p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>) ||
                        p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("DbSet", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var property in dbSetProperties)
        {
            var entityType = property.PropertyType.GetGenericArguments().FirstOrDefault();
            if (entityType == null) continue;

            // 只注册有效的实体类型
            if (!TypeHelper.IsEntityType(entityType)) continue;

            RegisterRepository(services, entityType, dbContextType, logger);
        }
    }

    /// <summary>
    /// 基于 IEntityRegister 注册 Repository（用于没有 DbSet 的实体）
    /// </summary>
    public void RegisterRepositoriesFromEntityRegisters(
        IServiceCollection services,
        Type dbContextType,
        bool isPrimary = false,
        ILogger? logger = null)
    {
        Check.NotNull(services);
        Check.NotNull(dbContextType);

        // 关键：加载入口程序集及其所有引用的程序集
        // 这确保了应用程序的实体配置类能被发现（特别是在配置阶段调用时）
        AssemblyScanner.LoadEntryAssemblyReferences(logger);

        // 使用 AssemblyScanner 发现所有的 IEntityRegister 实现
        var entityRegisterTypes = AssemblyScanner.FindTypesImplementing<IEntityRegister>(null, logger);

        foreach (var registerType in entityRegisterTypes)
        {
            try
            {
                var register = Activator.CreateInstance(registerType) as IEntityRegister;
                if (register == null) continue;

                var entityType = register.EntityType;
                var registerDbContextType = register.DbContextType;

                // 处理逻辑：
                // 1. 如果实体指定了 DbContextType，必须精确匹配当前 DbContext
                // 2. 如果实体的 DbContextType 为 null（表示主 DbContext），只有当前 DbContext 是主 DbContext 时才注册
                if (registerDbContextType != null)
                {
                    // 实体指定了 DbContextType，必须精确匹配
                    if (registerDbContextType != dbContextType)
                    {
                        continue;
                    }
                }
                else
                {
                    // 实体未指定 DbContextType，表示应该注册到主 DbContext
                    // 只有当前 DbContext 是主 DbContext 时才注册
                    if (!isPrimary)
                    {
                        continue;
                    }
                }

                RegisterRepository(services, entityType, dbContextType, logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to register repository from entity register {RegisterType}", registerType.FullName);
            }
        }
    }

    /// <summary>
    /// 为实体注册 Repository
    /// </summary>
    public void RegisterRepository(
        IServiceCollection services,
        Type entityType,
        Type dbContextType,
        ILogger? logger = null)
    {
        // 验证实体类型
        if (!TypeHelper.IsEntityType(entityType))
        {
            return;
        }

        try
        {
            // 查找主键类型
            var keyType = TypeHelper.GetPrimaryKeyType(entityType);

            var repoImplType = keyType != null
                ? typeof(EFCoreRepository<,,>).MakeGenericType(dbContextType, entityType, keyType)
                : typeof(EFCoreRepository<,>).MakeGenericType(dbContextType, entityType);

            // 1. 注册 IRepository<TEntity>
            var repoInterfaceType = typeof(IRepository<>).MakeGenericType(entityType);
            if (!services.Any(s => s.ServiceType == repoInterfaceType))
            {
                services.AddScoped(repoInterfaceType, repoImplType);
                logger?.LogDebug("Registered Repository: {Interface} -> {Implementation}",
                    repoInterfaceType.Name, repoImplType.Name);
            }

            // 2. 注册 IReadOnlyRepository<TEntity>（使用同一实现，CQRS 查询端可注入此接口）
            var readOnlyInterfaceType = typeof(IReadOnlyRepository<>).MakeGenericType(entityType);
            if (!services.Any(s => s.ServiceType == readOnlyInterfaceType))
            {
                services.AddScoped(readOnlyInterfaceType, repoImplType);
            }

            // 3. 注册 IRepository<TEntity, TKey> 和 IReadOnlyRepository<TEntity, TKey>
            if (keyType != null)
            {
                var repoKeyInterfaceType = typeof(IRepository<,>).MakeGenericType(entityType, keyType);
                var repoKeyImplType = typeof(EFCoreRepository<,,>).MakeGenericType(dbContextType, entityType, keyType);

                if (!services.Any(s => s.ServiceType == repoKeyInterfaceType))
                {
                    services.AddScoped(repoKeyInterfaceType, repoKeyImplType);
                    logger?.LogDebug("Registered Repository: {Interface} -> {Implementation}",
                        repoKeyInterfaceType.Name, repoKeyImplType.Name);
                }

                var readOnlyKeyInterfaceType = typeof(IReadOnlyRepository<,>).MakeGenericType(entityType, keyType);
                if (!services.Any(s => s.ServiceType == readOnlyKeyInterfaceType))
                {
                    services.AddScoped(readOnlyKeyInterfaceType, repoKeyImplType);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to register repository for entity {EntityType}", entityType.FullName);
        }
    }
}