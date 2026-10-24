using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.ErrorHandling;

namespace Wintech.Hrms.Api.Common.RateLimiting;

/// <summary>
/// API rate limiting (BRD section 11: anti-automation controls). A global limiter partitions by
/// user-or-IP; the "auth" policy adds a stricter per-IP budget for login/refresh endpoints.
/// </summary>
public static class RateLimitingExtensions
{
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddHrmsRateLimiter(this IServiceCollection services, RateLimitOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                var retryAfter = options.WindowSeconds;
                httpContext.Response.Headers.RetryAfter = retryAfter.ToString();
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsJsonAsync(new ApiError
                {
                    Status = 429,
                    Title = "Too many requests",
                    Detail = $"Rate limit exceeded. Try again in {retryAfter} seconds.",
                    CorrelationId = ApiErrorFactory.CorrelationId(httpContext)
                }, cancellationToken);
            };

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    "general:" + PartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            limiter.AddPolicy(AuthPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    "auth:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(options.AuthWindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    private static string PartitionKey(HttpContext httpContext)
    {
        var user = httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst("sub")?.Value
            : null;
        return user ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    }
}
