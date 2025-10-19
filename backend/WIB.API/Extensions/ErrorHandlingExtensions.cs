using Microsoft.AspNetCore.Mvc;
using WIB.API.Middleware;

namespace WIB.API.Extensions;

/// <summary>
/// Extension methods for configuring unified error handling in the application pipeline
/// </summary>
public static class ErrorHandlingExtensions
{
    /// <summary>
    /// Adds global exception handling middleware to the application pipeline
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }

    /// <summary>
    /// Configures services for enhanced error handling and logging
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEnhancedErrorHandling(this IServiceCollection services)
    {
        // Configure model state error handling to throw ValidationException
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(x => x.Value!.Errors.Count > 0)
                    .ToDictionary(
                        x => x.Key,
                        x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                    );

                throw new ValidationException(errors);
            };
        });

        // Note: Advanced logging configuration would be added here in production
        // For now, using default logging configuration

        return services;
    }
}