using Microsoft.AspNetCore.Mvc;

namespace Wintech.Hrms.Api.Common.ErrorHandling;

/// <summary>
/// The single error envelope returned by the API for every non-2xx response
/// (see docs/API_CONTRACT.md section 1).
/// </summary>
public sealed class ApiError
{
    public int Status { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
    public string? CorrelationId { get; init; }
}

/// <summary>Base exception carrying an HTTP status and the contract error envelope.</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public string Title { get; }
    public string? Detail { get; }
    public Dictionary<string, string[]>? Errors { get; }

    public ApiException(int statusCode, string title, string? detail = null,
        Dictionary<string, string[]>? errors = null, Exception? inner = null)
        : base(detail ?? title, inner)
    {
        StatusCode = statusCode;
        Title = title;
        Detail = detail;
        Errors = errors;
    }
}

/// <summary>404 — record not found or outside the caller's scope (no existence leak).</summary>
public sealed class NotFoundException(string? detail = null)
    : ApiException(404, "Not found", detail ?? "The requested resource was not found.");

/// <summary>403 — authenticated but not permitted (permission, record scope, inactive account).</summary>
public sealed class ForbiddenException(string? detail = null)
    : ApiException(403, "Access denied", detail ?? "You do not have permission to perform this action.");

/// <summary>401 — missing/expired token or invalid credentials.</summary>
public sealed class UnauthorizedException(string? detail = null)
    : ApiException(401, "Authentication required", detail ?? "The credentials provided are not valid.");

/// <summary>409 — invalid state transition or concurrency conflict.</summary>
public sealed class ConflictException(string? detail = null)
    : ApiException(409, "Conflict", detail ?? "The request conflicts with the current state of the resource.");

/// <summary>400 — request validation failed; field errors are returned in <see cref="ApiError.Errors"/>.</summary>
public sealed class RequestValidationException(string? detail = null, Dictionary<string, string[]>? errors = null)
    : ApiException(400, "Validation failed", detail ?? "The request has validation errors.", errors);

/// <summary>422 — a business rule rejected the request (leave balance, self-approval, window, ...).</summary>
public sealed class BusinessRuleException(string? detail = null, Dictionary<string, string[]>? errors = null)
    : ApiException(422, "Business rule not satisfied", detail ?? "The request violates a business rule.", errors);

public static class ApiErrorFactory
{
    public static string? CorrelationId(HttpContext context) =>
        context.Items["CorrelationId"] as string ?? context.TraceIdentifier;

    public static ApiError Create(HttpContext context, ApiException ex) => new()
    {
        Status = ex.StatusCode,
        Title = ex.Title,
        Detail = ex.Detail,
        Errors = ex.Errors,
        CorrelationId = CorrelationId(context)
    };

    public static ApiError Validation(HttpContext context, Dictionary<string, string[]> errors) => new()
    {
        Status = 400,
        Title = "Validation failed",
        Detail = "The request has validation errors.",
        Errors = errors.Count > 0 ? errors : null,
        CorrelationId = CorrelationId(context)
    };
}
