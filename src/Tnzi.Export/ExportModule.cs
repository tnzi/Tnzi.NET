using Tnzi.Modules;

namespace Tnzi.Export;

public class ExportModule : TnziCustomModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Exporters auto-register via ISingletonDependency marker interface
        // (see Abstractions/ITableExporter.cs consumers).
        return Task.CompletedTask;
    }
}
