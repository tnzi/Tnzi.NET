using Tnzi.AI.Infrastructure;
using Tnzi.AI.Infrastructure.Stores;
using Tnzi.AI.Services.Interfaces;
using Tnzi.AI.Workflow.Options;

namespace Tnzi.AI.Workflow;

/// <summary>
/// AI 工作流模块 — 提供 DAG 工作流引擎、节点类型、工作流服务和检查点管理
/// </summary>
[DependsOn(typeof(AIModule))]
public class AIWorkflowModule : TnziApplicationModule
{
    /// <summary>
    /// 表名前缀（与 AI 核心共享）
    /// </summary>
    public override string? TableNamePrefix => "AI";

    /// <summary>
    /// 加载顺序（在 AIModule(50) 之后）
    /// </summary>
    public override int LoadOrder => 52;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<WorkflowWatchdogOptions>()
            .Bind(context.Configuration.GetSection("AI:WorkflowWatchdog"))
            .ValidateWith<WorkflowWatchdogOptions, WorkflowWatchdogOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册工作流服务
        services.AddScoped<WorkflowService>();
        services.AddScoped<IWorkflowService>(sp => sp.GetRequiredService<WorkflowService>());
        services.AddScoped<IWorkflowExecutionControlService>(sp => sp.GetRequiredService<WorkflowService>());
        services.AddScoped<IWorkflowExecutionQueryService>(sp => sp.GetRequiredService<WorkflowService>());

        // 注册工作流检查点存储（TryAdd：允许更早 Configure 的模块注册自定义实现）
        services.TryAddScoped<IWorkflowCheckpointStore, DatabaseWorkflowCheckpointStore>();
        // AIModule 的 NoOpWorkflowExecutionMailbox 回退在 PostConfigure 阶段才 TryAdd，
        // 本模块 Configure 阶段的注册必然先于回退 → 真实实现永远胜出，无须 RemoveAll。
        services.AddScoped<IWorkflowExecutionMailbox, WorkflowExecutionMailboxService>();

        // 注册工作流引擎组件
        services.AddScoped<IWorkflowNodeServiceContext, WorkflowNodeServiceContext>();
        services.AddScoped<WorkflowNodeExecutor>();
        services.AddScoped<WorkflowEngine>();

        // 注册 Watchdog（Scoped — 每次扫描由宿主调度器在其自己的 scope 内解析）
        services.AddScoped<WorkflowWatchdogService>();

        // 注册工作流节点
        services.AddScoped<IWorkflowNode, AgentNode>();
        services.AddScoped<IWorkflowNode, ReviewNode>();
        services.AddScoped<IWorkflowNode, ApprovalNode>();
        services.AddScoped<IWorkflowNode, RouterNode>();
        services.AddScoped<IWorkflowNode, ParallelNode>();
        services.AddScoped<IWorkflowNode, SynthesizeNode>();
        services.AddScoped<IWorkflowNode, DebateNode>();
        services.AddScoped<IWorkflowNode, ConditionalNode>();
        services.AddScoped<IWorkflowNode, TransformNode>();

        return Task.CompletedTask;
    }
}
