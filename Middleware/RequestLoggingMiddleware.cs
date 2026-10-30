namespace UserManagementAPI.Middleware;

internal sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path;

        context.Response.OnCompleted(() =>
        {
            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode}",
                method,
                path,
                context.Response.StatusCode);

            return Task.CompletedTask;
        });

        await next(context);
    }
}
