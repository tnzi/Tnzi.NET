namespace Tnzi.EFCore.Providers;

/// <summary>
/// SQL Server 数据库提供者配置器
/// </summary>
public class SqlServerConfigurator : DatabaseProviderConfiguratorBase
{
    public override DatabaseProvider Provider => DatabaseProvider.SqlServer;
    
    protected override string AssemblyName => "Microsoft.EntityFrameworkCore.SqlServer";
    
    protected override string ExtensionMethodName => "UseSqlServer";
}

