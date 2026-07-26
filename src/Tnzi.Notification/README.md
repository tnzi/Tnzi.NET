# Tnzi.Notification 通知模块

## 功能概述

通知模块提供了完整的通知发送和管理功能，支持邮件、短信和推送通知。模块经过全面重构，提升了代码质量、性能和可维护性。

## 核心功能

### ✅ 已实现功能

1. **持久化存储**
    - 所有通知记录自动保存到数据库
    - 支持查询、分页、筛选
    - 记录发送状态、失败原因、重试次数
    - 详细统计：成功数、失败数、总接收者数

2. **群发支持**
    - 邮件支持群发（批量发送）
    - 短信支持群发
    - 推送通知支持群发
    - 并发控制，可配置最大并发数

3. **邮件附件**
    - 支持添加多个附件
    - 支持本地文件和URL
    - 自动识别MIME类型
    - 使用 IHttpClientFactory 下载URL附件

4. **多种通知类型**
    - Email（邮件）
    - SMS（短信）
    - Push（推送通知）

5. **发送状态管理**
    - 待发送（Pending）
    - 发送中（Sending）
    - 发送成功（Sent）
    - 发送失败（Failed）
    - 已取消（Cancelled）

6. **重试机制**
    - 支持配置最大重试次数
    - 支持延迟重试
    - 支持指数退避策略
    - 失败后可手动重试

7. **队列支持**
    - 抽象队列接口，支持多种实现
    - Hangfire 队列（可选）
    - 内存队列（默认回退）
    - 队列不可用时自动降级为立即发送

8. **事务处理**
    - 完整的数据库事务支持
    - 确保批量发送的数据一致性

9. **外部消息ID追踪**
    - 自动保存外部服务返回的消息ID
    - 支持 Twilio SID、Plivo UUID、FCM Message ID 等

10. **输入验证**
    - 完整的请求参数验证
    - 接收者列表验证
    - 内容长度验证（短信）

11. **性能优化**
    - 查询使用 AsNoTracking
    - 并发发送控制
    - 批量操作优化

## 项目结构

```
Tnzi.Notification/
├── Entities/
│   ├── Message.cs                     # 消息实体
│   ├── Recipient.cs                   # 接收者实体
│   ├── Attachment.cs                  # 附件实体（[FileField] FileId）
│   ├── NotificationPreference.cs      # 通知偏好实体
│   └── Configs/                       # 实体配置
├── Dtos/                              # 通知 / 邮件 / 偏好 / 统计 / 发送结果 DTOs
├── Services/
│   ├── Interfaces/                     # INotificationService / *QueryService / *RetryService /
│   │                                   #   IUserNotificationService / *PreferenceService / *QueueService /
│   │                                   #   IEmailSender / ISmsSender / IPushSender
│   ├── NotificationService.cs         # 通知创建/发送编排
│   ├── NotificationQueryService.cs    # 管理端查询 + 统计
│   ├── NotificationRetryService.cs    # 失败重试（指数退避）
│   ├── UserNotificationService.cs     # 用户收件箱
│   ├── NotificationPreferenceService.cs # 通知偏好
│   ├── ChannelQueueService.cs         # 后台队列（System.Threading.Channels）
│   ├── MailKitEmailSender.cs          # 邮件发送（MailKit）
│   ├── HttpSmsSender.cs               # 短信发送（HTTP REST：Twilio/Plivo）
│   ├── PushSender.cs                  # 推送（Firebase）
│   └── Null*Sender.cs                 # 空实现（未配置渠道时降级）
├── Options/
│   └── NotificationOptions.cs         # 配置选项（模板配置归 Tnzi.Template）
├── NotificationModule.cs              # 模块配置
└── GlobalUsings.cs                    # 全局引用
```

## 模板（由 Tnzi.Template 模块提供）

Notification **不自带模板子系统**。消息模板通过可选依赖 `Tnzi.Template` 的
`ITemplateRenderService` 渲染（未加载 Template 模块时，直接使用 `request.Content`）。
模板存储、CRUD、布局均归 `Tnzi.Template`（`Template_Template` / `Template_Layout`
表 + 文件系统），相关配置在 `Template:*` 配置节（`TemplateRootPath` 默认 `Templates`、
`EnableFileSystemTemplates` 默认 `true`、`TemplateExtension` 默认 `.cshtml`）。

