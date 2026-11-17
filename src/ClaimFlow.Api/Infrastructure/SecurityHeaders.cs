namespace ClaimFlow.Api.Infrastructure;

/// <summary>Adds conservative browser security headers to every response.</summary>
public static class SecurityHeaders
{
    // The React app is served from the same origin and loads no third-party scripts, styles or fonts.
    private const string AppCsp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    // The Scalar API reference page needs inline styles and scripts; it is scoped to /docs only.
    private const string DocsCsp =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";

    public static IApplicationBuilder UseClaimFlowSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers.ContentSecurityPolicy = context.Request.Path.StartsWithSegments("/docs", StringComparison.OrdinalIgnoreCase)
                ? DocsCsp
                : AppCsp;
            await next();
        });
}
