namespace Tnzi.AI.Tests;

/// <summary>
/// 上下文注入中间件 — Persona/Profile 注入测试
/// </summary>
public class ContextInjection_PersonaTests
{
    private readonly CompositeContextProvider _contextProvider;
    private readonly ILogger<ContextInjectionMiddleware> _logger;

    public ContextInjection_PersonaTests()
    {
        var aiOptions = Microsoft.Extensions.Options.Options.Create(new AIOptions());
        _contextProvider = new CompositeContextProvider(
            NullLogger<CompositeContextProvider>.Instance, aiOptions);
        _logger = NullLogger<ContextInjectionMiddleware>.Instance;
    }

    [Fact]
    public async Task InvokeAsync_WithPersona_InjectsSoulBlock()
    {
        // Arrange
        var personaId = Guid.NewGuid();
        var personaService = new Mock<IAgentPersonaService>();
        personaService.Setup(s => s.GetByIdAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AgentPersonaDto>.Success(new AgentPersonaDto
            {
                Id = personaId,
                Content = "You are a creative writer who loves metaphors."
            }));

        var sp = BuildServiceProvider(personaService: personaService.Object);
        var context = CreateContext(sp,
            agentConfig: JsonSerializer.Serialize(new { personaId = personaId.ToString() }));

        var middleware = new ContextInjectionMiddleware(_contextProvider, _logger);

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }), CancellationToken.None);

        // Assert
        var systemTexts = context.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? "")
            .ToList();

        Assert.Contains(systemTexts, m => m.Contains("<soul>"));
        Assert.Contains(systemTexts, m => m.Contains("creative writer"));
    }

    [Fact]
    public async Task InvokeAsync_WithUserProfile_InjectsUserProfileBlock()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var profile = new UserProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DisplayName = "Alice",
            Role = "Developer",
            PreferredLanguage = "zh-CN",
            Content = "I prefer concise answers."
        };

        var profileService = new Mock<IUserProfileService>();
        profileService.Setup(s => s.FindByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var sp = BuildServiceProvider(profileService: profileService.Object);
        var context = CreateContext(sp, userId: userId);

        var middleware = new ContextInjectionMiddleware(_contextProvider, _logger);

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }), CancellationToken.None);

        // Assert
        var systemTexts = context.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? "")
            .ToList();

        Assert.Contains(systemTexts, m => m.Contains("<user_profile>"));
        Assert.Contains(systemTexts, m => m.Contains("Alice"));
        Assert.Contains(systemTexts, m => m.Contains("Developer"));
    }

    [Fact]
    public async Task InvokeAsync_NoPersonaNoProfile_NoExtraBlocks()
    {
        // Arrange — no personaId in config, no userId
        var sp = BuildServiceProvider();
        var context = CreateContext(sp);

        var middleware = new ContextInjectionMiddleware(_contextProvider, _logger);

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }), CancellationToken.None);

        // Assert
        var systemTexts = string.Join(" ", context.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? ""));

        Assert.DoesNotContain("<soul>", systemTexts);
        Assert.DoesNotContain("<user_profile>", systemTexts);
    }

    [Fact]
    public async Task InvokeAsync_PersonaServiceNotRegistered_NoSoulBlock()
    {
        // Arrange — personaId exists in config, but no IAgentPersonaService registered
        var personaId = Guid.NewGuid();
        var sp = BuildServiceProvider(); // no personaService
        var context = CreateContext(sp,
            agentConfig: JsonSerializer.Serialize(new { personaId = personaId.ToString() }));

        var middleware = new ContextInjectionMiddleware(_contextProvider, _logger);

        // Act
        await middleware.InvokeAsync(context, (ctx, ct) =>
            Task.FromResult(new AgentRunResult { Response = "ok" }), CancellationToken.None);

        // Assert
        var systemTexts = string.Join(" ", context.Messages
            .Where(m => m.Role == ChatRole.System)
            .Select(m => m.Text ?? ""));

        Assert.DoesNotContain("<soul>", systemTexts);
    }

    private static AiMiddlewareContext CreateContext(IServiceProvider sp, string? agentConfig = null, Guid? userId = null)
    {
        var request = new AgentRunRequest
        {
            AgentId = Guid.NewGuid(),
            UserMessage = "Hello",
            UserId = userId
        };

        var resolution = AgentResolution.Success(
            agent: new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions()),
            provider: "openai",
            model: "gpt-4",
            agentId: request.AgentId,
            agentConfiguration: agentConfig);

        return new AiMiddlewareContext
        {
            Request = request,
            Agent = resolution,
            Messages = [new ChatMessage(ChatRole.User, "Hello")],
            ServiceProvider = sp
        };
    }

    private static IServiceProvider BuildServiceProvider(IAgentPersonaService? personaService = null, IUserProfileService? profileService = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (personaService != null)
            services.AddSingleton(personaService);
        if (profileService != null)
            services.AddSingleton(profileService);
        return services.BuildServiceProvider();
    }
}
