using Microsoft.Extensions.Http.Resilience;
using Portfolio.Freight.Api.Integrations.Alvys;
using Portfolio.Freight.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.Configure<AlvysOptions>(builder.Configuration.GetSection(AlvysOptions.Section));
builder.Services.AddSingleton<FreightStore>();
builder.Services.AddSingleton<OpportunityScorer>();
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

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "freight-ops" }));

app.MapGet("/api/dashboard", (FreightStore store, OpportunityScorer scorer) =>
{
    var customers = store.Customers;
    var opportunities = customers
        .Select(scorer.Score)
        .OrderByDescending(x => x.PriorityScore)
        .Take(5)
        .ToArray();

    return Results.Ok(new
    {
        activeCustomers = customers.Count,
        openOpportunities = opportunities.Length,
        monthlyLoads = customers.Sum(x => x.MonthlyLoads),
        grossMargin = customers.Sum(x => x.MonthlyGrossMargin),
        followUpsDue = customers.Count(x => x.DaysSinceTouch >= 7),
        topOpportunities = opportunities
    });
});

app.MapGet("/api/customers", (FreightStore store, OpportunityScorer scorer) =>
    Results.Ok(store.Customers.Select(c => new
    {
        c.Id,
        c.Name,
        c.PrimaryLane,
        c.MonthlyLoads,
        c.MonthlyRevenue,
        c.MonthlyGrossMargin,
        c.DaysSinceTouch,
        c.Stage,
        score = scorer.Score(c).PriorityScore
    }).OrderByDescending(x => x.score)));

app.MapGet("/api/opportunities", (FreightStore store, OpportunityScorer scorer) =>
    Results.Ok(store.Customers.Select(scorer.Score).OrderByDescending(x => x.PriorityScore)));

app.MapGet("/api/loads", async (IExternalLoadReader loads, CancellationToken ct) =>
{
    var result = await loads.GetVisibleLoadsAsync(ct);
    return Results.Ok(result);
});

app.MapGet("/api/integrations/alvys/status", (IExternalLoadReader loads) =>
    Results.Ok(loads.Status));

app.Run();
