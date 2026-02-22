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
│   ├── Attachment.cs                  # 附件实体
│   ├── Template.cs                    # 消息模板实体
│   ├── Layout.cs                      # 消息布局实体
│   └── Configs/                       # 实体配置
├── Data/
│   └── NotificationDbContext.cs       # DbContext基类
├── Dtos/
│   ├── NotificationDtos.cs            # 通知DTOs
│   ├── EmailDtos.cs                   # 邮件DTOs
│   └── SendResult.cs                  # 发送结果
├── Services/
│   ├── IMessageService.cs             # 消息服务接口
│   ├── MessageService.cs               # 消息服务实现
│   ├── ITemplateService.cs            # 模板服务接口
│   ├── TemplateService.cs             # 模板服务实现
│   ├── ILayoutService.cs              # 布局服务接口
│   ├── LayoutService.cs               # 布局服务实现
│   ├── IEmailSender.cs                # 邮件发送接口
│   ├── MailKitEmailSender.cs          # 邮件发送实现（MailKit）
│   ├── ISmsSender.cs                  # 短信发送接口
│   ├── HttpSmsSender.cs               # 短信发送实现（HTTP REST API）
│   ├── IPushSender.cs                 # 推送发送接口
│   ├── PushSender.cs                  # 推送通知实现
│   ├── IQueueService.cs               # 队列服务接口
│   ├── HangfireQueueService.cs        # Hangfire队列实现
│   ├── InMemoryQueueService.cs        # 内存队列实现
│   └── Null*Sender.cs                 # 空实现（用于测试）
├── Options/
│   ├── NotificationOptions.cs        # 配置选项
│   ├── TemplateOptions.cs           # 模板配置选项
│   └── DefaultMessageTemplates.cs   # 默认消息模板
├── TnziNotificationModule.cs       # 模块配置
└── GlobalUsings.cs                   # 全局引用
```

## 文件系统模板（优先级最高）

- 目录结构：

    ```
    Templates/
      ├── Notification/
      │   ├── Email/
      │   │   └── UserWelcome.cshtml
      │   ├── SMS/
      │   │   └── VerificationCode.cshtml
      │   └── Push/
      │       └── OrderStatus.cshtml
      └── Layouts/
          ├── Email/
          │   └── _Default.cshtml
          └── SMS/
              └── _Default.cshtml
    ```

- 模板文件（YAML front matter + Razor）：

    ```cshtml
    ---
    Subject: Welcome to @Model.SiteName!
    Layout: EmailDefault
    Description: Welcome email
    Type: Email
    ---
    @model UserWelcomeModel

    <h2>Welcome @Model.UserName!</h2>
    <p>Thanks for joining @Model.SiteName.</p>
    ```

- 布局文件：
    ```cshtml
    ---
    Type: Email
    IsDefault: true
    Description: Default email layout
    ---
    <!DOCTYPE html>
    <html>
    <head>
        <title>@Model.Subject</title>
    </head>
    <body>
        <div>@Model.Content</div>
    </body>
    </html>
    ```

### 模板查找优先级

1. 文件系统（Templates/...）
2. 数据库（Template、Layout 实体）
3. 配置 `Notification:Templates`
4. （无代码内置默认值）

### 配置兜底示例（最小模板/布局）

在应用层的 `appsettings.json` 中提供最小兜底（英文），避免文件缺失时无法发送：

```json
{
    "Notification": {
        "Templates": {
            "DefaultTemplates": {
                "UserWelcome": {
                    "TemplateName": "UserWelcome",
                    "TemplateType": "Email",
                    "SubjectTemplate": "Welcome to @Model.SiteName!",
                    "ContentTemplate": "<h2>Welcome @Model.UserName!</h2><p>Thanks for joining @Model.SiteName.</p>",
                    "DefaultLayoutName": "EmailDefault",
                    "Description": "Fallback welcome email"
                }
            },
            "DefaultLayouts": {
                "EmailDefault": {
                    "LayoutName": "EmailDefault",
                    "LayoutType": "Email",
                    "LayoutContent": "<html><body>@Model.Content</body></html>",
                    "IsDefault": true,
                    "Description": "Fallback email layout"
                }
            }
        }
    }
}
```

> 提示：仓库 `docs/NOTIFICATION_TEMPLATES_SAMPLE.json` 提供同样示例，可复制到应用配置。

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
        },
        "Templates": {
            "DefaultTemplates": {
                "UserWelcome": {
                    "TemplateName": "UserWelcome",
                    "TemplateType": 1,
                    "SubjectTemplate": "欢迎来到 @Model.SiteName!",
                    "ContentTemplate": "<h2>欢迎 @Model.UserName!</h2>",
                    "DefaultLayoutName": "EmailDefault"
                }
            },
            "DefaultLayouts": {
                "EmailDefault": {
                    "LayoutName": "EmailDefault",
                    "LayoutType": 1,
                    "LayoutContent": "<html><body>@Model.Content</body></html>",
                    "IsDefault": true
                }
            }
        }
    }
}
```

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
