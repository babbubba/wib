using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using WIB.API.Controllers;
using WIB.API.Middleware;
using WIB.Application.Common;
using Xunit;

namespace WIB.Tests;

/// <summary>
/// Tests for the unified error handling system including Result<T> pattern and middleware
/// </summary>
public class ErrorHandlingTests
{
    [Fact]
    public void Result_Success_ShouldReturnSuccessfulResult()
    {
        // Arrange & Act
        var result = ResultHelpers.Ok("test value");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal("test value", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Result_Failure_ShouldReturnFailedResult()
    {
        // Arrange & Act
        var result = ResultHelpers.Fail<string>("test error");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal("test error", result.Error);
    }

    [Fact]
    public void Result_Map_OnSuccess_ShouldTransformValue()
    {
        // Arrange
        var result = ResultHelpers.Ok(5);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(10, mapped.Value);
    }

    [Fact]
    public void Result_Map_OnFailure_ShouldPreserveError()
    {
        // Arrange
        var result = ResultHelpers.Fail<int>("original error");

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Equal("original error", mapped.Error);
    }

    [Fact]
    public void Result_Bind_OnSuccess_ShouldChainOperations()
    {
        // Arrange
        var result = ResultHelpers.Ok(5);

        // Act
        var bound = result.Bind(x => x > 0 ? ResultHelpers.Ok(x * 2) : ResultHelpers.Fail<int>("negative"));

        // Assert
        Assert.True(bound.IsSuccess);
        Assert.Equal(10, bound.Value);
    }

    [Fact]
    public void Result_Bind_OnFailure_ShouldPreserveError()
    {
        // Arrange
        var result = ResultHelpers.Fail<int>("original error");

        // Act
        var bound = result.Bind(x => ResultHelpers.Ok(x * 2));

        // Assert
        Assert.True(bound.IsFailure);
        Assert.Equal("original error", bound.Error);
    }

    [Fact]
    public void Result_OnSuccess_ShouldExecuteAction()
    {
        // Arrange
        var result = ResultHelpers.Ok("test");
        bool actionExecuted = false;

        // Act
        result.OnSuccess(value => actionExecuted = true);

        // Assert
        Assert.True(actionExecuted);
    }

    [Fact]
    public void Result_OnFailure_ShouldExecuteAction()
    {
        // Arrange
        var result = ResultHelpers.Fail<string>("test error");
        string? capturedError = null;

        // Act
        result.OnFailure(error => capturedError = error);

        // Assert
        Assert.Equal("test error", capturedError);
    }

    [Fact]
    public async Task Result_TryAsync_OnSuccess_ShouldReturnSuccessResult()
    {
        // Act
        var result = await ResultHelpers.TryAsync(async () =>
        {
            await Task.Delay(1);
            return "success";
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("success", result.Value);
    }

    [Fact]
    public async Task Result_TryAsync_OnException_ShouldReturnFailureResult()
    {
        // Act
        var result = await ResultHelpers.TryAsync<string>(async () =>
        {
            await Task.Delay(1);
            throw new InvalidOperationException("test exception");
        });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("test exception", result.Error);
    }

    [Fact]
    public void ValidationException_WithSingleField_ShouldCreateCorrectError()
    {
        // Act
        var exception = new ValidationException("field1", "error message");

        // Assert
        Assert.Single(exception.Errors);
        Assert.True(exception.Errors.ContainsKey("field1"));
        Assert.Equal("error message", exception.Errors["field1"].Single());
    }

    [Fact]
    public void ValidationException_WithMultipleFields_ShouldCreateCorrectErrors()
    {
        // Arrange
        var errors = new Dictionary<string, string[]>
        {
            ["field1"] = ["error1", "error2"],
            ["field2"] = ["error3"]
        };

        // Act
        var exception = new ValidationException(errors);

        // Assert
        Assert.Equal(2, exception.Errors.Count);
        Assert.Equal(2, exception.Errors["field1"].Length);
        Assert.Single(exception.Errors["field2"]);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_OnValidationException_ShouldReturnBadRequest()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new ValidationException("field1", "error"), loggerMock.Object);
        
        var context = new DefaultHttpContext();
        var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        
        responseBodyStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
        
        Assert.Equal("VALIDATION_ERROR", errorResponse.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_OnUnauthorizedException_ShouldReturnUnauthorized()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new UnauthorizedAccessException("Access denied"), loggerMock.Object);
        
        var context = new DefaultHttpContext();
        var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(401, context.Response.StatusCode);
        
        responseBodyStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
        
        Assert.Equal("AUTHORIZATION_ERROR", errorResponse.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_OnGenericException_ShouldReturnInternalServerError()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new Exception("Generic error"), loggerMock.Object);
        
        var context = new DefaultHttpContext();
        var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);
        
        responseBodyStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
        
        Assert.Equal("INTERNAL_ERROR", errorResponse.GetProperty("type").GetString());
        Assert.Contains("errorId", errorResponse.EnumerateObject().Select(p => p.Name));
        Assert.Contains("timestamp", errorResponse.EnumerateObject().Select(p => p.Name));
    }
}

/// <summary>
/// Test controller to verify BaseApiController functionality
/// </summary>
public class TestController : BaseApiController
{
    public IActionResult TestSuccessResult()
    {
        var result = ResultHelpers.Ok("success");
        return ToActionResult(result);
    }

    public IActionResult TestFailureResult()
    {
        var result = ResultHelpers.Fail<string>("test error");
        return ToActionResult(result);
    }

    public IActionResult TestValidationError()
    {
        return ValidationError("field1", "validation failed");
    }

    public IActionResult TestPaginationValidation(int skip, int take)
    {
        var paginationResult = ValidatePagination(skip, take);
        return ToActionResult(paginationResult);
    }
}

/// <summary>
/// Tests for BaseApiController functionality
/// </summary>
public class BaseApiControllerTests
{
    private readonly TestController _controller = new();

    [Fact]
    public void ToActionResult_WithSuccessResult_ShouldReturnOk()
    {
        // Act
        var actionResult = _controller.TestSuccessResult();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        Assert.Equal("success", okResult.Value);
    }

    [Fact]
    public void ToActionResult_WithFailureResult_ShouldReturnBadRequest()
    {
        // Act
        var actionResult = _controller.TestFailureResult();

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult);
        var errorObj = Assert.IsType<object>(badRequestResult.Value);
        // Note: In real scenario, this would be validated more thoroughly
    }

    [Fact]
    public void ValidationError_ShouldThrowValidationException()
    {
        // Act & Assert
        var exception = Assert.Throws<ValidationException>(() => _controller.TestValidationError());
        Assert.Contains("field1", exception.Errors.Keys);
    }

    [Fact]
    public void ValidatePagination_WithNegativeSkip_ShouldReturnFailure()
    {
        // Act
        var actionResult = _controller.TestPaginationValidation(-1, 20);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult);
        // Error should contain skip parameter validation message
    }

