namespace Tnzi.EFCore.Providers;

/// <summary>
/// SQLite 数据库提供者配置器
/// </summary>
public class SqliteConfigurator : DatabaseProviderConfiguratorBase
{
    public override DatabaseProvider Provider => DatabaseProvider.Sqlite;
    
    protected override string AssemblyName => "Microsoft.EntityFrameworkCore.Sqlite";
    
    protected override string ExtensionMethodName => "UseSqlite";
}

