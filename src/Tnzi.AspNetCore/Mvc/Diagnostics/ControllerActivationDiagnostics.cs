namespace Tnzi.AspNetCore.Mvc.Diagnostics;

/// <summary>
/// Collects diagnostic information about controller activation decisions.
/// Populated by IApplicationModelProvider implementations during startup,
/// flushed to ILogger during OnApplicationInitializationAsync.
/// </summary>
public class ControllerActivationDiagnostics
{
    private readonly List<ControllerActivationRecord> _records = [];

    public void RecordActivation(string controllerType, string? module, string? route, bool isDefault)
    {
        _records.Add(new ControllerActivationRecord
        {
            ControllerType = controllerType,
            Module = module,
            Route = route,
            IsDefault = isDefault,
            IsActive = true
        });
    }

    public void RecordSuppression(string controllerType, string detail, SuppressionReason reason)
    {
        _records.Add(new ControllerActivationRecord
        {
            ControllerType = controllerType,
            IsActive = false,
            SuppressionReason = reason,
            SuppressionDetail = detail
        });
    }

    public void RecordReplacement(string defaultController, string userController, string route)
    {
        _records.Add(new ControllerActivationRecord
        {
            ControllerType = defaultController,
            Route = route,
            IsDefault = true,
            IsActive = false,
            SuppressionReason = SuppressionReason.ReplacedByUser,
            ReplacedBy = userController
        });
    }

    public IReadOnlyList<ControllerActivationRecord> GetRecords() => _records;

    /// <summary>
    /// Flush all records to logger
    /// </summary>
    public void FlushToLogger(ILogger logger)
    {
        foreach (var record in _records)
        {
            if (record.IsActive)
            {
                logger.LogDebug("[DefaultController] {ControllerType} activated at route {Route}",
                    record.ControllerType, record.Route);
            }
            else if (record.SuppressionReason == SuppressionReason.ReplacedByUser)
            {
                logger.LogInformation(
                    "Default controller {Default} replaced by {User} at route {Route}",
                    record.ControllerType, record.ReplacedBy, record.Route);
            }
            else
            {
                logger.LogDebug("Controller {ControllerType} suppressed: {Detail}",
                    record.ControllerType, record.SuppressionDetail);
            }
        }

        _records.Clear();
    }
}

public class ControllerActivationRecord
{
    public string ControllerType { get; init; } = string.Empty;
    public string? Module { get; init; }
    public string? Route { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public SuppressionReason SuppressionReason { get; init; }
    public string? SuppressionDetail { get; init; }
    public string? ReplacedBy { get; init; }
}

public enum SuppressionReason
{
    None,
    MarkerAbsent,
    MissingDependency,
    ReplacedByUser
}
