namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// EmailEnvelope 单元测试 —— 收件人归一化与开发环境重定向
/// </summary>
public class EmailEnvelopeTests
{
    private static EmailMessage BuildOrganisationMail() => new()
    {
        To = [new EmailAddress("claims@insurer.example", "Claims Intake")],
        Cc = [new EmailAddress("adjuster@insurer.example", "A. Adjuster"), new EmailAddress("counsel@firm.example")],
        Bcc = [new EmailAddress("file@ourfirm.example")],
        Subject = "Claim 12345",
        Body = "<p>Please find enclosed.</p>"
    };

    #region Normalize

    [Fact]
    public void Normalize_Should_Keep_All_Fields_When_Nothing_To_Clean()
    {
        // Arrange
        var message = BuildOrganisationMail();

        // Act
        var result = EmailEnvelope.Normalize(message);

        // Assert
        result.To.Count.ShouldBe(1);
        result.Cc.Count.ShouldBe(2);
        result.Bcc.Count.ShouldBe(1);
        result.Subject.ShouldBe("Claim 12345");
        result.Body.ShouldBe("<p>Please find enclosed.</p>");
        result.IsHtml.ShouldBeTrue();
    }

    [Fact]
    public void Normalize_Should_Drop_Blank_Addresses()
    {
        // Arrange
        var message = new EmailMessage
        {
            To = [new EmailAddress("a@example.com"), new EmailAddress("  "), new EmailAddress("")],
            Cc = [new EmailAddress("b@example.com")]
        };

        // Act
        var result = EmailEnvelope.Normalize(message);

        // Assert
        result.To.Select(a => a.Address).ShouldBe(["a@example.com"]);
        result.Cc.Select(a => a.Address).ShouldBe(["b@example.com"]);
    }

    [Fact]
    public void Normalize_Should_Trim_Addresses()
    {
        // Arrange
        var message = new EmailMessage { To = [new EmailAddress("  a@example.com ", "A")] };

        // Act
        var result = EmailEnvelope.Normalize(message);

        // Assert
        result.To.Single().Address.ShouldBe("a@example.com");
        result.To.Single().Name.ShouldBe("A");
    }

