namespace Tnzi.AI.Workflow;

/// <summary>
/// AI 工作流模块 - 提供 DAG 工作流引擎、节点类型、工作流服务和检查点管理
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
        context.Services.AddTnziOptions<WorkflowWatchdogOptions, WorkflowWatchdogOptionsValidator>(context.Configuration, "AI:WorkflowWatchdog");

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, AIWorkflowPermissions>();

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

        // 注册 Watchdog（Scoped - 每次扫描在自己的 scope 内解析）+ 驱动它的后台循环。
        // ★ 后台循环无条件注册，由 WorkflowWatchdogHostedService 自己读 UseBuiltInScheduler
        //   决定跑不跑并把结论写进启动日志 —— 按配置决定要不要注册的话，"关掉了"和
        //   "根本没接上"在日志里长得一模一样，而这正是此前的状况。
        services.AddScoped<WorkflowWatchdogService>();
        services.AddHostedService<WorkflowWatchdogHostedService>();
        // 执行期间的心跳（所有模式、与检查点无关）：没有它 RunningTimeout 量的是"跑了多久"而不是"卡住多久"。
        // Singleton：每次心跳在自己的作用域里工作，与请求作用域的 DbContext 并发也安全。TryAdd 允许消费方替换。
        services.TryAddSingleton<IWorkflowExecutionHeartbeat, DatabaseWorkflowExecutionHeartbeat>();

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
