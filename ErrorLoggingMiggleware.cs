using System.Diagnostics;

namespace RatingAPI
{
    public class ErrorLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorLoggingMiddleware> _logger;
        private readonly Process process;

        public ErrorLoggingMiddleware(RequestDelegate next, ILogger<ErrorLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            process = Process.GetCurrentProcess();
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                var guid = Guid.NewGuid();
                float before = (float)(Process.GetCurrentProcess().WorkingSet64 / 1000l) / 1000000.0f;
                if (context.Request.Path != "/servername")
                {
                    _logger.LogWarning(null, $"STARTED {guid} {before} GB {context.Request.Path}{context.Request.QueryString}");
                }
                await _next(context);
                if (context.Request.Path != "/servername")
                {
                    _logger.LogWarning(null, $"FINISHED {guid} {before} {(float)(Process.GetCurrentProcess().WorkingSet64 / 1000l) / 1000000.0f} GB {context.Request.Path}{context.Request.QueryString}");
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(null, "LOL500 " + context.Request.Path + context.Request.QueryString);
                throw;
            }
        }
    }
}