### 路径约定（关键）

模板文件路径为 `Templates/{Module}/{Category}/{TemplateName}.cshtml`。对通知模板，
**Module = `Notification`，Category = 渠道名**（`Email` / `Sms` / `Push`，即
`NotificationType.ToString()`）：

```
Templates/
  └── Notification/
      ├── Email/
      │   ├── TwoFactorCode.cshtml
      │   ├── PasswordReset.cshtml
      │   └── WelcomeEmail.cshtml
      └── Sms/
          └── TwoFactorCode.cshtml
```

> ⚠️ 子目录名必须与渠道名**逐字一致**：是 `Sms`（不是 `SMS`）、`Email`、`Push`——
> 对应 `NotificationType.Sms/Email/Push` 的 `ToString()`。在大小写敏感的文件系统
> （Linux）上，`SMS/` 会导致模板查找失败、正文为空。

发送时 `CreateNotificationRequest` 通常只设置 `Type` + `TemplateName`（不设 `Category`）。
`NotificationService` 会在未显式指定 `Category` 时，**自动以渠道名作为模板查找的
Category**，从而命中上述按渠道组织的模板。若显式设置了 `Category`（自定义分组），
则以显式值为准。

框架在 `Tnzi.Hosting` 内自带了 `Notification/{Email,Sms}/` 下的默认模板
（`TwoFactorCode` / `PasswordReset` / `WelcomeEmail` / `EmailConfirmation`），
经 `CopyToOutputDirectory` 复制到消费方输出目录，开箱即用。

### 模板文件格式（YAML front matter + Razor）

```cshtml
---
Subject: Welcome to @Model.AppName!
Layout: EmailDefault
Description: Welcome email
---
<h2>Welcome @Model.UserName!</h2>
<p>Thanks for joining @Model.AppName.</p>
```

### 模板查找优先级

1. 数据库（`Template_Template`，可经 Template 管理端编辑/覆盖，命中即用）
2. 文件系统（`Templates/{Module}/{Category}/*.cshtml`，由 `Template:EnableFileSystemTemplates` 控制，默认开启）

命中不到时 `NotificationService` 回退到 `request.Content`（未提供则为空）。
若要在管理端覆盖框架自带模板，请以相同的 **渠道 Category**（`Email` / `Sms`）新建
DB 模板，使其优先于文件系统版本。

### 确保发布可找到模板文件

1. **配置路径**：在应用配置中设置 `Template:TemplateRootPath` 指向发布输出内的 `Templates` 目录。默认值为 `Templates`，若与发布结构一致可不改。
2. **复制到输出**：在应用层 csproj 中为模板目录添加：
    ```xml
    <ItemGroup>
      <Content Include=\"Templates\\**\\*\" CopyToOutputDirectory=\"PreserveNewest\" />
    </ItemGroup>
    ```
3. **多环境/租户**：可通过环境变量覆盖 `Template:TemplateRootPath`，指向各自的模板目录。

## 使用示例

### 1. 配置

在 `appsettings.json` 中配置：

```json
{
    "Notification": {
        "MailSender": {
            "SmtpServer": "smtp.example.com",
            "SmtpPort": 587,
            "Username": "your-email@example.com",
            "Password": "your-password",
            "EnableSsl": true,
            "FromEmail": "noreply@example.com",
            "FromName": "Tnzi System"
        },
        "SmsSender": {
            "Provider": "twilio",
            "TwilioAccountSid": "your-account-sid",
            "TwilioAuthToken": "your-auth-token",
            "TwilioFromPhoneNumber": "+1234567890"
        },
        "PushSender": {
            "Provider": "fcm",
            "FirebaseProjectId": "your-project-id",
            "FirebaseServiceAccountJsonPath": "/path/to/service-account.json"
        },
        "Queue": {
            "Provider": "Hangfire",
            "Enabled": true
        },
        "MaxConcurrency": 10,
        "Retry": {
            "RetryDelaySeconds": 60,
            "EnableExponentialBackoff": true
        }
    }
}
```

> 模板不在 `Notification` 配置节下。模板文件路径 / 开关等由 `Tnzi.Template` 的
> `Template:*` 配置节控制（见上「模板」一节）。

