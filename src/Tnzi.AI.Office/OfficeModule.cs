using Tnzi.AI;
using Tnzi.AI.Office.Tools;

namespace Tnzi.AI.Office;

[DependsOn(typeof(AIModule))]
public class OfficeModule : TnziApplicationModule
{
    public override int LoadOrder => 70;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // IOfficeFileCreator / IOfficeFileMutator implementations auto-register via
        // ISingletonDependency marker interface. OfficeFileToolProvider also singleton.
        // Register explicitly for clarity and to ensure correct DI lifetime.
        context.Services.AddSingleton<Creators.XlsxFileCreator>();
        context.Services.AddSingleton<Creators.DocxFileCreator>();
        context.Services.AddSingleton<Mutators.XlsxFileMutator>();
        context.Services.AddSingleton<Mutators.DocxFileMutator>();
        context.Services.AddSingleton<IOfficeFileCreator>(sp => sp.GetRequiredService<Creators.XlsxFileCreator>());
        context.Services.AddSingleton<IOfficeFileCreator>(sp => sp.GetRequiredService<Creators.DocxFileCreator>());
        context.Services.AddSingleton<IOfficeFileMutator>(sp => sp.GetRequiredService<Mutators.XlsxFileMutator>());
        context.Services.AddSingleton<IOfficeFileMutator>(sp => sp.GetRequiredService<Mutators.DocxFileMutator>());
        context.Services.AddScoped<OfficeFileTools>();
        return Task.CompletedTask;
    }
}
