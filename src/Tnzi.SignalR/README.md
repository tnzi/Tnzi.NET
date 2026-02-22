# Tnzi.SignalR 模块

## 概述

Tnzi.SignalR 模块提供了企业级的 SignalR 封装，支持强类型 Hub、授权机制、Redis Backplane、MessagePack 协议和速率限制等功能。

## 功能特性

### ✅ 核心功能

| 功能 | 说明 |
|------|------|
| **强类型 Hub** | 泛型 `TnziHub<TClient>` 基类，支持编译时类型检查 |
| **授权机制** | `HubAuthorizeAttribute` 特性，复用框架 `IPermissionChecker` |
| **连接管理** | 用户连接追踪，支持多连接和在线状态检查 |
| **消息推送** | 支持单用户、多用户、组和广播推送 |

### ✅ 可选功能

| 功能 | 配置 |
|------|------|
| **Redis Backplane** | `SignalR:Backplane:Type = "Redis"` |
| **MessagePack 协议** | `SignalR:MessagePack:Enabled = true` |
| **速率限制** | `SignalR:RateLimit:Enabled = true` |
| **详细日志** | `SignalR:EnableDetailedLogging = true` |

## 快速开始

### 1. 添加模块依赖

```csharp
[DependsOn(typeof(SignalRModule))]
public class YourAppModule : TnziApplicationModule
{
    // ...
}
```

### 2. 配置选项 (appsettings.json)

```json
{
  "SignalR": {
    "Hub": {
      "EnableDetailedErrors": false,
      "KeepAliveInterval": "00:00:15",
      "ClientTimeoutInterval": "00:00:30"
    },
    "Backplane": {
      "Type": "Redis",
      "ConnectionString": "localhost:6379",
      "ChannelPrefix": "MyApp.SignalR"
    },
    "MessagePack": {
      "Enabled": true,
      "EnableCompression": true
    },
    "RateLimit": {
      "Enabled": true,
      "MaxConnectionsPerUser": 5,
      "MaxMessagesPerMinute": 60
    },
    "EnableDetailedLogging": false
  }
}
```

### 3. 创建 Hub

**连接管理与推送**：`TnziHub` 支持无参构造与 `(IConnectionManager, IPermissionChecker?)` 构造。**必须**注入 `IConnectionManager`（使用带参构造）才能启用连接管理与 `IMessagePushService` 的按用户推送；仅使用无参构造时，仅能使用组/广播等不依赖连接管理的能力。

```csharp
// 定义客户端接口
public interface IChatClient : ITnziHubClient
{
    Task ReceiveMessage(string user, string message);
    Task UserJoined(string user);
}

// 创建 Hub（注入 IConnectionManager 以启用连接追踪与按用户推送）
public class ChatHub : TnziHub<IChatClient>
{
    public ChatHub(
        IConnectionManager connectionManager,
        IPermissionChecker? permissionChecker = null)
        : base(connectionManager, permissionChecker)
    {
    }

    [HubAuthorize("Chat.Send")]
    public async Task SendMessage(string message)
    {
        await Clients.All.ReceiveMessage(CurrentUserName ?? "Anonymous", message);
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await Clients.Others.UserJoined(CurrentUserName ?? "Anonymous");
    }
}
```

### 4. 注册 Hub 路由和消息推送服务

```csharp
// 在 Program.cs 或模块中注册
app.MapHub<ChatHub>("/hubs/chat");

// 注册消息推送服务 (如需要在其他服务中推送消息)
services.AddScoped<IMessagePushService<ChatHub>, MessagePushService<ChatHub>>();
// 可选：注册非泛型接口
services.AddScoped<IMessagePushService>(sp => sp.GetRequiredService<IMessagePushService<ChatHub>>());
```

## 授权机制

使用 `[HubAuthorize(PermissionName = "...")]` 进行权限检查时，须加载 **Authorization 模块**并注册 `IPermissionChecker`；否则会抛出 "Authorization service unavailable"。

### HubAuthorizeAttribute

```csharp
// 仅验证认证
[HubAuthorize]
public Task SecureMethod() { }

// 验证权限
[HubAuthorize("Admin.User.Manage")]
public Task AdminMethod() { }

// 验证角色
[HubAuthorize(Roles = "Admin,Manager")]
public Task RoleMethod() { }
```

### Hub 内权限检查

```csharp
public class MyHub : TnziHub<IMyClient>
{
    public async Task DoSomething()
    {
        // 检查权限
        if (await HasPermissionAsync("Feature.DoSomething"))
        {
            // 有权限
        }

        // 或者直接要求权限
        await RequirePermissionAsync("Feature.DoSomething");

        // 检查角色
        RequireRole("Admin", "Manager");

        // 检查认证
        RequireAuthentication();
    }
}
```

## Hub Filters

模块自动注册以下 Filters：

1. **ExceptionHandlingHubFilter** - 统一异常处理
2. **HubAuthorizationFilter** - 授权检查
3. **RateLimitHubFilter** - 速率限制（仅当 `SignalR:RateLimit:Enabled = true` 时注册）；连接建立时检查连接数与被封禁状态，方法调用时检查消息速率并记录，超限则拒绝并可封禁，**无需应用额外调用**
4. **LoggingHubFilter** - 日志记录（仅当 `SignalR:EnableDetailedLogging = true` 时注册）

## 依赖项

- **`Tnzi.AspNetCore`**：本模块通过 `AspNetCoreModule` 依赖 `CachingModule`，使用 `ICache` 与 `CacheKeys.SignalR`；使用 SignalR 时须确保已加载 `AspNetCoreModule`（进而加载 Caching）
- `Microsoft.AspNetCore.SignalR.StackExchangeRedis` (Redis Backplane)
- `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` (MessagePack)

## 配置选项

### SignalROptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Hub | HubOptions | - | Hub 配置 |
| Backplane | BackplaneOptions? | null | Backplane 配置 |
| MessagePack | MessagePackOptions | - | MessagePack 配置 |
| RateLimit | RateLimitOptions | - | 速率限制配置 |
| EnableDetailedLogging | bool | false | 启用详细日志 |

### HubOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| EnableDetailedErrors | bool | false | 启用详细错误 |
| KeepAliveInterval | TimeSpan | 15秒 | 心跳间隔 |
| ClientTimeoutInterval | TimeSpan | 30秒 | 客户端超时 |
| HandshakeTimeout | TimeSpan | 15秒 | 握手超时 |
| MaximumReceiveMessageSize | int? | 32KB | 最大消息大小 |

### BackplaneOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Type | BackplaneType | None | Backplane 类型 |
| ConnectionString | string? | null | Redis 连接字符串；Redis 时**仅使用**本配置，不使用其他模块的 Redis 配置 |
| ChannelPrefix | string | "Tnzi.SignalR" | 频道前缀 |

### RateLimitOptions

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| Enabled | bool | false | 启用速率限制 |
| MaxConnectionsPerUser | int | 5 | 每用户最大连接数 |
| MaxMessagesPerMinute | int | 60 | 每分钟最大消息数 |
| BanDuration | TimeSpan | 5分钟 | 封禁时长 |

---

**版本**: 2.0  
**最后更新**: 2026-01-06
