using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Portfolio.Freight.Api.Data;
using Portfolio.Freight.Api.Models;
using Portfolio.Freight.Api.Services;

namespace Portfolio.Freight.Api.Endpoints;

public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class Paging
{
    public const int MaxPageSize = 100;

    public static (int Page, int Size) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 25, 1, MaxPageSize));

    public static async Task<Paged<T>> ToPagedAsync<T>(this IQueryable<T> query, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return new Paged<T>(items, total, p, size);
    }

    public static Paged<T> ToPaged<T>(this IEnumerable<T> source, int? page, int? pageSize)
    {
        var (p, size) = Normalize(page, pageSize);
        var all = source as IReadOnlyList<T> ?? source.ToList();
        return new Paged<T>(all.Skip((p - 1) * size).Take(size).ToList(), all.Count, p, size);
    }
}

/// <summary>Collects field errors and turns them into a 400 ValidationProblem.</summary>
public sealed partial class Checks
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public bool Ok => errors.Count == 0;
    public IResult Problem() => Results.ValidationProblem(errors.ToDictionary(x => x.Key, x => x.Value.ToArray()));

    public void Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
        list.Add(message);
    }

    public Checks Required(string field, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, "Required.");
        else if (value.Trim().Length > max) Add(field, $"Must be {max} characters or fewer.");
        return this;
    }

    public Checks Optional(string field, string? value, int max)
    {
        if (value is not null && value.Trim().Length > max) Add(field, $"Must be {max} characters or fewer.");
        return this;
    }

    public Checks OneOf(string field, string? value, IReadOnlyCollection<string> allowed)
    {
        if (value is null || !allowed.Contains(value)) Add(field, $"Must be one of: {string.Join(", ", allowed)}.");
        return this;
    }

    public Checks Range(string field, int value, int min, int max)
    {
        if (value < min || value > max) Add(field, $"Must be between {min} and {max}.");
        return this;
    }

    public Checks Range(string field, decimal value, decimal min, decimal max)
    {
        if (value < min || value > max) Add(field, $"Must be between {min:N0} and {max:N0}.");
        return this;
    }

    public Checks Email(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && (value.Length > 200 || !EmailPattern().IsMatch(value)))
            Add(field, "Must be a valid email address.");
        return this;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}

public static class Http
{
    public static IResult NotFound(string what) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{what} not found.");

    public static IResult Conflict(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: detail);

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Lane(string origin, string destination) => $"{origin} → {destination}";
}

/// <summary>Maps database outages to 503 and write races to 409, both as ProblemDetails.</summary>
public sealed class DatabaseExceptionHandler(IProblemDetailsService problems, ILogger<DatabaseExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        (int Status, string Title)? mapped = exception switch
        {
            DbUpdateConcurrencyException => (409, "The record was changed by someone else. Reload and try again."),
            DbUpdateException => (409, "The change conflicts with existing data."),
            DbException or TimeoutException => (503, "The database is unavailable. Try again shortly."),
            _ => null
        };
        if (mapped is null) return false;

        logger.LogWarning(exception, "Database error on {Path}", context.Request.Path);
        context.Response.StatusCode = mapped.Value.Status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = mapped.Value.Status, Title = mapped.Value.Title }
        });
    }
}

/// <summary>
/// Data endpoints answer 503 until the database is ready. If startup could not reach it
/// (for example a cold serverless database), this retries initialization at most every 30 seconds.
/// </summary>
public sealed class DatabaseGate(DatabaseStatus status, IServiceProvider services, TimeProvider clock)
{
    private readonly SemaphoreSlim sync = new(1, 1);
    private DateTimeOffset lastAttempt = DateTimeOffset.MinValue;

    public async Task<bool> EnsureReadyAsync(CancellationToken ct)
    {
        if (status.Ready) return true;
        if (clock.GetUtcNow() - lastAttempt < TimeSpan.FromSeconds(30)) return false;

        await sync.WaitAsync(ct);
        try
        {
            if (status.Ready) return true;
            lastAttempt = clock.GetUtcNow();
            await Database.InitializeAsync(services, ct);
            return status.Ready;
        }
        finally
        {
            sync.Release();
        }
    }

    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var gate = context.HttpContext.RequestServices.GetRequiredService<DatabaseGate>();
        if (!await gate.EnsureReadyAsync(context.HttpContext.RequestAborted))
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The database is unavailable. Try again shortly.");
        }
        return await next(context);
    }
}

public sealed record CustomerSnapshot(
    Guid Id,
    string Name,
    string LaneOrigin,
    string LaneDestination,
    string PrimaryLane,
    string Stage,
    int MonthlyLoads,
    decimal MonthlyRevenue,
    decimal MonthlyGrossMargin,
    Guid? OwnerId,
    string? OwnerName,
    DateTime? LastTouchAt,
    int DaysSinceTouch,
    int OpenFollowUps,
    int OverdueFollowUps,
    decimal Score,
    string WhyNow,
    string RecommendedAction);

/// <summary>
/// Computes each customer's last touch and overdue follow-ups from the database, then scores them.
/// The score depends on those aggregates, so ranking happens after the (small, filtered) set is loaded.
/// </summary>
public sealed class CustomerInsights(OpportunityScorer scorer, TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<List<CustomerSnapshot>> SnapshotsAsync(IQueryable<Customer> customers, CancellationToken ct)
    {
        var today = Today;
        var rows = await customers
            .Select(c => new
            {
                c.Id, c.Name, c.LaneOrigin, c.LaneDestination, c.Stage, c.MonthlyLoads, c.MonthlyRevenue,
                c.MonthlyGrossMargin, c.OwnerId, OwnerName = c.Owner != null ? c.Owner.Name : null, c.CreatedAt,
                LastTouchAt = c.Activities.Max(a => (DateTime?)a.OccurredAt),
                OpenFollowUps = c.FollowUps.Count(f => f.Status == FollowUpStatuses.Open),
                OverdueFollowUps = c.FollowUps.Count(f => f.Status == FollowUpStatuses.Open && f.DueOn < today)
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var since = DateOnly.FromDateTime(r.LastTouchAt ?? r.CreatedAt);
            var days = Math.Max(today.DayNumber - since.DayNumber, 0);
            var lane = Http.Lane(r.LaneOrigin, r.LaneDestination);
            var opportunity = scorer.Score(new CustomerAccount(
                r.Id, r.Name, lane, r.MonthlyLoads, r.MonthlyRevenue, r.MonthlyGrossMargin, days, r.Stage, r.OverdueFollowUps));
            return new CustomerSnapshot(r.Id, r.Name, r.LaneOrigin, r.LaneDestination, lane, r.Stage, r.MonthlyLoads,
                r.MonthlyRevenue, r.MonthlyGrossMargin, r.OwnerId, r.OwnerName, r.LastTouchAt, days, r.OpenFollowUps,
                r.OverdueFollowUps, opportunity.PriorityScore, opportunity.WhyNow, opportunity.RecommendedAction);
        }).ToList();
    }

    public static Opportunity ToOpportunity(CustomerSnapshot s) =>
        new(s.Id, s.Name, s.PrimaryLane, s.Score, s.WhyNow, s.RecommendedAction);
}
