
namespace Tnzi.Tests.Results;

/// <summary>
/// Result扩展方法测试
/// </summary>
public class ResultExtensionsTests
{
    #region Validate Tests

    [Fact]
    public void Validate_WhenResultFailed_ShouldReturnOriginalResult()
    {
        // Arrange
        var originalResult = Result<string>.Failure("Original error", 400);

        // Act
        var result = originalResult.Validate(_ => Result.Success());

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Original error", result.Message);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public void Validate_WhenResultSucceededAndValidatorSucceeds_ShouldReturnSuccess()
    {
        // Arrange
        var originalResult = Result<string>.Success("test-data");

        // Act
        var result = originalResult.Validate(_ => Result.Success());

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("test-data", result.Data);
    }

    [Fact]
    public void Validate_WhenResultSucceededAndValidatorFails_ShouldReturnFailure()
    {
        // Arrange
        var originalResult = Result<string>.Success("test-data");

        // Act
        var result = originalResult.Validate(_ => Result.Failure("Validation failed", 400));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Validation failed", result.Message);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public void Validate_WhenDataIsNull_ShouldStillRunValidatorWithNull()
    {
        // Arrange: "success but null" is a legal state - the validator must still run and receive null.
        var originalResult = Result<string?>.Success(null);
        bool validatorCalled = false;
        string? observed = "sentinel";

        // Act
        var result = originalResult.Validate(data =>
        {
            validatorCalled = true;
            observed = data;
            return Result.Success();
        });

        // Assert: validator ran with null, and a passing validator keeps the success.
        Assert.True(validatorCalled);
        Assert.Null(observed);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenDataIsNullAndValidatorFails_ShouldReturnFailure()
    {
        // Arrange: a "must be non-null" style validator can now flag null instead of it being skipped.
        var originalResult = Result<string?>.Success(null);

        // Act
        var result = originalResult.Validate(data =>
            data is null ? Result.Failure("Value is required", 400) : Result.Success());

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Value is required", result.Message);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public void Validate_WithSimpleValidator_WhenValidationFails_ShouldReturnFailure()
    {
        // Arrange
        var originalResult = Result<string>.Success("ab"); // Length is 2, less than 3

        // Act
        var result = originalResult.Validate(data => data.Length < 3 ? "Too short" : null);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Too short", result.Message);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public void Validate_WithSimpleValidator_WhenValidationSucceeds_ShouldReturnSuccess()
    {
        // Arrange
        var originalResult = Result<string>.Success("test-data");

        // Act
        var result = originalResult.Validate(data => data.Length < 3 ? "Too short" : null);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("test-data", result.Data);
    }

    #endregion

    #region FromException Tests

    [Fact]
    public void FromException_WithBusinessException_ShouldReturnFailureWithStatusCode()
    {
        // Arrange
        var exception = new BusinessException("Business error", ErrorCodes.RESOURCE_NOT_FOUND, 404);

        // Act
        var result = ResultExtensions.FromException(exception);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Business error", result.Message);
        Assert.Equal(404, result.Code);
        Assert.Equal(ErrorCodes.RESOURCE_NOT_FOUND, result.ErrorCode);
    }

    [Fact]
    public void FromException_WithTnziException_ShouldReturnFailureWithStatusCode500()
    {
        // Arrange
        var exception = new InfrastructureException("Config", "Configuration error");

        // Act
        var result = ResultExtensions.FromException(exception);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Configuration error", result.Message);
        Assert.Equal(500, result.Code);
        Assert.Equal(exception.Code, result.ErrorCode);
    }

    [Fact]
    public void FromException_WithBusinessException_Generic_ShouldReturnFailureWithStatusCode()
    {
        // Arrange
        var exception = new BusinessException("Business error", ErrorCodes.RESOURCE_NOT_FOUND, 404);

        // Act
        var result = ResultExtensions.FromException<string>(exception);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Business error", result.Message);
        Assert.Equal(404, result.Code);
        Assert.Equal(ErrorCodes.RESOURCE_NOT_FOUND, result.ErrorCode);
    }

    [Fact]
    public void FromException_WithGenericException_ShouldReturnFailureWithStatusCode500()
    {
        // Arrange
        var exception = new InvalidOperationException("Generic error");

        // Act
        var result = ResultExtensions.FromException(exception);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("Generic error", result.Message);
        Assert.Equal(500, result.Code);
        Assert.Equal(ErrorCodes.INTERNAL_SERVER_ERROR, result.ErrorCode);
    }


    #endregion

    #region Combine Tests

    [Fact]
    public void Combine_WhenAllResultsSucceed_ShouldReturnSuccess()
    {
        // Arrange
        var results = new[]
        {
            Result.Success(),
            Result.Success(),
            Result.Success()
        };

        // Act
        var result = ResultExtensions.Combine(results);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Combine_WhenOneResultFails_ShouldReturnFirstFailure()
    {
        // Arrange
        var results = new[]
        {
            Result.Success(),
            Result.Failure("First error", 400),
            Result.Failure("Second error", 500)
        };

        // Act
        var result = ResultExtensions.Combine(results);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal("First error", result.Message);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public void Combine_WhenResultsIsEmpty_ShouldReturnSuccess()
    {
        // Arrange
        var results = Array.Empty<Result>();

        // Act
        var result = ResultExtensions.Combine(results);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Combine_WhenResultsIsNull_ShouldReturnSuccess()
    {
        // Act
        var result = ResultExtensions.Combine((Result[])null!);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Combine_WithIEnumerable_WhenAllResultsSucceed_ShouldReturnSuccess()
    {
        // Arrange
        var results = new List<Result>
        {
            Result.Success(),
            Result.Success(),
            Result.Success()
        };

        // Act
        var result = ResultExtensions.Combine(results);

        // Assert
        Assert.True(result.Succeeded);
    }

    #endregion

    #region GetFailures Tests

    [Fact]
    public void GetFailures_WhenAllResultsSucceed_ShouldReturnEmptyList()
    {
        // Arrange
        var results = new[]
        {
            Result.Success(),
            Result.Success(),
            Result.Success()
        };

        // Act
        var failures = ResultExtensions.GetFailures(results);

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void GetFailures_WhenSomeResultsFail_ShouldReturnFailures()
    {
        // Arrange
        var results = new[]
        {
            Result.Success(),
            Result.Failure("Error 1", 400),
            Result.Failure("Error 2", 500),
            Result.Success()
        };

        // Act
        var failures = ResultExtensions.GetFailures(results);

        // Assert
        Assert.Equal(2, failures.Count);
        Assert.Equal("Error 1", failures[0].Message);
        Assert.Equal("Error 2", failures[1].Message);
    }

    [Fact]
    public void GetFailures_WhenResultsIsEmpty_ShouldReturnEmptyList()
    {
        // Arrange
        var results = Array.Empty<Result>();

        // Act
        var failures = ResultExtensions.GetFailures(results);

        // Assert
        Assert.Empty(failures);
    }

    #endregion

    #region OnSuccess Tests

    [Fact]
    public void OnSuccess_WhenResultSucceeded_ShouldExecuteAction()
    {
        // Arrange
        var result = Result<string>.Success("test-data");
        var actionExecuted = false;
        string? executedData = null;

        // Act
        var returnedResult = result.OnSuccess(data =>
        {
            actionExecuted = true;
            executedData = data;
        });

        // Assert
        Assert.True(actionExecuted);
        Assert.Equal("test-data", executedData);
        Assert.Same(result, returnedResult);
    }

    [Fact]
    public void OnSuccess_WhenResultFailed_ShouldNotExecuteAction()
    {
        // Arrange
        var result = Result<string>.Failure("Error", 400);
        var actionExecuted = false;

        // Act
        var returnedResult = result.OnSuccess(_ => actionExecuted = true);

        // Assert
        Assert.False(actionExecuted);
        Assert.Same(result, returnedResult);
    }

    [Fact]
    public void OnSuccess_WhenResultSucceededButDataIsNull_ShouldExecuteActionWithNull()
    {
        // Arrange: "success but null" is a legal state - the action must run and receive null.
        var result = Result<string?>.Success(null);
        var actionExecuted = false;
        string? observed = "sentinel";

        // Act
        var returnedResult = result.OnSuccess(data =>
        {
            actionExecuted = true;
            observed = data;
        });

        // Assert
        Assert.True(actionExecuted);
        Assert.Null(observed);
        Assert.Same(result, returnedResult);
    }

    #endregion

    #region OnFailure Tests

    [Fact]
    public void OnFailure_WhenResultFailed_ShouldExecuteAction()
    {
        // Arrange
        var result = Result<string>.Failure("Error message", 400, "ERROR_CODE");
        var actionExecuted = false;
        string? executedMessage = null;
        string? executedErrorCode = null;

        // Act
        var returnedResult = result.OnFailure((message, errorCode) =>
        {
            actionExecuted = true;
            executedMessage = message;
            executedErrorCode = errorCode;
        });

        // Assert
        Assert.True(actionExecuted);
        Assert.Equal("Error message", executedMessage);
        Assert.Equal("ERROR_CODE", executedErrorCode);
        Assert.Same(result, returnedResult);
    }

    [Fact]
    public void OnFailure_WhenResultSucceeded_ShouldNotExecuteAction()
    {
        // Arrange
        var result = Result<string>.Success("test-data");
        var actionExecuted = false;

        // Act
        var returnedResult = result.OnFailure((_, _) => actionExecuted = true);

        // Assert
        Assert.False(actionExecuted);
        Assert.Same(result, returnedResult);
    }

    #endregion
}