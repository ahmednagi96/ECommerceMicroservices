using System.Threading.RateLimiting;
using BuildingBlocks.Authentication;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Register YARP and read routes/clusters from the "ReverseProxy" config section
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();   // NEW


// NEW: rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 1) Global limit: applies to every request, one counter per client IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        // Never limit health checks
        if (httpContext.Request.Path.StartsWithSegments("/health"))
            return RateLimitPartition.GetNoLimiter("health");

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            });
    });

    // 2) Strict policy, used only by the checkout route
    options.AddPolicy("checkout", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromSeconds(30),
                QueueLimit = 0
            }));

    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }
        return ValueTask.CompletedTask;
    };
});


// NEW: CORS policy for our frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .WithMethods("GET", "POST", "PUT", "DELETE")
        .AllowAnyHeader());
});

builder.AddJwtAuthentication();
var app = builder.Build();
app.MapHealthChecks("/health/live");  // NEW: "is the gateway process alive?"

app.UseCors();          // NEW: before the rate limiter
app.UseAuthentication();   // NEW: fills context.User before the limiter needs it
app.UseRateLimiter();   // NEW: must come before MapReverseProxy
app.UseAuthorization();
// Add the proxy to the request pipeline
app.MapReverseProxy();

app.Run();
static string ClientIp(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
// Logged-in user: one counter per user. Guest: one counter per IP.
static string ClientKey(HttpContext context) =>
    context.User.Identity?.IsAuthenticated == true
        ? $"user:{context.User.GetRequiredUserId()}"
        : $"ip:{ClientIp(context)}";    