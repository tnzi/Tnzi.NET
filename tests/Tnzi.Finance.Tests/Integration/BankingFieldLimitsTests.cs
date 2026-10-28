namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 银行域里人手工填写的字符串字段：超过列宽时 400 点名字段、什么都不写。
/// </summary>
/// <remarks>
/// 测试库是 SQLite，不检查列宽，原样赋值的超长值在这里「保存成功」，而在 SQL Server / PostgreSQL 上
/// 是一次不点名的截断异常、界面上一个 500。所以这里断言的是服务层的拒绝，而不是数据库的。
/// </remarks>
public class BankingFieldLimitsTests : FinanceIntegrationTestBase
{
    private static string Long(int length) => new('x', length);

    private async Task<Guid> BankAsync()
    {
        var ledger = await AccountIdByCodeAsync("1120");
        var result = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledger, Name = "Operating", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021",
            AccountNumber = "111222333", EftOriginatorId = "123456789", EftOriginatorName = "ACME CORP"
        }));
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Id;
    }

    [Fact]
    public async Task BankAccount_CreateWithAnOverLongName_Returns400_AndWritesNothing()
    {
        await SeedCoaAsync();
        var ledger = await AccountIdByCodeAsync("1120");

        var result = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.CreateAsync(new CreateBankAccountDto
        {
            AccountId = ledger, Name = Long(BankingFieldLimits.BankAccountNameMaxLength + 1), Scheme = BankNumberScheme.UsAba
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("bank account name");
        (await InScopeAsync<IReadOnlyRepository<BankAccount, Guid>, int>(r => r.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task BankAccount_UpdateWithAnOverLongOriginatorName_Returns400_AndKeepsTheOldValue()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();

        var result = await InScopeAsync<IBankAccountService, Result<BankAccountDto>>(s => s.UpdateAsync(bank, new UpdateBankAccountDto
        {
            Name = "Operating", Scheme = BankNumberScheme.UsAba, RoutingNumber = "021000021",
            EftOriginatorId = "123456789", EftOriginatorName = Long(BankingFieldLimits.EftOriginatorNameMaxLength + 1)
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("EFT originator name");
        (await ReloadAsync<BankAccount>(bank))!.EftOriginatorName.ShouldBe("ACME CORP");
    }

    [Fact]
    public async Task PartyBankAccount_WithOverLongNotes_Returns400_AndWritesNothing()
    {
        await SeedCoaAsync();
        var vendor = await InScopeAsync<IVendorService, Result<VendorDto>>(s => s.CreateAsync(new CreateVendorDto { Name = "Acme" }));

        var result = await InScopeAsync<IPartyBankAccountService, Result<PartyBankAccountDto>>(s => s.CreateAsync(new SavePartyBankAccountDto
        {
            PartyType = FinancePartyType.Vendor, PartyId = vendor.Data!.Id, Scheme = BankNumberScheme.UsAba,
            Notes = Long(BankingFieldLimits.PartyBankNotesMaxLength + 1)
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("notes");
        (await InScopeAsync<IReadOnlyRepository<PartyBankAccount, Guid>, int>(r => r.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task BankRule_WithAnOverLongConditionValue_Returns400_AndWritesNothing()
    {
        await SeedCoaAsync();

        var result = await InScopeAsync<IBankRuleService, Result<BankRuleDto>>(s => s.CreateAsync(new CreateBankRuleDto
        {
            Name = "Coffee", Direction = BankRuleDirection.Any, MatchMode = BankRuleMatchMode.All, DocType = BankFeedDocType.Expense,
            Conditions =
            [
                new CreateBankRuleConditionDto
                {
                    Field = BankRuleField.Description, Operator = BankRuleOperator.Contains,
                    Value = Long(BankingFieldLimits.BankRuleConditionValueMaxLength + 1)
                }
            ]
        }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Condition 1");
        (await InScopeAsync<IReadOnlyRepository<BankRule, Guid>, int>(r => r.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Check_VoidSpoilAndManualRegistration_RejectOverLongText()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var registered = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 10, PayeeName = "Cash", Amount = 5m, IssueDate = new DateTime(2026, 7, 1)
        }));
        registered.Succeeded.ShouldBeTrue(registered.Message);

        var voided = await InScopeAsync<ICheckService, Result<BankCheckDto>>(
            s => s.VoidAsync(registered.Data!.Id, new VoidCheckDto { Reason = Long(BankingFieldLimits.VoidReasonMaxLength + 1) }));
        voided.Code.ShouldBe(400);
        voided.Message!.ShouldContain("reason");
        (await ReloadAsync<BankCheck>(registered.Data!.Id))!.Status.ShouldBe(CheckStatus.Issued);

        var spoiled = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.SpoilAsync(new SpoilCheckDto
        {
            BankAccountId = bank, CheckNumber = 11, Reason = Long(BankingFieldLimits.VoidReasonMaxLength + 1)
        }));
        spoiled.Code.ShouldBe(400);

        var manual = await InScopeAsync<ICheckService, Result<BankCheckDto>>(s => s.RegisterManualAsync(new RegisterManualCheckDto
        {
            BankAccountId = bank, CheckNumber = 12, PayeeName = Long(BankingFieldLimits.CheckPayeeNameMaxLength + 1), Amount = 5m,
            IssueDate = new DateTime(2026, 7, 1)
        }));
        manual.Code.ShouldBe(400);
        manual.Message!.ShouldContain("payee name");

        (await InScopeAsync<IReadOnlyRepository<BankCheck, Guid>, int>(r => r.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task EftBatch_VoidWithAnOverLongReason_Returns400()
    {
        await SeedCoaAsync();
        var bank = await BankAsync();
        var batchId = Guid.NewGuid();
        await InScopeAsync<IRepository<EftBatch, Guid>, int>(async repo =>
        {
            await repo.InsertAsync(new EftBatch
            {
                Id = batchId, BankAccountId = bank, Format = EftFileFormat.Nacha, Currency = "USD",
                EffectiveDate = new DateTime(2026, 7, 1), Status = EftBatchStatus.Draft
            });
            await repo.SaveChangesAsync();
            return 0;
        });

        var voided = await InScopeAsync<IEftService, Result<EftBatchDto>>(
            s => s.VoidBatchAsync(batchId, new VoidEftBatchDto { Reason = Long(BankingFieldLimits.VoidReasonMaxLength + 1) }));

        voided.Code.ShouldBe(400);
        (await ReloadAsync<EftBatch>(batchId))!.Status.ShouldBe(EftBatchStatus.Draft);
    }

    /// <summary>服务层的上限与实体配置的列宽是同一组常量（各写一份数字的话，漂移的症状是 500 而不是编译失败）。</summary>
    [Theory]
    [InlineData(typeof(BankAccount), nameof(BankAccount.Name), BankingFieldLimits.BankAccountNameMaxLength)]
    [InlineData(typeof(BankAccount), nameof(BankAccount.BankName), BankingFieldLimits.BankNameMaxLength)]
    [InlineData(typeof(BankAccount), nameof(BankAccount.Currency), BankingFieldLimits.CurrencyMaxLength)]
    [InlineData(typeof(BankAccount), nameof(BankAccount.CheckTemplateName), BankingFieldLimits.CheckTemplateNameMaxLength)]
    [InlineData(typeof(BankAccount), nameof(BankAccount.FeedProviderKey), BankingFieldLimits.FeedProviderKeyMaxLength)]
    [InlineData(typeof(BankAccount), nameof(BankAccount.ExternalAccountId), BankingFieldLimits.ExternalAccountIdMaxLength)]
    [InlineData(typeof(BankAccount), nameof(BankAccount.EftOriginatorName), BankingFieldLimits.EftOriginatorNameMaxLength)]
    [InlineData(typeof(PartyBankAccount), nameof(PartyBankAccount.Label), BankingFieldLimits.PartyBankLabelMaxLength)]
    [InlineData(typeof(PartyBankAccount), nameof(PartyBankAccount.BankName), BankingFieldLimits.BankNameMaxLength)]
    [InlineData(typeof(PartyBankAccount), nameof(PartyBankAccount.Currency), BankingFieldLimits.CurrencyMaxLength)]
    [InlineData(typeof(PartyBankAccount), nameof(PartyBankAccount.Notes), BankingFieldLimits.PartyBankNotesMaxLength)]
    [InlineData(typeof(BankCheck), nameof(BankCheck.PayeeName), BankingFieldLimits.CheckPayeeNameMaxLength)]
    [InlineData(typeof(BankCheck), nameof(BankCheck.Currency), BankingFieldLimits.CurrencyMaxLength)]
    [InlineData(typeof(BankCheck), nameof(BankCheck.VoidReason), BankingFieldLimits.VoidReasonMaxLength)]
    [InlineData(typeof(BankCheck), nameof(BankCheck.PrintTemplateName), BankingFieldLimits.CheckTemplateNameMaxLength)]
    [InlineData(typeof(EftBatch), nameof(EftBatch.VoidReason), BankingFieldLimits.VoidReasonMaxLength)]
    [InlineData(typeof(BankRule), nameof(BankRule.Name), BankingFieldLimits.BankRuleNameMaxLength)]
    [InlineData(typeof(BankRule), nameof(BankRule.PaymentMethod), BankingFieldLimits.PaymentMethodMaxLength)]
    [InlineData(typeof(BankRuleCondition), nameof(BankRuleCondition.Value), BankingFieldLimits.BankRuleConditionValueMaxLength)]
    public void ConfiguredColumnWidths_MatchTheServiceLimits(Type entity, string property, int expected)
    {
        DbContext.Model.FindEntityType(entity)!.FindProperty(property)!.GetMaxLength().ShouldBe(expected);
    }
}
