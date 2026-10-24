using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Wintech.Hrms.Api.Common.ErrorHandling;

/// <summary>
/// Converts exceptions into the documented error envelope, logs with correlation id
/// and never leaks internals (BRD section 11: no sensitive data in errors/logs).
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    public const string CorrelationItemKey = "CorrelationId";
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
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items[CorrelationItemKey] as string ?? context.TraceIdentifier;
        var status = 500;
        ApiError error;

        switch (exception)
        {
            case ApiException apiException:
                status = apiException.StatusCode;
                error = ApiErrorFactory.Create(context, apiException);
                if (status >= 500) _logger.LogError(exception, "Request failed ({Status}) {Path}", status, context.Request.Path);
                else _logger.LogInformation("Request rejected ({Status}) {Path}: {Detail}", status, context.Request.Path, apiException.Detail);
                break;

            case DbUpdateConcurrencyException:
                status = 409;
                error = new ApiError
                {
                    Status = status,
                    Title = "Conflict",
                    Detail = "The record was modified by another user. Reload and try again.",
                    CorrelationId = correlationId
                };
                _logger.LogWarning(exception, "Concurrency conflict on {Path}", context.Request.Path);
                break;

            case BadHttpRequestException badRequest:
                status = badRequest.StatusCode;
                error = new ApiError
                {
                    Status = status,
                    Title = "Malformed request",
                    Detail = "The request body could not be read.",
                    CorrelationId = correlationId
                };
                break;

            default:
                error = new ApiError
                {
                    Status = status,
                    Title = "Unexpected error",
                    Detail = "An unexpected error occurred. Quote the correlation id when contacting support.",
                    CorrelationId = correlationId
                };
                _logger.LogError(exception, "Unhandled exception on {Method} {Path} ({CorrelationId})",
                    context.Request.Method, context.Request.Path, correlationId);
                break;
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started; rethrowing {Status} error for {Path}", status, context.Request.Path);
            throw exception;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(error);
    }
}

/// <summary>Assigns/echoes X-Correlation-Id and opens a logging scope carrying it.</summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = IsValid(incoming)
            ? incoming!
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.Items[ExceptionHandlingMiddleware.CorrelationItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>Writes the error envelope for framework-generated empty 4xx/5xx responses.</summary>
public static class StatusCodePagesExtensions
{
    public static IApplicationBuilder UseErrorEnvelopeStatusCodes(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async context =>
        {
            var response = context.HttpContext.Response;
            if (response.HasStarted || response.ContentLength is not null || response.StatusCode < 400) return;
            if (response.Headers.ContainsKey("Content-Type")) return;

            var titles = new Dictionary<int, string>
            {
                [400] = "Validation failed",
                [401] = "Authentication required",
                [403] = "Access denied",
                [404] = "Not found",
                [405] = "Method not allowed",
                [429] = "Too many requests"
            };

            var status = response.StatusCode;
            response.ContentType = "application/json";
            await response.WriteAsJsonAsync(new ApiError
            {
                Status = status,
                Title = titles.GetValueOrDefault(status, "Request failed"),
                Detail = status is 401 ? "A valid access token is required."
                    : status is 403 ? "You do not have permission to perform this action."
                    : null,
                CorrelationId = ApiErrorFactory.CorrelationId(context.HttpContext)
            });
        });
}
