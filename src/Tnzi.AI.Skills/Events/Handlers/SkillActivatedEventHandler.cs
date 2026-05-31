namespace Tnzi.AI.Skills.Events.Handlers;

/// <summary>
/// Handles SkillActivatedEvent — updates activation count and last activated time for database-stored skills.
/// </summary>
public class SkillActivatedEventHandler : IEventHandler<SkillActivatedEvent>
{
    private readonly IRepository<SkillEntity, Guid>? _repository;
    private readonly ILogger<SkillActivatedEventHandler> _logger;

    public SkillActivatedEventHandler(
        ILogger<SkillActivatedEventHandler> logger,
        IRepository<SkillEntity, Guid>? repository = null)
    {
        _logger = Check.NotNull(logger);
        _repository = repository;
    }

    public async Task HandleAsync(SkillActivatedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            // Only track DB-stored skills (Tenant/User scope)
            if (@event.Source != SkillSource.Database || _repository == null)
                return;

            // Narrow predicate to the exact resolved row using all identity fields.
            // This prevents cross-tenant and cross-user activation-count bleed when
            // multiple tenants/users share the same slug.
            var scopedTenantId = @event.TenantId;
            var scopedOwnerUserId = @event.OwnerUserId;

            var updated = await _repository
                .Where(e => e.Slug == @event.Slug
                         && e.Scope == @event.Scope
                         && e.TenantId == scopedTenantId
                         && e.OwnerUserId == scopedOwnerUserId
                         && !e.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.ActivationCount, e => e.ActivationCount + 1)
                    .SetProperty(e => e.LastActivatedAt, @event.ActivatedAt), cancellationToken);

            if (updated > 0)
            {
                _logger.LogDebug("Updated activation stats for skill '{Slug}': count incremented", @event.Slug);
            }
        }
        catch (Exception ex)
        {
            // Silent catch: event handler errors don't impact main flow
            _logger.LogWarning(ex, "Failed to update activation stats for skill '{Slug}'", @event.Slug);
        }
    }
}
