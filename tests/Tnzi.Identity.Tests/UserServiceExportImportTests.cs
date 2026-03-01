
namespace Tnzi.Identity.Tests;

/// <summary>
/// Tests for user CSV export/import and login trend functionality
/// </summary>
public class UserServiceExportImportTests
{
    #region ExportUsersCsvAsync Tests

    [Fact]
    public void IUserService_ExportUsersCsvAsync_MethodExists()
    {
        // Verify the interface method exists with correct signature
        var method = typeof(IUserService).GetMethod("ExportUsersCsvAsync");
        method.ShouldNotBeNull();
        method.ReturnType.ShouldBe(typeof(Task<Result<string>>));
    }

    [Fact]
    public void IUserService_ImportUsersCsvAsync_MethodExists()
    {
        // Verify the interface method exists with correct signature
        var method = typeof(IUserService).GetMethod("ImportUsersCsvAsync");
        method.ShouldNotBeNull();
        method.ReturnType.ShouldBe(typeof(Task<Result<UserImportResult>>));
    }

    #endregion

    #region UserImportResult DTO Tests

    [Fact]
    public void UserImportResult_DefaultValues_AreZero()
    {
        // Act
        var result = new UserImportResult();

        // Assert
        result.TotalRows.ShouldBe(0);
        result.SuccessCount.ShouldBe(0);
        result.FailedCount.ShouldBe(0);
        result.SkippedCount.ShouldBe(0);
        result.Errors.ShouldNotBeNull();
        result.Errors.Count.ShouldBe(0);
    }

    [Fact]
    public void UserImportResult_CanTrackErrors()
    {
        // Arrange
        var result = new UserImportResult
        {
            TotalRows = 10,
            SuccessCount = 7,
            FailedCount = 2,
            SkippedCount = 1
        };

        // Act
        result.Errors[3] = "Invalid email format";
        result.Errors[7] = "Duplicate username";

        // Assert
        result.Errors.Count.ShouldBe(2);
        result.Errors[3].ShouldBe("Invalid email format");
        result.Errors[7].ShouldBe("Duplicate username");
    }

    #endregion

    #region LoginTrendItem DTO Tests

    [Fact]
    public void LoginTrendItem_DefaultValues_AreZero()
    {
        // Act
        var item = new LoginTrendItem();

        // Assert
        item.Date.ShouldBe(default);
        item.TotalLogins.ShouldBe(0);
        item.SuccessfulLogins.ShouldBe(0);
        item.FailedLogins.ShouldBe(0);
        item.UniqueUsers.ShouldBe(0);
    }

    [Fact]
    public void LoginTrendItem_CanSetValues()
    {
        // Act
        var today = DateTime.UtcNow.Date;
        var item = new LoginTrendItem
        {
            Date = today,
            TotalLogins = 100,
            SuccessfulLogins = 85,
            FailedLogins = 15,
            UniqueUsers = 42
        };

        // Assert
        item.Date.ShouldBe(today);
        item.TotalLogins.ShouldBe(100);
        item.SuccessfulLogins.ShouldBe(85);
        item.FailedLogins.ShouldBe(15);
        item.UniqueUsers.ShouldBe(42);
    }

    #endregion

    #region ILoginLogService GetLoginTrendAsync Tests

    [Fact]
    public void ILoginLogService_GetLoginTrendAsync_MethodExists()
    {
        // Verify the interface method exists
        var method = typeof(ILoginLogService).GetMethod("GetLoginTrendAsync");
        method.ShouldNotBeNull();
    }

    #endregion
}
