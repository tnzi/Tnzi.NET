using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="CreateNotificationRequest"/> 的可空性契约测试。
///
/// 回归背景：<c>Attachments</c> 曾声明为不可空的 <c>List&lt;FileInfoDto&gt;</c>，
/// ASP.NET Core 的模型验证据此把它当成**必填**，于是不带附件的调用方（绝大多数）
/// 都会收到 400 "The Attachments field is required."，而服务端本就按可空处理
/// （<c>request.Attachments?.Select(...)</c>）—— DTO 声明与服务端语义自相矛盾。
/// 可选集合必须显式可空，必填集合则靠 [Required] 明示，两者都在这里锁住。
/// </summary>
public class CreateNotificationRequestShapeTests
{
    private static NullabilityState WriteStateOf(string propertyName)
    {
        var property = typeof(CreateNotificationRequest).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found.");
        return new NullabilityInfoContext().Create(property).WriteState;
    }

    [Fact]
    public void Attachments_IsNullable_SoItIsNotImplicitlyRequired()
    {
        Assert.Equal(NullabilityState.Nullable, WriteStateOf(nameof(CreateNotificationRequest.Attachments)));
    }

    [Fact]
    public void Recipients_StaysRequired()
    {
        var property = typeof(CreateNotificationRequest).GetProperty(nameof(CreateNotificationRequest.Recipients))!;

        Assert.NotNull(property.GetCustomAttribute<RequiredAttribute>());
    }

    /// <summary>
    /// 不带附件构造的请求可以直接被服务端消费，不会因为 Attachments 为 null 抛异常。
    /// </summary>
    [Fact]
    public void RequestWithoutAttachments_ProjectsToEmptyAttachmentList()
    {
        var request = new CreateNotificationRequest
        {
            Type = NotificationType.Email,
            TemplateName = "WelcomeEmail",
            Recipients = [new RecipientInput { Address = "test@example.com" }]
        };

        Assert.Null(request.Attachments);

        // 与 NotificationService.CreateAsync 中的投影同形
        var attachments = request.Attachments?.Select(a => new Attachment { FileName = a.FileName }).ToList()
            ?? new List<Attachment>();

        Assert.Empty(attachments);
    }
}
