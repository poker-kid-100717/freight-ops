using System.Threading.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Portfolio.Freight.Api.Data;
using Portfolio.Freight.Api.Endpoints;
using Portfolio.Freight.Api.Integrations.Alvys;
using Portfolio.Freight.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddFreightDatabase(builder.Configuration);
builder.Services.AddSingleton<DemoSeeder>();
builder.Services.AddSingleton<DatabaseGate>();
builder.Services.AddScoped<CustomerInsights>();
builder.Services.AddSingleton<OpportunityScorer>();

builder.Services.Configure<AlvysOptions>(builder.Configuration.GetSection(AlvysOptions.Section));
builder.Services.AddSingleton<AlvysTokenProvider>();
builder.Services.AddSingleton<IExternalLoadReader, AlvysLoadReader>();

builder.Services.AddHttpClient(AlvysTokenProvider.AuthClient)
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
    });

builder.Services.AddHttpClient(AlvysLoadReader.ApiClient)
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(12);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });

// This is an anonymous public demo: limit writes per client. Cloudflare supplies the caller's IP.
var writesPerMinute = builder.Configuration.GetValue("RateLimiting:WritesPerMinute", 30);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            return RateLimitPartition.GetNoLimiter("reads");
        var client = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                     ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = writesPerMinute,
            Window = TimeSpan.FromMinutes(1)
        });
    });
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

// `dotnet Portfolio.*.Api.dll migrate`: apply migrations and exit (used by the deploy pipeline).
if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await Database.MigrateAsync(app.Services) ? 0 : 1;
    return;
}

await Database.InitializeAsync(app.Services);

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "freight-ops" }));
app.MapGet("/health/ready", async (DatabaseGate gate, DatabaseStatus database, CancellationToken ct) =>
{
    var ready = await gate.EnsureReadyAsync(ct);
    var body = new { status = ready ? "Ready" : "Degraded", database = new { database.Mode, database.Ready, database.Error } };
    return ready ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
});

var api = app.MapGroup("/api");
api.MapPlatformEndpoints();

var data = api.MapGroup("").AddEndpointFilter(DatabaseGate.Filter);
data.MapAccountEndpoints();
data.MapSalesEndpoints();
data.MapInsightEndpoints();

api.MapGet("/loads", async (IExternalLoadReader loads, CancellationToken ct) =>
{
    var result = await loads.GetVisibleLoadsAsync(ct);
    return Results.Ok(result);
});

api.MapGet("/integrations/alvys/status", (IExternalLoadReader loads) =>
    Results.Ok(loads.Status));

app.Run();

public partial class Program;