### 2. 使用主 DbContext（自动注册）

通知模块的实体已通过自动注册机制自动注册到您的主 DbContext（如 `DefaultDbContext`），无需手动创建独立的 DbContext。

如果您的主 DbContext 继承自 `IdentityDbContext` 或 `TnziDbContext`，通知实体会自动被注册。

### 3. 发送通知

```csharp
public class MyService
{
    private readonly IMessageService _messageService;

    public MyService(IMessageService messageService)
    {
        _messageService = messageService;
    }

    // 发送邮件（带附件）
    public async Task SendEmailWithAttachment()
    {
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Test Email",
            Content = "<h1>This is a test email</h1>",
            IsHtml = true,
            Recipients = new List<RecipientInfo>
            {
                new() { Address = "user1@example.com", Name = "User 1" },
                new() { Address = "user2@example.com", Name = "User 2" }
            },
            Attachments = new List<AttachmentInfo>
            {
                new()
                {
                    FileName = "report.pdf",
                    FilePath = "/path/to/report.pdf",
                    ContentType = "application/pdf"
                }
            },
            SendImmediately = true,
            MaxRetryCount = 3
        };

        await _messageService.CreateAndSendAsync(request);
    }

    // 发送短信（群发，加入队列）
    public async Task SendSmsBatch()
    {
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Sms,
            Subject = "System Notification",
            Content = "Your verification code is: 123456",
            Recipients = new List<RecipientInfo>
            {
                new() { Address = "13800138000" },
                new() { Address = "13900139000" }
            },
            SendImmediately = false,  // 加入队列
            MaxRetryCount = 3
        };

        await _messageService.CreateAndSendAsync(request);
    }

    // 使用模板发送
    public async Task SendWithTemplate()
    {
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            Subject = "Welcome",
            Content = "Default content",
            TemplateName = "welcome-email",
            TemplateVariables = new Dictionary<string, object>
            {
                { "UserName", "John Doe" },
                { "ActivationLink", "https://example.com/activate" }
            },
            Recipients = new List<RecipientInfo>
            {
                new() { Address = "user@example.com", Name = "John Doe" }
            },
            SendImmediately = true
        };

        await _messageService.CreateAndSendAsync(request);
    }
}
```

### 4. 查询通知

```csharp
// 查询通知
var queryRequest = new QueryNotificationRequest
{
    Type = NotificationType.Email,
    Status = NotificationStatus.Sent,
    StartTime = DateTime.Now.AddDays(-7),
    PageIndex = 1,
    PageSize = 20
};

var result = await _messageService.QueryAsync(queryRequest);

// 根据ID获取消息
var notification = await _messageService.GetByIdAsync(notificationId);

// 查看统计信息
Console.WriteLine($"Total: {notification.TotalRecipientCount}, Success: {notification.SuccessCount}, Failed: {notification.FailureCount}");
```

### 5. 重试失败的通知

```csharp
// 重试失败的消息（支持延迟和指数退避）
await _messageService.RetryAsync(notificationId);
```

### 6. 取消通知

```csharp
// 取消待发送的消息
await _messageService.CancelAsync(notificationId);
```

## 数据库表结构

### Message（消息表，表名：Notification_Message）

- `Id` - 主键
- `Type` - 通知类型（Email/SMS/Push）
- `Subject` - 主题/标题
- `Content` - 内容
- `IsHtml` - 是否HTML格式
- `Status` - 发送状态
- `SentTime` - 发送时间
- `FailureReason` - 失败原因
- `RetryCount` - 重试次数
- `MaxRetryCount` - 最大重试次数
- `TotalRecipientCount` - 总接收者数量
- `SuccessCount` - 成功发送数量
- `FailureCount` - 失败数量

### Recipient（接收者表，表名：Notification_Recipient）

- `Id` - 主键
- `MessageId` - 消息ID（外键）
- `Address` - 接收者地址（邮箱/手机号/设备Token）
- `Name` - 接收者名称
- `Status` - 发送状态
- `SentTime` - 发送时间
- `FailureReason` - 失败原因
- `ExternalMessageId` - 外部服务返回的消息ID（用于追踪）

### Attachment（附件表，表名：Notification_Attachment）

