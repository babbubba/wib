using Microsoft.AspNetCore.Mvc;
using WIB.Application.Common;
using WIB.API.Middleware;
using static WIB.Application.Common.ResultHelpers;

namespace WIB.API.Controllers;

/// <summary>
/// Base controller that provides standardized Result<T> to HTTP response conversion
/// and common functionality for all API controllers
/// </summary>
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Converts a Result<T> into an appropriate HTTP response
    /// </summary>
    protected IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return result.Value switch
            {
                null => NotFound(),
                _ => Ok(result.Value)
            };
        }

        return HandleFailure(result.Error!);
    }

    /// <summary>
    /// Converts a Result into an appropriate HTTP response
    /// </summary>
    protected IActionResult ToActionResult(Result result)
    {
        if (result.IsSuccess)
            return NoContent();

        return HandleFailure(result.Error!);
    }

    /// <summary>
    /// Converts a Result<T> into a CreatedAtAction response
    /// </summary>
    protected IActionResult ToCreatedResult<T>(Result<T> result, string actionName, object? routeValues = null)
    {
        if (result.IsSuccess)
        {
            return CreatedAtAction(actionName, routeValues, result.Value);
        }

        return HandleFailure(result.Error!);
    }

    /// <summary>
    /// Converts a Result<T> into a specific status code response
    /// </summary>
    protected IActionResult ToActionResult<T>(Result<T> result, int successStatusCode)
    {
        if (result.IsSuccess)
        {
            return successStatusCode switch
            {
                200 => Ok(result.Value),
                201 => StatusCode(201, result.Value),
                202 => StatusCode(202, result.Value),
                204 => NoContent(),
                _ => StatusCode(successStatusCode, result.Value)
            };
        }

        return HandleFailure(result.Error!);
    }

    /// <summary>
    /// Handles different types of failure errors and maps them to appropriate HTTP status codes
    /// </summary>
    private IActionResult HandleFailure(string error)
    {
        // Parse common error patterns to determine appropriate status codes
        var lowerError = error.ToLowerInvariant();

        if (lowerError.Contains("not found") || lowerError.Contains("does not exist"))
            return NotFound(new { error });

        if (lowerError.Contains("validation") || lowerError.Contains("invalid") || lowerError.Contains("required"))
            return BadRequest(new { error });

        if (lowerError.Contains("unauthorized") || lowerError.Contains("access denied"))
            return Unauthorized(new { error });

        if (lowerError.Contains("forbidden") || lowerError.Contains("permission"))
            return Forbid(error);

        if (lowerError.Contains("conflict") || lowerError.Contains("already exists"))
            return Conflict(new { error });

        if (lowerError.Contains("timeout"))
            return StatusCode(408, new { error });

        // Default to BadRequest for business logic failures
        return BadRequest(new { error });
    }

    /// <summary>
    /// Creates a validation error response using the ValidationException
    /// </summary>
    protected IActionResult ValidationError(string field, string message)
    {
        throw new ValidationException(field, message);
    }

    /// <summary>
    /// Creates a validation error response with multiple field errors
    /// </summary>
    protected IActionResult ValidationError(Dictionary<string, string[]> errors)
    {
        throw new ValidationException(errors);
    }

    /// <summary>
    /// Validates a model and throws ValidationException if invalid
    /// </summary>
    protected void ValidateModel()
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value!.Errors.Count > 0)
                .ToDictionary(
                    x => x.Key,
                    x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            
            throw new ValidationException(errors);
        }
    }

    /// <summary>
    /// Validates that a GUID parameter is not empty
    /// </summary>
    protected Result ValidateId(Guid id, string parameterName = "id")
    {
        return id == Guid.Empty 
            ? Result.Failure($"Parameter '{parameterName}' cannot be empty") 
            : Result.Success();
    }

    /// <summary>
    /// Validates that a required string parameter is not null or empty
    /// </summary>
    protected Result ValidateRequired(string? value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Result.Failure($"Parameter '{parameterName}' is required")
            : Result.Success();
    }

    /// <summary>
    /// Validates pagination parameters
    /// </summary>
    protected Result<(int Skip, int Take)> ValidatePagination(int skip = 0, int take = 20, int maxTake = 100)
    {
        if (skip < 0)
            return Fail<(int, int)>("Skip parameter cannot be negative");

        if (take <= 0)
            take = 20;

        if (take > maxTake)
            take = maxTake;

        return Result<(int, int)>.Success((skip, take));
    }

    /// <summary>
    /// Executes an async operation and wraps any exceptions in a Result
    /// </summary>
    protected async Task<Result<T>> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        return await TryAsync(operation);
    }

    /// <summary>
    /// Executes an async operation and wraps any exceptions in a Result
    /// </summary>
    protected async Task<Result> ExecuteAsync(Func<Task> operation)
    {
        return await TryAsync(operation);
    }

    /// <summary>
    /// Logs a warning with structured context
    /// </summary>
    protected void LogWarning(string message, params object[] args)
    {
        // Implementation would use ILogger if injected
        // For now, this is a placeholder for consistent logging approach
    }

    /// <summary>
    /// Logs an information message with structured context
    /// </summary>
    protected void LogInformation(string message, params object[] args)
    {
        // Implementation would use ILogger if injected
        // For now, this is a placeholder for consistent logging approach
    }
}