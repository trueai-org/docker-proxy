using Serilog;
using System.Net;
using System.Text.Json;

namespace DockerProxy
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ErrorHandlingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Unhandled exception: {ex.Message}");

                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var result = JsonSerializer.Serialize(new
            {
                error = "An unexpected error occurred.",
                message = exception.Message
            });

            context.Response.Headers.Append("Docker-Distribution-API-Version", "registry/2.0");

            return context.Response.WriteAsync(result);
        }
    }
}