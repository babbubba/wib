using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace WIB.API.Middleware;

/// <summary>
/// Global exception handling middleware that converts unhandled exceptions 
/// into consistent HTTP responses with structured error information
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var errorId = Guid.NewGuid();
        var request = context.Request;
        var user = context.User?.Identity?.Name ?? "Anonymous";

        // Log structured error information
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ErrorId"] = errorId,
            ["UserId"] = user,
            ["RequestPath"] = request.Path,
            ["RequestMethod"] = request.Method,
            ["UserAgent"] = request.Headers["User-Agent"].ToString(),
            ["IPAddress"] = GetClientIpAddress(context)
        }))
        {
            _logger.LogError(exception, "Unhandled exception occurred during request processing");
        }

        var errorResponse = CreateErrorResponse(exception, errorId);
        var statusCode = GetStatusCode(exception);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse, jsonOptions);
        await context.Response.WriteAsync(jsonResponse);
    }

    private static ErrorResponse CreateErrorResponse(Exception exception, Guid errorId)
    {
        return exception switch
        {
            ValidationException validationEx => new ErrorResponse
            {
                ErrorId = errorId,
                Type = "VALIDATION_ERROR",
                Title = "Validation failed",
                Detail = validationEx.Message,
                ValidationErrors = validationEx.Errors
            },
            UnauthorizedAccessException => new ErrorResponse
            {
                ErrorId = errorId,
                Type = "AUTHORIZATION_ERROR", 
                Title = "Access denied",
                Detail = "You are not authorized to perform this action"
            },
            KeyNotFoundException notFoundEx => new ErrorResponse
            {
                ErrorId = errorId,
                Type = "NOT_FOUND",
                Title = "Resource not found",
                Detail = notFoundEx.Message
            },
            InvalidOperationException invalidOpEx => new ErrorResponse
            {
                ErrorId = errorId,
                Type = "INVALID_OPERATION",
                Title = "Invalid operation",
                Detail = invalidOpEx.Message
            },
            ArgumentException argEx => new ErrorResponse
            {
                ErrorId = errorId,
                Type = "INVALID_ARGUMENT",
                Title = "Invalid argument",
                Detail = argEx.Message
            },
            TimeoutException => new ErrorResponse
            {
                ErrorId = errorId,
                Type = "TIMEOUT",
                Title = "Operation timeout", 
                Detail = "The operation took too long to complete"
            },
            _ => new ErrorResponse
            {
                ErrorId = errorId,
                Type = "INTERNAL_ERROR",
                Title = "An unexpected error occurred",
                Detail = "Please try again later or contact support if the problem persists"
            }
        };
    }

    private static HttpStatusCode GetStatusCode(Exception exception)
    {
        return exception switch
        {
            ValidationException => HttpStatusCode.BadRequest,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            KeyNotFoundException => HttpStatusCode.NotFound,
            InvalidOperationException => HttpStatusCode.BadRequest,
            ArgumentException => HttpStatusCode.BadRequest,
            TimeoutException => HttpStatusCode.RequestTimeout,
            _ => HttpStatusCode.InternalServerError
        };
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded IP (when behind proxy/load balancer)
        var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xForwardedFor))
        {
            return xForwardedFor.Split(',')[0].Trim();
        }

        var xRealIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xRealIp))
        {
            return xRealIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}

/// <summary>
/// Structured error response format
/// </summary>
public class ErrorResponse
{
    public Guid ErrorId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}

/// <summary>
/// Custom validation exception with structured error details
/// </summary>
public class ValidationException : Exception
{
    public Dictionary<string, string[]> Errors { get; }

    public ValidationException(Dictionary<string, string[]> errors) 
        : base("One or more validation errors occurred")
    {
        Errors = errors;
    }

    public ValidationException(string field, string error) 
        : base($"Validation failed for {field}: {error}")
    {
        Errors = new Dictionary<string, string[]>
        {
            [field] = [error]
        };
    }

    public ValidationException(string field, string[] errors) 
        : base($"Validation failed for {field}")
    {
        Errors = new Dictionary<string, string[]>
        {
            [field] = errors
        };
    }
}