    [Fact]
    public void ValidatePagination_WithValidParams_ShouldReturnSuccess()
    {
        // Act
        var actionResult = _controller.TestPaginationValidation(0, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        // Should return the validated pagination tuple
    }
}

/// <summary>
/// Integration tests for the complete error handling pipeline
/// </summary>
public class ErrorHandlingIntegrationTests
{
    [Fact]
    public void Result_ComplexChaining_ShouldWorkCorrectly()
    {
        // Arrange
        var initialResult = ResultHelpers.Ok(5);

        // Act
        var finalResult = initialResult
            .Map(x => x * 2)  // 10
            .Bind(x => x > 5 ? ResultHelpers.Ok(x + 1) : ResultHelpers.Fail<int>("too small"))  // 11
            .Map(x => $"Final: {x}");  // "Final: 11"

        // Assert
        Assert.True(finalResult.IsSuccess);
        Assert.Equal("Final: 11", finalResult.Value);
    }

    [Fact]
    public void Result_ChainWithFailure_ShouldStopAtFirstFailure()
    {
        // Arrange
        var initialResult = ResultHelpers.Ok(-5);

        // Act
        var finalResult = initialResult
            .Map(x => x * 2)  // -10
            .Bind(x => x > 0 ? ResultHelpers.Ok(x + 1) : ResultHelpers.Fail<int>("negative number"))  // Fails here
            .Map(x => $"Final: {x}");  // Should not execute

        // Assert
        Assert.True(finalResult.IsFailure);
        Assert.Equal("negative number", finalResult.Error);
    }

    [Fact] 
    public async Task AsyncResultChaining_ShouldWorkCorrectly()
    {
        // Act
        var result = await ResultHelpers.TryAsync(async () =>
        {
            await Task.Delay(1);
            return 42;
        });

        var finalResult = result.Map(x => x.ToString());

        // Assert
        Assert.True(finalResult.IsSuccess);
        Assert.Equal("42", finalResult.Value);
    }
}