- `Id` - 主键
- `MessageId` - 消息ID（外键）
- `FileName` - 文件名
- `FilePath` - 文件路径或URL
- `FileSize` - 文件大小
- `ContentType` - MIME类型

## 配置选项

### NotificationOptions

| 属性           | 类型               | 说明         | 默认值                                                |
| -------------- | ------------------ | ------------ | ----------------------------------------------------- |
| MailSender     | MailSenderOptions? | 邮件发送配置 | null                                                  |
| SmsSender      | SmsSenderOptions?  | 短信发送配置 | null                                                  |
| PushSender     | PushSenderOptions? | 推送配置     | null                                                  |
| Queue          | QueueOptions       | 队列配置     | Provider: "Hangfire", Enabled: true                   |
| MaxConcurrency | int                | 最大并发数   | 10                                                    |
| Retry          | RetryOptions       | 重试配置     | RetryDelaySeconds: 60, EnableExponentialBackoff: true |

### QueueOptions

| 属性     | 类型   | 说明                                | 默认值     |
| -------- | ------ | ----------------------------------- | ---------- |
| Provider | string | 队列提供者 (Hangfire/InMemory/None) | "Hangfire" |
| Enabled  | bool   | 是否启用队列                        | true       |

### RetryOptions

| 属性                     | 类型 | 说明             | 默认值 |
| ------------------------ | ---- | ---------------- | ------ |
| RetryDelaySeconds        | int  | 重试延迟（秒）   | 60     |
| EnableExponentialBackoff | bool | 是否启用指数退避 | true   |

## 依赖项

- `Tnzi` - 框架核心（包含接口和实现）
- `Tnzi.EntityFrameworkCore` - EF Core 支持
- `Tnzi.Template` - 模板引擎支持
- `MailKit` - 邮件发送库
- `FirebaseAdmin` - Firebase推送服务
- `Hangfire.Core` - 后台任务队列（可选）
- `Microsoft.EntityFrameworkCore` - EF Core

> **注意**:
>
> - 短信服务使用HTTP REST API方式，无需Twilio或Plivo SDK，减少约500KB依赖
> - Hangfire 为可选依赖，未安装时自动使用内存队列

## 已实现的服务提供商

1. **短信服务**
    - ✅ Twilio（使用HTTP REST API方式，无SDK依赖）
    - ✅ Plivo（使用HTTP REST API方式，无SDK依赖）

2. **推送服务**
    - ✅ Firebase Cloud Messaging (FCM)
    - ⚠️ Apple Push Notification Service (APNs) - 占位符，未实现

3. **队列支持**
    - ✅ Hangfire队列集成（可选）
    - ✅ 内存队列（默认回退）

## 性能特性

1. **并发控制**
    - 使用 SemaphoreSlim 控制并发数
    - 可配置最大并发数，避免过载

2. **事务处理**
    - 完整的数据库事务支持
    - 确保批量发送的数据一致性

3. **查询优化**
    - 使用 AsNoTracking 提升查询性能
    - 优化的 Include 查询

4. **批量操作**
    - 并行发送，受并发数限制
    - 批量更新数据库

## 改进记录

### 2025-12-XX 重大重构

1. **抽象队列接口**
    - 创建 IQueueService 接口
    - 支持 Hangfire 和内存队列
    - 队列不可用时自动降级

2. **实体增强**
    - 添加 SuccessCount、FailureCount、TotalRecipientCount 字段
    - 支持详细统计追踪

3. **配置验证完善**
    - 完整的配置验证
    - 验证所有必需字段

4. **外部消息ID保存**
    - 所有发送器返回 SendResult
    - 自动保存外部消息ID

5. **HttpClient 优化**
    - 使用 IHttpClientFactory
    - 避免 HttpClient 资源泄漏

6. **并发控制**
    - SemaphoreSlim 控制并发
    - 可配置最大并发数

7. **事务处理**
    - 完整的数据库事务
    - 确保数据一致性

8. **输入验证**
    - 完整的参数验证
    - 内容长度验证

9. **重试优化**
    - 支持延迟重试
    - 支持指数退避

10. **性能优化**
    - AsNoTracking 查询
    - 批量操作优化

---

**最后更新**: 2025-12-XX
