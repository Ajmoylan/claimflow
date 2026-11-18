using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace ClaimFlow.Api.Infrastructure;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>API requests (<c>/api/*</c> and <c>/health</c>, the paths that reach the database), per client IP, per minute.</summary>
    public int RequestsPerMinute { get; set; } = 120;

    /// <summary>
    /// Page requests (the React app's HTML for client-side routes, <c>/docs</c>, <c>/openapi</c>), per client IP, per minute.
    /// Counted separately, so a client that has used up its API allowance can still load the app and see a friendly message.
    /// Built static assets (JS, CSS, images) are served before the limiter and are not counted.
    /// </summary>
    public int PageRequestsPerMinute { get; set; } = 300;

    /// <summary>Creates and status changes, per client IP, per minute.</summary>
    public int WritesPerMinute { get; set; } = 10;

    /// <summary>Creates and status changes from all clients together, per minute. Caps the total write load on the demo.</summary>
    public int GlobalWritesPerMinute { get; set; } = 60;
}

/// <summary>
/// ASP.NET Core's built-in rate limiter, set up as three chained fixed-window limits:
/// requests per client (with separate allowances for the API and for pages), writes per client,
/// and writes from everyone combined.
/// Behind App Service, ASPNETCORE_FORWARDEDHEADERS_ENABLED makes RemoteIpAddress the real client.
/// IP addresses are only in-memory partition keys; they are never logged or stored.
/// </summary>
public static class RateLimiting
{
    public static IServiceCollection AddClaimFlowRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsApi(context)
                    ? RateLimitPartition.GetFixedWindowLimiter("api|" + ClientKey(context), _ => PerMinute(options.RequestsPerMinute))
                    : RateLimitPartition.GetFixedWindowLimiter("page|" + ClientKey(context), _ => PerMinute(options.PageRequestsPerMinute))),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsWrite(context)
                    ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => PerMinute(options.WritesPerMinute))
                    : RateLimitPartition.GetNoLimiter(string.Empty)),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsWrite(context)
                    ? RateLimitPartition.GetFixedWindowLimiter("all-writes", _ => PerMinute(options.GlobalWritesPerMinute))
                    : RateLimitPartition.GetNoLimiter(string.Empty)));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "This public demo limits how fast requests can be made. Please wait a moment and try again.",
                    },
                });
            };
        });

        return services;
    }

    /// <summary>The API and the health check: the requests that do database work.</summary>
    internal static bool IsApi(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    internal static bool IsWrite(HttpContext context) =>
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        !HttpMethods.IsOptions(context.Request.Method);

    private static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
