namespace Tnzi.AI.Tests;

/// <summary>
/// 配额预警级别测试
/// </summary>
public class QuotaWarningLevelTests
{
    [Fact]
    public void QuotaCheckResult_Allow_WithWarningLevel_SetsCorrectly()
    {
        var result = QuotaCheckResult.Allow(100, 200, 0.85m, 0.70m, QuotaWarningLevel.Warning);

        result.IsAllowed.ShouldBeTrue();
        result.WarningLevel.ShouldBe(QuotaWarningLevel.Warning);
        result.DailyUsagePercentage.ShouldBe(0.85m);
        result.MonthlyUsagePercentage.ShouldBe(0.70m);
    }

    [Fact]
    public void QuotaCheckResult_Deny_SetsCriticalLevel()
    {
        var result = QuotaCheckResult.Deny("Exceeded");

        result.IsAllowed.ShouldBeFalse();
        result.WarningLevel.ShouldBe(QuotaWarningLevel.Critical);
        result.DailyUsagePercentage.ShouldBe(1);
        result.MonthlyUsagePercentage.ShouldBe(1);
    }

    [Fact]
    public void QuotaCheckResult_Allow_Default_NoneLevel()
    {
        var result = QuotaCheckResult.Allow(1000, 5000);

        result.WarningLevel.ShouldBe(QuotaWarningLevel.None);
        result.DailyUsagePercentage.ShouldBe(0);
        result.MonthlyUsagePercentage.ShouldBe(0);
    }

    [Fact]
    public void UserQuotaDto_WarningLevel_CalculatesFromUsage()
    {
        var dto = new UserQuotaDto
        {
            DailyTokenLimit = 100000,
            MonthlyTokenLimit = 3000000,
            CurrentDailyUsage = 85000, // 85% daily
            CurrentMonthlyUsage = 1500000, // 50% monthly
            WarningThreshold = 0.8m,
            CriticalThreshold = 0.95m
        };

        // Max is 85% daily, which exceeds WarningThreshold(0.8) but not CriticalThreshold(0.95)
        dto.WarningLevel.ShouldBe(QuotaWarningLevel.Warning);
    }

    [Fact]
    public void UserQuotaDto_WarningLevel_Critical_WhenMonthlyExceeds()
    {
        var dto = new UserQuotaDto
        {
            DailyTokenLimit = 100000,
            MonthlyTokenLimit = 3000000,
            CurrentDailyUsage = 50000, // 50% daily
            CurrentMonthlyUsage = 2900000, // ~96.7% monthly
            WarningThreshold = 0.8m,
            CriticalThreshold = 0.95m
        };

        dto.WarningLevel.ShouldBe(QuotaWarningLevel.Critical);
    }

    [Fact]
    public void UserQuotaDto_WarningLevel_None_WhenUnderThreshold()
    {
        var dto = new UserQuotaDto
        {
            DailyTokenLimit = 100000,
            MonthlyTokenLimit = 3000000,
            CurrentDailyUsage = 30000, // 30%
            CurrentMonthlyUsage = 500000, // ~16.7%
            WarningThreshold = 0.8m,
            CriticalThreshold = 0.95m
        };

        dto.WarningLevel.ShouldBe(QuotaWarningLevel.None);
    }

    [Fact]
    public void UserQuota_DefaultThresholds_AreCorrect()
    {
        var quota = new UserQuota();
        quota.WarningThreshold.ShouldBe(0.8m);
        quota.CriticalThreshold.ShouldBe(0.95m);
    }

}
