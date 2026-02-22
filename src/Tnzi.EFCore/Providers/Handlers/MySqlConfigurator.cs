
namespace Tnzi.EFCore.Providers;

/// <summary>
/// MySQL 数据库提供者配置器
/// 使用 Pomelo.EntityFrameworkCore.MySql
/// 支持 Pomelo 7.0+ (需要 ServerVersion 参数) 和旧版本
/// </summary>
public class MySqlConfigurator : DatabaseProviderConfiguratorBase
{
    public override DatabaseProvider Provider => DatabaseProvider.MySql;

    protected override string AssemblyName => "Pomelo.EntityFrameworkCore.MySql";

    protected override string ExtensionMethodName => "UseMySql";

    /// <summary>
    /// MySQL 的 UseMySql 方法查找逻辑
    /// Pomelo 7.0+ 签名: UseMySql(builder, connectionString, serverVersion, action?)
    /// Pomelo 旧版签名: UseMySql(builder, connectionString, action?)
    /// </summary>
    protected override MethodInfo? FindExtensionMethod(Assembly assembly, string methodName)
    {
        var extensionType = assembly.GetTypes()
            .FirstOrDefault(t => t.Name.Contains("DbContextOptionsBuilderExtensions", StringComparison.OrdinalIgnoreCase));

        if (extensionType == null)
        {
            return null;
        }

        var methods = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "UseMySql" && m.GetParameters().Length >= 2)
            .ToList();

        // 优先查找 Pomelo 7.0+ 签名: (DbContextOptionsBuilder, string, ServerVersion, Action<>?)
        // ServerVersion 类型名包含 "ServerVersion"
        var pomeloV7Method = methods.FirstOrDefault(m =>
        {
            var parameters = m.GetParameters();
            return parameters.Length >= 3 &&
                   parameters[0].ParameterType == typeof(DbContextOptionsBuilder) &&
                   parameters[1].ParameterType == typeof(string) &&
                   parameters[2].ParameterType.Name == "ServerVersion";
        });

        if (pomeloV7Method != null)
            return pomeloV7Method;

        // 回退到旧版签名: (DbContextOptionsBuilder, string) 或 (DbContextOptionsBuilder, string, Action<>)
        var legacyMethod = methods.FirstOrDefault(m =>
        {
            var parameters = m.GetParameters();
            return parameters[0].ParameterType == typeof(DbContextOptionsBuilder) &&
                   parameters[1].ParameterType == typeof(string) &&
                   (parameters.Length == 2 || (parameters.Length == 3 && parameters[2].ParameterType.IsGenericType));
        });

        return legacyMethod;
    }

    /// <summary>
    /// MySQL 的调用方式
    /// Pomelo 7.0+ 需要通过 ServerVersion.AutoDetect(connectionString) 获取版本
    /// </summary>
    protected override void InvokeExtensionMethod(MethodInfo method, DbContextOptionsBuilder builder, string connectionString)
    {
        var parameters = method.GetParameters();

        // Pomelo 7.0+: UseMySql(builder, connectionString, serverVersion, action?)
        if (parameters.Length >= 3 && parameters[2].ParameterType.Name == "ServerVersion")
        {
            var serverVersionType = parameters[2].ParameterType;

            // 调用 ServerVersion.AutoDetect(connectionString) 静态方法
            var autoDetectMethod = serverVersionType.GetMethod("AutoDetect",
                BindingFlags.Public | BindingFlags.Static,
                null, [typeof(string)], null);

            if (autoDetectMethod == null)
            {
                throw new InvalidOperationException(
                    "ServerVersion.AutoDetect method not found. " +
                    "Please ensure your Pomelo.EntityFrameworkCore.MySql version is compatible.");
            }

            var serverVersion = autoDetectMethod.Invoke(null, [connectionString]);

            if (parameters.Length == 3)
            {
                method.Invoke(null, [builder, connectionString, serverVersion!]);
            }
            else
            {
                // 第四个参数是 Action<MySqlDbContextOptionsBuilder>，传 null
                method.Invoke(null, [builder, connectionString, serverVersion!, null!]);
            }
        }
        else if (parameters.Length == 2)
        {
            // 旧版: UseMySql(builder, connectionString)
            method.Invoke(null, [builder, connectionString]);
        }
        else if (parameters.Length == 3)
        {
            // 旧版: UseMySql(builder, connectionString, action)
            method.Invoke(null, [builder, connectionString, null!]);
        }
        else
        {
            throw new InvalidOperationException(
                $"UseMySql method with {parameters.Length} parameters is not supported.");
        }
    }
}