using System.Net;

namespace PicLoom.Api.Security;

public sealed class LocalAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, PicLoomOptions options, LocalSessionService sessions)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.XFrameOptions = "DENY";
        response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        response.Headers.CacheControl = "no-store";
        var origin = $"{request.Scheme}://{request.Host}";
        var allowedOrigin = origin == options.Origin || options.AllowDevProxy && origin == PicLoomOptions.DevOrigin;
        var remote = context.Connection.RemoteIpAddress;
        if (!allowedOrigin || remote is not null && !IPAddress.IsLoopback(remote)
            || request.Headers["Sec-Fetch-Site"] == "cross-site"
            || request.Headers.TryGetValue("Origin", out var source) && source != origin)
        {
            await Reject(context, 403, "forbidden");
            return;
        }
        if (request.Path.StartsWithSegments("/api") && request.Path != "/api/session/bootstrap")
        {
            if (!sessions.TryGetSession(request.Cookies[LocalSessionService.CookieName], out var csrf))
            {
                await Reject(context, 401, "unauthorized");
                return;
            }
            if (request.Method is not ("GET" or "HEAD" or "OPTIONS")
                && !LocalSessionService.ValidCsrf(request.Headers["X-PicLoom-CSRF"].ToString(), csrf))
            {
                await Reject(context, 403, "forbidden");
                return;
            }
            context.Items["csrf"] = csrf;
        }
        await next(context);
    }

    private static Task Reject(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { code });
    }
}
