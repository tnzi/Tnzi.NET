
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// 技能上下文贡献者
/// </summary>
internal sealed class SkillContributor : IContextProviderContributor
{
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISkillRegistry? _skillRegistry;
    private readonly ISkillTemplateEngine? _skillTemplateEngine;
    private readonly ISkillConstraintEnforcer? _skillConstraintEnforcer;
    private readonly ISkillLoadTracker? _skillLoadTracker;
    private readonly ISkillActivationTracker? _skillActivationTracker;
    private readonly IEventBus? _eventBus;

    public int Order => ContextProviderOrders.Skills;

    public SkillContributor(
        IOptionsMonitor<AIOptions> options,
        ILoggerFactory loggerFactory,
        ISkillRegistry? skillRegistry = null,
        ISkillTemplateEngine? skillTemplateEngine = null,
        ISkillConstraintEnforcer? skillConstraintEnforcer = null,
        ISkillLoadTracker? skillLoadTracker = null,
        ISkillActivationTracker? skillActivationTracker = null,
        IEventBus? eventBus = null)
    {
        _options = Check.NotNull(options);
        _loggerFactory = Check.NotNull(loggerFactory);
        _skillRegistry = skillRegistry;
        _skillTemplateEngine = skillTemplateEngine;
        _skillConstraintEnforcer = skillConstraintEnforcer;
        _skillLoadTracker = skillLoadTracker;
        _skillActivationTracker = skillActivationTracker;
        _eventBus = eventBus;
    }

    public IContextProvider? TryCreate(ContextProviderCreationContext context)
    {
        if (!_options.CurrentValue.ContextProviders.Skills.Enabled) return null;
        if (_skillRegistry == null || _skillTemplateEngine == null || _skillLoadTracker == null) return null;

        try
        {
            var skillsOptions = _options.CurrentValue.ContextProviders.Skills;
            var logger = _loggerFactory.CreateLogger<SkillContextProvider>();
            return new SkillContextProvider(
                _skillRegistry, _skillTemplateEngine, skillsOptions, logger, _skillConstraintEnforcer, _skillLoadTracker,
                context.AgentName, context.SkillSlugs, _skillActivationTracker, context.ThreadId,
                _eventBus, context.UserId);
        }
        catch (Exception ex)
        {
            _loggerFactory.CreateLogger<SkillContributor>()
                .LogError(ex, "Failed to create SkillContextProvider");
            return null;
        }
    }
}