    [Fact]
    public void Normalize_Should_Deduplicate_Keeping_The_Most_Visible_Field()
    {
        // Arrange - 同一个地址既是主收件人又出现在抄送/密送里（组织收件箱与具名联系人重叠是常态）
        var message = new EmailMessage
        {
            To = [new EmailAddress("shared@example.com", "Shared")],
            Cc = [new EmailAddress("SHARED@example.com", "Shared Again"), new EmailAddress("other@example.com")],
            Bcc = [new EmailAddress("shared@EXAMPLE.com")]
        };

        // Act
        var result = EmailEnvelope.Normalize(message);

        // Assert
        result.To.Select(a => a.Address).ShouldBe(["shared@example.com"]);
        result.Cc.Select(a => a.Address).ShouldBe(["other@example.com"]);
        result.Bcc.ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_Should_Not_Mutate_The_Input()
    {
        // Arrange - 调用方可能在重试时复用同一份消息
        var message = BuildOrganisationMail();

        // Act
        EmailEnvelope.Normalize(message);

        // Assert
        message.To.Count.ShouldBe(1);
        message.Cc.Count.ShouldBe(2);
        message.Bcc.Count.ShouldBe(1);
        message.Subject.ShouldBe("Claim 12345");
    }

    #endregion

    #region HasNoRecipient

    [Fact]
    public void HasNoRecipient_Should_Be_True_When_All_Fields_Empty()
    {
        EmailEnvelope.HasNoRecipient(new EmailMessage { Subject = "orphan" }).ShouldBeTrue();
    }

    [Theory]
    [InlineData("to")]
    [InlineData("cc")]
    [InlineData("bcc")]
    public void HasNoRecipient_Should_Be_False_When_Any_Field_Has_An_Address(string field)
    {
        // Arrange
        var address = new EmailAddress("a@example.com");
        var message = field switch
        {
            "to" => new EmailMessage { To = [address] },
            "cc" => new EmailMessage { Cc = [address] },
            _ => new EmailMessage { Bcc = [address] }
        };

        // Act & Assert
        EmailEnvelope.HasNoRecipient(message).ShouldBeFalse();
    }

    #endregion

    #region RedirectTo (开发环境重定向)

    [Fact]
    public void RedirectTo_Should_Leave_The_Override_As_The_Only_Address_Anywhere()
    {
        // Arrange - 这是本次改动唯一不可回归的行为：重定向生效时，除该地址外任何地址都不许收到这封信
        var message = BuildOrganisationMail();

        // Act
        var result = EmailEnvelope.RedirectTo(message, "dev@localhost");

        // Assert
        var everyAddress = result.To.Concat(result.Cc).Concat(result.Bcc).Select(a => a.Address).ToList();
        everyAddress.ShouldBe(["dev@localhost"]);
        result.Cc.ShouldBeEmpty();
        result.Bcc.ShouldBeEmpty();
    }

    [Fact]
    public void RedirectTo_Should_Record_The_Original_Recipients_In_The_Subject()
    {
        // Arrange
        var message = BuildOrganisationMail();

        // Act
        var result = EmailEnvelope.RedirectTo(message, "dev@localhost");

        // Assert - 开发者据此看得出这封信本来要发给谁
        result.Subject.ShouldStartWith("[DEV → ");
        result.Subject.ShouldContain("Claims Intake <claims@insurer.example>");
        result.Subject.ShouldContain("adjuster@insurer.example");
        result.Subject.ShouldContain("counsel@firm.example");
        result.Subject.ShouldContain("file@ourfirm.example");
        result.Subject.ShouldEndWith("] Claim 12345");
    }

    [Fact]
    public void RedirectTo_Should_Preserve_Body_And_Attachments()
    {
        // Arrange
        var attachments = new List<EmailAttachment> { EmailAttachment.FromBytes([1, 2, 3], "brief.pdf", "application/pdf") };
        var message = new EmailMessage
        {
            To = [new EmailAddress("a@example.com")],
            Subject = "Subject",
            Body = "plain text",
            IsHtml = false,
            Attachments = attachments
        };

        // Act
        var result = EmailEnvelope.RedirectTo(message, "dev@localhost");

        // Assert
        result.Body.ShouldBe("plain text");
        result.IsHtml.ShouldBeFalse();
        result.Attachments.ShouldBe(attachments);
    }

    [Fact]
    public void RedirectTo_Should_Not_Mutate_The_Input()
    {
        // Arrange
        var message = BuildOrganisationMail();

        // Act
        EmailEnvelope.RedirectTo(message, "dev@localhost");

        // Assert
        message.To.Single().Address.ShouldBe("claims@insurer.example");
        message.Cc.Count.ShouldBe(2);
        message.Bcc.Count.ShouldBe(1);
        message.Subject.ShouldBe("Claim 12345");
    }

    #endregion

    #region Describe

    [Fact]
    public void Describe_Should_Render_Name_And_Address_When_Name_Present()
    {
        var message = new EmailMessage { To = [new EmailAddress("a@example.com", "Alice")] };

        EmailEnvelope.Describe(message).ShouldBe("Alice <a@example.com>");
    }

    [Fact]
    public void Describe_Should_Render_Bare_Address_When_Name_Missing()
    {
        var message = new EmailMessage { To = [new EmailAddress("a@example.com")] };

        EmailEnvelope.Describe(message).ShouldBe("a@example.com");
    }

    [Fact]
    public void Describe_Should_Cap_The_List_So_The_Subject_Cannot_Grow_Unbounded()
    {
        // Arrange - 一次群发的收件人可能上千，摘要会被写进主题
        var message = new EmailMessage
        {
            To = Enumerable.Range(1, 12).Select(i => new EmailAddress($"u{i}@example.com")).ToList()
        };

        // Act
        var description = EmailEnvelope.Describe(message);

        // Assert
        description.ShouldContain("u1@example.com");
        description.ShouldContain("u5@example.com");
        description.ShouldNotContain("u6@example.com");
        description.ShouldEndWith("+7 more");
    }

    [Fact]
    public void Describe_Should_Report_Empty_Envelope()
    {
        EmailEnvelope.Describe(new EmailMessage()).ShouldBe("(no recipient)");
    }

    #endregion
}
