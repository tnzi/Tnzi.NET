namespace Tnzi.EFCore.Providers;

/// <summary>
/// PostgreSQL 数据库提供者配置器
/// </summary>
public class PostgreSqlConfigurator : DatabaseProviderConfiguratorBase
{
    public override DatabaseProvider Provider => DatabaseProvider.PostgreSQL;
    
    protected override string AssemblyName => "Npgsql.EntityFrameworkCore.PostgreSQL";
    
    protected override string ExtensionMethodName => "UseNpgsql";
}

