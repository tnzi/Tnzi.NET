namespace Tnzi.Finance.Ai;

/// <summary>
/// Finance AI sub-module: ships the default AI-backed <see cref="IReceiptExtractor"/> so the finance
/// module's receipt capture (<c>admin/finance/receipts/{id}/extract</c>) works out of the box.
/// </summary>
/// <remarks>
/// Optional. The <see cref="IReceiptExtractor"/> contract and its 501 guide stay in Finance core (which
/// keeps zero AI/Storage references — the contract passes a <c>FileId</c>, not a byte stream). Loading
/// this module binds the vision + PDF-text implementation; a consumer may still register its own
/// <see cref="IReceiptExtractor"/> to override (the default is registered via <c>TryAddScoped</c>). No
/// entities/tables, so no <c>TableNamePrefix</c> and <see cref="TnziCustomModule"/> is the right base.
/// </remarks>
[DependsOn(typeof(FinanceModule), typeof(AIModule), typeof(StorageModule))]
public class FinanceAiModule : TnziCustomModule
{
    /// <inheritdoc />
    public override int LoadOrder => 56;

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<FinanceAiOptions, FinanceAiOptionsValidator>(context.Configuration);
        return base.PreConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Default implementation for Finance's optional IReceiptExtractor dependency
        // (image/* vision + PDF text -> structured output). TryAddScoped lets a consumer
        // register its own extractor to override.
        context.Services.TryAddScoped<IReceiptExtractor, AiReceiptExtractor>();
        return base.ConfigureServicesAsync(context);
    }
}
